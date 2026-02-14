using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Utils;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FODevManager.Services.EasyGit
{
    public sealed class AzureDevOpsPrService : IAzureDevOpsPrService
    {
        private readonly AppConfig _config;
        private readonly HttpClient _httpClient;

        public AzureDevOpsPrService(AppConfig config)
        {
            _config = config;
            _httpClient = new HttpClient();
        }

        public async Task<EasyGitOperationResult> CreatePullRequestAsync(
            RepositoryModel repository,
            string sourceBranch,
            string targetBranch,
            string title,
            string description,
            CancellationToken cancellationToken = default)
        {
            if (repository == null)
                return EasyGitOperationResult.Fail("Repository is not defined.");

            if (!TryParseRepository(repository, out var details, out var parseError))
                return EasyGitOperationResult.Fail(parseError);

            var sourceRef = sourceBranch.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase)
                ? sourceBranch
                : $"refs/heads/{sourceBranch}";

            var targetRef = targetBranch.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase)
                ? targetBranch
                : $"refs/heads/{targetBranch}";

            var createUrl = BuildCreatePrUrl(details.Organization, details.Project, details.RepositoryName, sourceRef, targetRef);

            if (_config.AzureDevOpsPat.IsNullOrEmpty())
            {
                OpenBrowser(createUrl);
                return EasyGitOperationResult.Success("Opened Azure DevOps PR creation page.", createUrl);
            }

            var apiResult = await TryCreatePrByApiAsync(details, sourceRef, targetRef, title, description, cancellationToken).ConfigureAwait(false);
            if (apiResult.Succeeded)
                return apiResult;

            MessageLogger.Warning($"API PR creation failed. Falling back to browser URL. {apiResult.Message}");
            OpenBrowser(createUrl);
            return EasyGitOperationResult.Success("Opened Azure DevOps PR creation page (fallback).", createUrl);
        }

        private async Task<EasyGitOperationResult> TryCreatePrByApiAsync(
            AzureDevOpsRepoDetails details,
            string sourceRef,
            string targetRef,
            string title,
            string description,
            CancellationToken cancellationToken)
        {
            var apiUrl =
                $"https://dev.azure.com/{Uri.EscapeDataString(details.Organization)}/{Uri.EscapeDataString(details.Project)}/_apis/git/repositories/{Uri.EscapeDataString(details.RepositoryName)}/pullrequests?api-version=7.1";

            var payload = new
            {
                sourceRefName = sourceRef,
                targetRefName = targetRef,
                title,
                description
            };

            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{_config.AzureDevOpsPat}"));

            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return EasyGitOperationResult.Fail($"Azure DevOps API PR create failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var url = doc.RootElement.TryGetProperty("url", out var urlElement)
                    ? urlElement.GetString()
                    : string.Empty;

                if (doc.RootElement.TryGetProperty("pullRequestId", out var idElement) && idElement.TryGetInt32(out var prId))
                {
                    var browseUrl =
                        $"https://dev.azure.com/{details.Organization}/{details.Project}/_git/{details.RepositoryName}/pullrequest/{prId}";
                    return EasyGitOperationResult.Success("Pull request created.", browseUrl);
                }

                return EasyGitOperationResult.Success("Pull request created.", url);
            }
            catch
            {
                return EasyGitOperationResult.Success("Pull request created.");
            }
        }

        private static string BuildCreatePrUrl(string organization, string project, string repositoryName, string sourceRef, string targetRef)
        {
            var sourceEscaped = Uri.EscapeDataString(sourceRef);
            var targetEscaped = Uri.EscapeDataString(targetRef);
            return $"https://dev.azure.com/{organization}/{project}/_git/{repositoryName}/pullrequestcreate?sourceRef={sourceEscaped}&targetRef={targetEscaped}";
        }

        private static bool TryParseRepository(RepositoryModel repository, out AzureDevOpsRepoDetails details, out string error)
        {
            details = default;
            error = string.Empty;

            var remoteUrl = (repository.GitUrl ?? string.Empty).Trim();
            if (remoteUrl.IsNullOrEmpty())
            {
                error = "Repository remote URL is empty.";
                return false;
            }

            if (!Uri.TryCreate(remoteUrl.Replace(" ", "%20"), UriKind.Absolute, out var uri))
            {
                error = "Repository remote URL is invalid for Azure DevOps.";
                return false;
            }

            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var gitIndex = Array.FindIndex(segments, segment => segment.Equals("_git", StringComparison.OrdinalIgnoreCase));
            if (gitIndex < 1 || gitIndex + 1 >= segments.Length)
            {
                error = "Repository remote URL does not match Azure DevOps format.";
                return false;
            }

            var project = Uri.UnescapeDataString(segments[gitIndex - 1]);
            var repoName = Uri.UnescapeDataString(segments[gitIndex + 1]);
            var organization = Uri.UnescapeDataString(segments[0]);

            if (project.IsNullOrEmpty() || repoName.IsNullOrEmpty() || organization.IsNullOrEmpty())
            {
                error = "Could not parse Azure DevOps organization/project/repository from Git URL.";
                return false;
            }

            details = new AzureDevOpsRepoDetails(organization, project, repoName);
            return true;
        }

        private static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageLogger.Warning($"Could not open browser for PR URL: {ex.Message}");
            }
        }

        private readonly record struct AzureDevOpsRepoDetails(string Organization, string Project, string RepositoryName);
    }
}
