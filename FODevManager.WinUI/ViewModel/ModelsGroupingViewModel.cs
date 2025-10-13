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

        public ModelsGroupingViewModel(IEnumerable<ProfileEnvironmentViewModel> items)
        {
            var list = items?.ToList() ?? new List<ProfileEnvironmentViewModel>();

            var git = list.Where(v => v.HasGit).ToList();
            var non = list.Where(v => !v.HasGit).ToList();

            var groups = git.GroupBy(v => v.GitUrl)
                            .Select(g =>
                            {
                                var vms = g.OrderBy(x => x.ModelName, StringComparer.OrdinalIgnoreCase).ToList();
                                var display = ExtractRepoName(g.Key);
                                var branch = vms.FirstOrDefault()?.GitBranch; // same repo => same branch
                                return new RepoGroupViewModel(
                                    gitUrl: g.Key,
                                    displayName: display,
                                    branch: branch,
                                    models: new ReadOnlyCollection<ProfileEnvironmentViewModel>(vms)
                                );
                            })
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
