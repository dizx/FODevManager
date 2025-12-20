using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Security.Cryptography;


namespace FODevManager.Models
{
    public partial class RepositoryModel
    {
        public string RepoId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string RepoRootFolder { get; set; } = "";     // where .git lives (your current ModelRootFolder)
        public string? GitUrl { get; set; }                  // origin URL
        public string? PreferredBranch { get; set; }          // profile wants this branch
        public string? LastKnownBranch { get; set; }          // saved/observed branch
        public string? LastKnownCommit { get; set; }          // optional

        public string MainBranchName { get; set; } = "main";

        public bool AutoCheckoutOnProfileLoad { get; set; } = true;
        public bool AutoStashOnDirtyCheckout { get; set; } = false;

        public bool AutoApplyStashAfterCheckout { get; set; } = false;

        // Models that belong to this repo
        public List<ProfileEnvironmentModel> Models { get; set; } = new();

        // Optional: repo-level Task (you already have repo-level UI actions)
        public string Task { get; set; } = "";
        public string TaskComment { get; set; } = "";
    }

    public partial class RepositoryModel
    {
        public string GetRepoKey()
        {
            var gitUrl = (GitUrl ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(gitUrl))
                return NormalizeKey(gitUrl);

            var repoRootFolder = (RepoRootFolder ?? string.Empty).Trim();
            return NormalizeKey(repoRootFolder);
        }

        public void EnsureRepoId()
        {
            if (!string.IsNullOrWhiteSpace(RepoId))
                return;

            var repoKey = GetRepoKey();
            if (string.IsNullOrWhiteSpace(repoKey))
                return;

            RepoId = CreateRepoIdFromRepoKey(repoKey);
        }

        public static string CreateRepoIdFromRepoKey(string repoKey)
        {
            var normalizedRepoKey = NormalizeKey(repoKey);

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedRepoKey));
            var hex = Convert.ToHexString(hashBytes);

            return $"repo-{hex.Substring(0, 12).ToLowerInvariant()}";
        }

        public static string NormalizeKey(string value)
        {
            var normalized = (value ?? string.Empty).Trim();

            normalized = normalized.Replace('\\', '/').TrimEnd('/');

            if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 4);

            return normalized.ToLowerInvariant();
        }

        public void EnsureDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
                return;

            var fromGitUrl = TryExtractRepoNameFromGitUrl(GitUrl);
            if (!string.IsNullOrWhiteSpace(fromGitUrl))
            {
                DisplayName = fromGitUrl;
                return;
            }

            var fromFolder = TryExtractRepoNameFromFolder(RepoRootFolder);
            if (!string.IsNullOrWhiteSpace(fromFolder))
            {
                DisplayName = fromFolder;
                return;
            }
        }
        public static string? TryExtractRepoNameFromGitUrl(string? gitUrl)
        {
            if (string.IsNullOrWhiteSpace(gitUrl))
                return null;

            var normalized = gitUrl.Trim().Trim('"', '\'').Replace('\\', '/').TrimEnd('/');

            var queryIndex = normalized.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0)
                normalized = normalized.Substring(0, queryIndex);

            // Azure DevOps: https://dev.azure.com/org/proj/_git/repo
            var azureSegmentIndex = normalized.IndexOf("/_git/", StringComparison.OrdinalIgnoreCase);
            if (azureSegmentIndex >= 0)
            {
                var startIndex = azureSegmentIndex + "/_git/".Length;
                var endIndex = normalized.IndexOf('/', startIndex);
                var repoName = endIndex >= 0
                    ? normalized.Substring(startIndex, endIndex - startIndex)
                    : normalized.Substring(startIndex);

                return TrimGitSuffix(Uri.UnescapeDataString(repoName));
            }

            // SSH: git@host:org/repo.git
            var sshColonIndex = normalized.IndexOf(':');
            if (!normalized.Contains("://", StringComparison.Ordinal) && sshColonIndex >= 0)
            {
                var afterColon = normalized.Substring(sshColonIndex + 1).Trim('/');
                var lastSlash = afterColon.LastIndexOf('/');
                var repoName = lastSlash >= 0 ? afterColon.Substring(lastSlash + 1) : afterColon;
                return TrimGitSuffix(Uri.UnescapeDataString(repoName));
            }

            // Standard: https://host/org/repo(.git)
            var lastSlashIndex = normalized.LastIndexOf('/');
            if (lastSlashIndex >= 0 && lastSlashIndex + 1 < normalized.Length)
            {
                var repoName = normalized.Substring(lastSlashIndex + 1);
                return TrimGitSuffix(Uri.UnescapeDataString(repoName));
            }

            return TrimGitSuffix(Uri.UnescapeDataString(normalized));
        }

        public static string? TryExtractRepoNameFromFolder(string? repoRootFolder)
        {
            if (string.IsNullOrWhiteSpace(repoRootFolder))
                return null;

            var trimmed = repoRootFolder.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        private static string TrimGitSuffix(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var trimmed = name.Trim();
            if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - 4);

            return trimmed;
        }
    
        
    }

}
