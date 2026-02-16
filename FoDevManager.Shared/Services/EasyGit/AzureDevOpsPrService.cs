using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Utils;
using System.Diagnostics;
using System.Linq;
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

            if (!TryBuildRepositoryWebBase(repository, out var repositoryWebBase, out var webBaseError))
                return EasyGitOperationResult.Fail(webBaseError);

            if (!TryNormalizeBranchName(sourceBranch, out var sourceBranchName, out var sourceBranchError))
                return EasyGitOperationResult.Fail(sourceBranchError);

            if (!TryNormalizeBranchName(targetBranch, out var targetBranchName, out var targetBranchError))
                return EasyGitOperationResult.Fail(targetBranchError);

            var sourceRef = $"refs/heads/{sourceBranchName}";
            var targetRef = $"refs/heads/{targetBranchName}";

            var createUrl = BuildCreatePrUrl(repositoryWebBase, sourceBranchName, targetBranchName);

            if (_config.AzureDevOpsPat.IsNullOrEmpty())
            {
                OpenBrowser(createUrl);
                return EasyGitOperationResult.Success("Opened Azure DevOps PR creation page. Complete creation in browser, then refresh.", createUrl);
            }

            var apiResult = await TryCreatePrByApiAsync(details, sourceRef, targetRef, title, description, cancellationToken).ConfigureAwait(false);
            if (apiResult.Succeeded)
                return apiResult;

            MessageLogger.Warning($"API PR creation failed. Falling back to browser URL. {apiResult.Message}");
            OpenBrowser(createUrl);
            return EasyGitOperationResult.Success("API create failed. Opened Azure DevOps PR creation page in browser. Complete creation there, then refresh.", createUrl);
        }

        public async Task<EasyGitPullRequestState> GetPullRequestStateAsync(
            RepositoryModel repository,
            int pullRequestId,
            CancellationToken cancellationToken = default)
        {
            if (repository == null)
                return new EasyGitPullRequestState { CanVerify = false, IsMerged = false, Message = "Repository is not defined." };

            if (pullRequestId <= 0)
                return new EasyGitPullRequestState { CanVerify = false, IsMerged = false, Message = "Pull request ID is missing." };

            if (_config.AzureDevOpsPat.IsNullOrEmpty())
                return new EasyGitPullRequestState { CanVerify = false, IsMerged = false, Message = "Azure DevOps PAT is not configured." };

            if (!TryParseRepository(repository, out var details, out var parseError))
                return new EasyGitPullRequestState { CanVerify = false, IsMerged = false, Message = parseError };

            var apiUrl =
                $"https://dev.azure.com/{Uri.EscapeDataString(details.Organization)}/{Uri.EscapeDataString(details.Project)}/_apis/git/repositories/{Uri.EscapeDataString(details.RepositoryName)}/pullrequests/{pullRequestId}?api-version=7.1";

            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($":{_config.AzureDevOpsPat}"));

            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

            try
            {
                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return new EasyGitPullRequestState
                    {
                        CanVerify = false,
                        IsMerged = false,
                        Message = $"Azure DevOps API PR status failed: {(int)response.StatusCode} {response.ReasonPhrase}"
                    };
                }

                using var doc = JsonDocument.Parse(body);
                var status = doc.RootElement.TryGetProperty("status", out var statusElement)
                    ? (statusElement.GetString() ?? string.Empty)
                    : string.Empty;

                var isMerged = status.Equals("completed", StringComparison.OrdinalIgnoreCase);
                return new EasyGitPullRequestState
                {
                    CanVerify = true,
                    IsMerged = isMerged,
                    Message = isMerged ? "Pull request is completed." : $"Pull request status is '{status}'."
                };
            }
            catch (Exception ex)
            {
                return new EasyGitPullRequestState
                {
                    CanVerify = false,
                    IsMerged = false,
                    Message = $"Could not verify pull request status: {ex.Message}"
                };
            }
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
                    var browseUrl = BuildBrowsePrUrl(details.RepositoryWebBaseUrl, prId);
                    return EasyGitOperationResult.Success("Pull request created.", browseUrl);
                }

                return EasyGitOperationResult.Success("Pull request created.", url);
            }
            catch
            {
                return EasyGitOperationResult.Success("Pull request created.");
            }
        }

        private static string BuildCreatePrUrl(string repositoryWebBaseUrl, string sourceBranchName, string targetBranchName)
        {
            var sourceEscaped = Uri.EscapeDataString(sourceBranchName);
            var targetEscaped = Uri.EscapeDataString(targetBranchName);
            return $"{repositoryWebBaseUrl}/pullrequestcreate?sourceRef={sourceEscaped}&targetRef={targetEscaped}";
        }

        private static string BuildBrowsePrUrl(string repositoryWebBaseUrl, int pullRequestId)
        {
            return $"{repositoryWebBaseUrl}/pullrequest/{pullRequestId}";
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

            var organization = string.Empty;
            if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                organization = gitIndex >= 2
                    ? Uri.UnescapeDataString(segments[gitIndex - 2])
                    : string.Empty;
            }
            else if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
            {
                organization = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
            }
            else
            {
                organization = Uri.UnescapeDataString(segments[0]);
            }

            if (project.IsNullOrEmpty() || repoName.IsNullOrEmpty() || organization.IsNullOrEmpty())
            {
                error = "Could not parse Azure DevOps organization/project/repository from Git URL.";
                return false;
            }

            if (!TryBuildRepositoryWebBase(repository, out var repositoryWebBaseUrl, out error))
                return false;

            details = new AzureDevOpsRepoDetails(organization, project, repoName, repositoryWebBaseUrl);
            return true;
        }

        private static bool TryBuildRepositoryWebBase(RepositoryModel repository, out string repositoryWebBaseUrl, out string error)
        {
            repositoryWebBaseUrl = string.Empty;
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

            var rawSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var gitIndex = Array.FindIndex(rawSegments, segment => Uri.UnescapeDataString(segment).Equals("_git", StringComparison.OrdinalIgnoreCase));
            if (gitIndex < 1 || gitIndex + 1 >= rawSegments.Length)
            {
                error = "Repository remote URL does not match Azure DevOps format.";
                return false;
            }

            var prefix = string.Join("/", rawSegments.Take(gitIndex));
            var repositorySegment = rawSegments[gitIndex + 1];
            repositoryWebBaseUrl = $"{uri.Scheme}://{uri.Authority}/{prefix}/_git/{repositorySegment}";
            return true;
        }

        private static bool TryNormalizeBranchName(string? branch, out string normalizedBranch, out string error)
        {
            normalizedBranch = string.Empty;
            error = string.Empty;

            var safeBranch = (branch ?? string.Empty).Trim();
            if (safeBranch.IsNullOrEmpty())
            {
                error = "Branch is empty.";
                return false;
            }

            const string refsPrefix = "refs/heads/";
            if (safeBranch.StartsWith(refsPrefix, StringComparison.OrdinalIgnoreCase))
                safeBranch = safeBranch.Substring(refsPrefix.Length);

            safeBranch = safeBranch.Trim();
            if (safeBranch.IsNullOrEmpty())
            {
                error = "Branch is invalid.";
                return false;
            }

            normalizedBranch = safeBranch;
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

        private readonly record struct AzureDevOpsRepoDetails(string Organization, string Project, string RepositoryName, string RepositoryWebBaseUrl);
    }
}
