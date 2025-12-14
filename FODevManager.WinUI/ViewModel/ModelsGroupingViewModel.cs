using FODevManager.Models;
using FODevManager.Models.FODevManager.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FODevManager.WinUI.ViewModel
{
    public sealed class ModelsGroupingViewModel
    {
        public ReadOnlyCollection<RepoGroupViewModel> GitGroups { get; }
        public ReadOnlyCollection<ProfileEnvironmentViewModel> NonGitModels { get; }

        public ModelsGroupingViewModel(ProfileModel profile, IEnumerable<ProfileEnvironmentViewModel> allItems)
        {
            var items = allItems?.ToList() ?? new List<ProfileEnvironmentViewModel>();

            // Standalone/non-repo models = still come from list (or profile.Environments)
            var non = items.Where(v => !v.HasGit).ToList();

            // Repo groups should come from profile.Repositories (source of truth)
            var groups = (profile.Repositories ?? new List<RepositoryModel>())
                .Select(repo =>
                {
                    // Match viewmodels to repo models. Prefer (ModelName + MetadataFolder) to avoid collisions.
                    var repoModelKeys = (repo.Models ?? new List<ProfileEnvironmentModel>())
                        .Select(m => (m.ModelName ?? "", m.MetadataFolder ?? ""))
                        .ToHashSet();

                    var vms = items
                        .Where(v => v.HasGit)
                        .Where(v => repoModelKeys.Contains((v.ModelName ?? "", v.MetadataFolder ?? "")))
                        .OrderBy(v => v.ModelName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // Fallback: if UI items weren’t built with MetadataFolder, match by name only
                    if (vms.Count == 0)
                    {
                        var names = (repo.Models ?? new List<ProfileEnvironmentModel>())
                            .Select(m => m.ModelName ?? "")
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        vms = items
                            .Where(v => v.HasGit)
                            .Where(v => names.Contains(v.ModelName ?? ""))
                            .OrderBy(v => v.ModelName, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }

                    var display = !string.IsNullOrWhiteSpace(repo.DisplayName)
                        ? repo.DisplayName
                        : ExtractRepoName(repo.GitUrl ?? repo.RepoRootFolder);

                    var branch = repo.LastKnownBranch; // repo-level truth

                    return new RepoGroupViewModel(
                        gitUrl: repo.GitUrl ?? string.Empty,
                        displayName: display,
                        branch: branch,
                        models: new ReadOnlyCollection<ProfileEnvironmentViewModel>(vms)
                    );
                })
                // Hide empty repos if you prefer
                .Where(g => g.Models.Count > 0)
                .OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            GitGroups = new ReadOnlyCollection<RepoGroupViewModel>(groups);
            NonGitModels = new ReadOnlyCollection<ProfileEnvironmentViewModel>(
                non.OrderBy(x => x.ModelName, StringComparer.OrdinalIgnoreCase).ToList()
            );
        }

        private static string ExtractRepoName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // Normalize
            var s = input.Trim().Trim('"', '\'').Replace('\\', '/');

            // Strip query/fragment early
            var q = s.IndexOfAny(new[] { '?', '#' });
            if (q >= 0) s = s.Substring(0, q);

            // Trim trailing slashes
            s = s.TrimEnd('/');

            // Azure DevOps: .../_git/Repo Name (optionally ends with .git)
            var gitSegIdx = s.IndexOf("/_git/", StringComparison.OrdinalIgnoreCase);
            if (gitSegIdx >= 0)
            {
                var start = gitSegIdx + "/_git/".Length;
                var end = s.IndexOf('/', start);
                var name = end >= 0 ? s.Substring(start, end - start) : s.Substring(start);
                return TrimGitSuffix(Uri.UnescapeDataString(name));
            }

            // SCP-like: git@host:org/repo name(.git)
            var hasScheme = s.Contains("://", StringComparison.Ordinal);
            if (!hasScheme && s.Contains(':'))
            {
                var afterColon = s.Substring(s.IndexOf(':') + 1).Trim('/');
                var lastSlash = afterColon.LastIndexOf('/');
                var name = lastSlash >= 0 ? afterColon.Substring(lastSlash + 1) : afterColon;
                return TrimGitSuffix(Uri.UnescapeDataString(name));
            }

            // Regular URL: take last non-empty path segment
            if (hasScheme)
            {
                // Don’t rely on Uri.TryCreate with spaces—just split path manually
                var lastSlash = s.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash + 1 < s.Length)
                {
                    var tail = s.Substring(lastSlash + 1);
                    if (string.IsNullOrWhiteSpace(tail))
                    {
                        // If trailing slash, back up one segment
                        var prev = s.LastIndexOf('/', Math.Max(0, lastSlash - 1));
                        if (prev >= 0 && prev + 1 < lastSlash)
                            tail = s.Substring(prev + 1, lastSlash - prev - 1);
                    }
                    return TrimGitSuffix(Uri.UnescapeDataString(tail));
                }
            }

            // Local/UNC path or bare name: take last segment after '/'
            var idx = s.LastIndexOf('/');
            var candidate = idx >= 0 ? s.Substring(idx + 1) : s;
            return TrimGitSuffix(Uri.UnescapeDataString(candidate));
        }

        private static string TrimGitSuffix(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            name = name.Trim();
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name;
        }

    }
}
