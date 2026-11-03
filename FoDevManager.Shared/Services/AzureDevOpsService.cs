using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FODevManager.Models;
using FODevManager.Messages;
using FODevManager.Utils;

namespace FODevManager.Services;

// Read-only Azure DevOps Services discovery; credentials never go to response-provided URLs
public sealed class AzureDevOpsService(AppConfig config, HttpClient? client = null)
{
    private const int MaximumResponseBytes = 8 * 1024 * 1024;
    private const int MaximumItems = 20000;
    private const int MaximumPages = 100;
    private const int MaximumProfileCandidates = 100;
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient http = client ?? SharedClient;

    public async Task<IReadOnlyList<AzureDevOpsProject>> ListProjectsAsync(string organization, CancellationToken token)
    {
        var root = OrganizationRoot(organization);
        var items = await GetCollectionAsync(root + "_apis/projects", [("$top", "100")], token).ConfigureAwait(false);
        MessageLogger.Info($"Discovered {items.Count} accessible Azure DevOps projects");
        return items.Select(item => new AzureDevOpsProject(Required(item, "id"), Required(item, "name"),
            Optional(item, "state"), Optional(item, "visibility"), root + Uri.EscapeDataString(Required(item, "name")))).ToArray();
    }

    public async Task<IReadOnlyList<AzureDevOpsRepository>> ListRepositoriesAsync(string organization, string? project, CancellationToken token)
    {
        var root = OrganizationRoot(organization);
        if (project != null) ValidateSegment(project, "project");
        var route = root + (project == null ? "" : Uri.EscapeDataString(project) + "/") + "_apis/git/repositories";
        var items = await GetCollectionAsync(route, [], token).ConfigureAwait(false);
        MessageLogger.Info($"Discovered {items.Count} accessible Azure DevOps repositories");
        return items.Select(item => ReadRepository(root, item)).ToArray();
    }

    public async Task<AzureDevOpsRepositoryInspection> InspectRepositoryAsync(string organization, string repositoryId, string? branch, CancellationToken token)
    {
        var root = OrganizationRoot(organization);
        if (!Guid.TryParse(repositoryId, out var id)) throw new ArgumentException("repositoryId must be the Azure DevOps repository GUID returned by repository discovery");
        var route = root + "_apis/git/repositories/" + id.ToString("D");
        var (metadata, _) = await GetJsonAsync(route, [], token).ConfigureAwait(false);
        var repository = ReadRepository(root, metadata);
        if (!repository.Id.Equals(id.ToString("D"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Azure DevOps returned a different repository identity");
        branch ??= repository.DefaultBranch;
        if (branch == null) return new(repository, "no_default_branch", null, null, []);
        if (string.IsNullOrWhiteSpace(branch) || branch.Length > 1024 || branch.Any(char.IsControl))
            throw new ArgumentException("A non-empty branch name is required when branch is supplied");
        var reference = branch.StartsWith("refs/heads/", StringComparison.Ordinal) ? branch : "refs/heads/" + branch;
        var refs = await GetCollectionAsync(route + "/refs", [("filter", reference[5..]), ("$top", "100")], token).ConfigureAwait(false);
        var exact = refs.Where(r => Optional(r, "name") == reference).ToArray();
        if (exact.Length != 1) throw new InvalidOperationException("Requested branch was not found or is not accessible");
        var commit = Required(exact[0], "objectId");
        if (!Regex.IsMatch(commit, "\\A[0-9a-fA-F]{40}\\z")) throw new InvalidOperationException("Azure DevOps returned an invalid branch commit");

        // List the root first to avoid recursively downloading a potentially large FO metadata tree
        var roots = await ItemsAsync(route, "/", "OneLevel", commit, token).ConfigureAwait(false);
        var folders = roots.Where(IsFolder).Select(i => Required(i, "path"))
            .Where(IsArtifactRoot).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var candidates = new List<string>();
        foreach (var folder in folders)
        {
            var children = await ItemsAsync(route, folder, "Full", commit, token).ConfigureAwait(false);
            candidates.AddRange(children.Where(i => !IsFolder(i)).Select(i => Required(i, "path"))
                .Where(p => p.StartsWith(folder + "/", StringComparison.Ordinal) && p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
            if (candidates.Count > MaximumProfileCandidates)
                throw new InvalidOperationException("Repository contains more than 100 profile JSON candidates; inspection was not completed");
        }

        var profiles = new List<AzureDevOpsProfileCandidate>();
        foreach (var path in candidates.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
        {
            MessageLogger.Info($"Inspecting profile JSON candidate {profiles.Count + 1} of {candidates.Count}");
            var (item, _) = await GetJsonAsync(route + "/items", [("path", path), ("includeContent", "true"), ("$format", "json"),
                ("versionDescriptor.version", commit), ("versionDescriptor.versionType", "commit")], token).ConfigureAwait(false);
            if (Optional(item, "path") != path || (Optional(item, "commitId") is string returnedCommit && !returnedCommit.Equals(commit, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Azure DevOps returned content for a different path or commit");
            if (Boolean(item, "isSymLink") == true)
                profiles.Add(new(path, false, null, null, [], ["Symbolic links are not inspected as profile definitions"]));
            else
                profiles.Add(AzureDevOpsProfileReader.Read(path, Required(item, "content", allowEmpty: true)));
        }
        return new(repository, "inspected", reference, commit, profiles);
    }

    private async Task<List<JsonElement>> ItemsAsync(string route, string scope, string recursion, string commit, CancellationToken token)
    {
        var items = await GetCollectionAsync(route + "/items", [("scopePath", scope), ("recursionLevel", recursion),
            ("versionDescriptor.version", commit), ("versionDescriptor.versionType", "commit")], token).ConfigureAwait(false);
        foreach (var item in items)
        {
            var path = Required(item, "path");
            if (!path.StartsWith('/') || path.Any(char.IsControl)) throw new InvalidOperationException("Azure DevOps returned an invalid item path");
            _ = IsFolder(item);
            _ = Boolean(item, "isSymLink");
        }
        return items;
    }

    private async Task<List<JsonElement>> GetCollectionAsync(string route, (string Key, string Value)[] query, CancellationToken token)
    {
        var result = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? continuation = null;
        for (var page = 0; page < MaximumPages; page++)
        {
            var parameters = continuation == null ? query : query.Concat([("continuationToken", continuation)]).ToArray();
            var (body, next) = await GetJsonAsync(route, parameters, token).ConfigureAwait(false);
            if (!body.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Azure DevOps returned an invalid collection response");
            result.AddRange(values.EnumerateArray());
            if (result.Count > MaximumItems) throw new InvalidOperationException("Azure DevOps discovery exceeded 20000 items; narrow the query");
            if (string.IsNullOrEmpty(next)) return result;
            if (!seen.Add(next)) throw new InvalidOperationException("Azure DevOps repeated a continuation token; discovery was not completed");
            continuation = next;
        }
        throw new InvalidOperationException("Azure DevOps discovery exceeded 100 pages; narrow the query");
    }

    private async Task<(JsonElement Body, string? Continuation)> GetJsonAsync(string route, (string Key, string Value)[] query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(config.AzureArtifactsPat))
            throw new InvalidOperationException("Configure AzureArtifactsPat with Code: Read and Project and Team: Read permissions for discovery");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(http.Timeout);
        var requestToken = timeout.Token;
        var parameters = query.Append(("api-version", "7.1"));
        var uri = route + "?" + string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Item1) + "=" + Uri.EscapeDataString(p.Item2)));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + config.AzureArtifactsPat)));
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("Azure DevOps request timed out"); }
        catch (HttpRequestException) { throw new InvalidOperationException("Could not connect to Azure DevOps; check network access and proxy configuration"); }
        using (response)
        {
            if (!response.IsSuccessStatusCode) throw HttpFailure(response.StatusCode);
            var next = response.Headers.TryGetValues("x-ms-continuationtoken", out var tokens) ? tokens.SingleOrDefault() : null;
            if (next?.Length > 4096) throw new InvalidOperationException("Azure DevOps returned an invalid continuation token");
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw ResponseTooLarge();
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var block = new byte[16384];
                int read;
                while ((read = await stream.ReadAsync(block, requestToken).ConfigureAwait(false)) != 0)
                {
                    if (buffer.Length + read > MaximumResponseBytes) throw ResponseTooLarge();
                    buffer.Write(block, 0, read);
                }
                using var document = JsonDocument.Parse(buffer.ToArray());
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                return (document.RootElement.Clone(), next);
            }
            catch (JsonException) { throw new InvalidOperationException("Azure DevOps returned invalid JSON; check authentication and the service response"); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("Azure DevOps request timed out while reading its response"); }
            catch (HttpRequestException) { throw new InvalidOperationException("Azure DevOps response could not be read"); }
        }
    }

    private static InvalidOperationException HttpFailure(HttpStatusCode status) => new(status switch
    {
        HttpStatusCode.Unauthorized => "Azure DevOps authentication failed (401); check AzureArtifactsPat and its expiration",
        HttpStatusCode.Forbidden => "Azure DevOps permission denied (403); check Code: Read, Project and Team: Read, and project access",
        HttpStatusCode.NotFound => "Azure DevOps organization, project, repository, or item was not found (404) or is not accessible",
        HttpStatusCode.TooManyRequests => "Azure DevOps rate limit reached (429); wait before retrying",
        _ when (int)status is >= 300 and < 400 => "Azure DevOps returned a redirect; credentials will not be forwarded to another endpoint",
        _ => $"Azure DevOps request failed with HTTP {(int)status}"
    });

    private static InvalidOperationException ResponseTooLarge() => new("Azure DevOps response exceeded the 8 MiB inspection limit; narrow the query");

    private static string OrganizationRoot(string organization)
    {
        if (string.IsNullOrWhiteSpace(organization) || organization.Length > 50 || !Regex.IsMatch(organization, "\\A[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?\\z"))
            throw new ArgumentException("organization must be an Azure DevOps organization name, for example peritus-no, not a URL");
        return "https://dev.azure.com/" + organization + "/";
    }

    private static void ValidateSegment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.Length > 256 || value.Any(c => char.IsControl(c) || c is '/' or '\\' or '?' or '#'))
            throw new ArgumentException($"A valid {name} name or ID is required");
    }

    private static bool IsFolder(JsonElement item)
    {
        var folder = Boolean(item, "isFolder");
        var kind = Optional(item, "gitObjectType");
        return folder ?? string.Equals(kind, "tree", StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsArtifactRoot(string path) => path.Equals("/Artifact", StringComparison.OrdinalIgnoreCase) || path.Equals("/Artifacts", StringComparison.OrdinalIgnoreCase);

    private static AzureDevOpsRepository ReadRepository(string root, JsonElement item)
    {
        if (!item.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Azure DevOps returned a repository without project information");
        var name = Required(item, "name");
        var projectName = Required(project, "name");
        ValidateSegment(name, "repository");
        ValidateSegment(projectName, "project");
        var url = root + Uri.EscapeDataString(projectName) + "/_git/" + Uri.EscapeDataString(name);
        return new(Required(item, "id"), name, Required(project, "id"), projectName, Optional(item, "defaultBranch"), url, url,
            Boolean(item, "isDisabled") == true);
    }

    private static string Required(JsonElement item, string name, bool allowEmpty = false) =>
        Optional(item, name) is string value && (allowEmpty || !string.IsNullOrWhiteSpace(value))
            ? value : throw new InvalidOperationException($"Azure DevOps response is missing required {name}");

    private static string? Optional(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Azure DevOps response entry must be an object");
        if (!item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidOperationException($"Azure DevOps response contains invalid {name}");
        return value.GetString();
    }

    private static bool? Boolean(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Azure DevOps response entry must be an object");
        if (!item.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException($"Azure DevOps response contains invalid {name}")
        };
    }
}
