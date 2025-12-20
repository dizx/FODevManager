using FODevManager.Models;
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

            var repositoryModels = (profile.Repositories ?? new List<RepositoryModel>()).ToList();

            var modelKeysInRepos = repositoryModels
                .SelectMany(repo => repo.Models ?? new List<ProfileEnvironmentModel>())
                .Select(model => CreateModelKey(model.ModelName, model.MetadataFolder))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var non = items
                .Where(viewModel => !modelKeysInRepos.Contains(CreateModelKey(viewModel.ModelName, viewModel.MetadataFolder)))
                .ToList();

            var groups = repositoryModels
                .Select(repo =>
                {
                    var repoModelKeys = (repo.Models ?? new List<ProfileEnvironmentModel>())
                        .Select(model => (model.ModelName ?? "", model.MetadataFolder ?? ""))
                        .ToHashSet();

                    var viewModels = items
                        .Where(viewModel => repoModelKeys.Contains((viewModel.ModelName ?? "", viewModel.MetadataFolder ?? "")))
                        .OrderBy(viewModel => viewModel.ModelName, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (viewModels.Count == 0)
                    {
                        var names = (repo.Models ?? new List<ProfileEnvironmentModel>())
                            .Select(model => model.ModelName ?? "")
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);

                        viewModels = items
                            .Where(viewModel => names.Contains(viewModel.ModelName ?? ""))
                            .OrderBy(viewModel => viewModel.ModelName, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }

                    var displayName = !string.IsNullOrWhiteSpace(repo.DisplayName)
                        ? repo.DisplayName
                        : ExtractRepoName(repo.GitUrl ?? repo.RepoRootFolder);

                    return new RepoGroupViewModel(
                        profileName: profile.ProfileName,
                        repository: repo,
                        displayName: displayName,
                        models: new ReadOnlyCollection<ProfileEnvironmentViewModel>(viewModels)
                    );
                })
                .Where(group => group.Models.Count > 0)
                .OrderBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            GitGroups = new ReadOnlyCollection<RepoGroupViewModel>(groups);

            NonGitModels = new ReadOnlyCollection<ProfileEnvironmentViewModel>(
                non.OrderBy(model => model.ModelName, StringComparer.OrdinalIgnoreCase).ToList()
            );
        }

        private static string CreateModelKey(string modelName, string metadataFolder)
        {
            var normalizedModelName = (modelName ?? string.Empty).Trim();
            var normalizedMetadataFolder = (metadataFolder ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(normalizedModelName))
                return string.Empty;

            return string.IsNullOrWhiteSpace(normalizedMetadataFolder)
                ? normalizedModelName
                : $"{normalizedModelName}|{normalizedMetadataFolder}";
        }

        private static string ExtractRepoName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var normalizedInput = input.Trim().Trim('"', '\'').Replace('\\', '/');

            var queryOrFragmentIndex = normalizedInput.IndexOfAny(new[] { '?', '#' });
            if (queryOrFragmentIndex >= 0)
                normalizedInput = normalizedInput.Substring(0, queryOrFragmentIndex);

            normalizedInput = normalizedInput.TrimEnd('/');

            var azureDevOpsGitSegmentIndex = normalizedInput.IndexOf("/_git/", StringComparison.OrdinalIgnoreCase);
            if (azureDevOpsGitSegmentIndex >= 0)
            {
                var startIndex = azureDevOpsGitSegmentIndex + "/_git/".Length;
                var endIndex = normalizedInput.IndexOf('/', startIndex);
                var repoName = endIndex >= 0
                    ? normalizedInput.Substring(startIndex, endIndex - startIndex)
                    : normalizedInput.Substring(startIndex);

                return TrimGitSuffix(Uri.UnescapeDataString(repoName));
            }

            var hasScheme = normalizedInput.Contains("://", StringComparison.Ordinal);
            if (!hasScheme && normalizedInput.Contains(':'))
            {
                var afterColon = normalizedInput.Substring(normalizedInput.IndexOf(':') + 1).Trim('/');
                var lastSlash = afterColon.LastIndexOf('/');
                var repoName = lastSlash >= 0 ? afterColon.Substring(lastSlash + 1) : afterColon;
                return TrimGitSuffix(Uri.UnescapeDataString(repoName));
            }

            if (hasScheme)
            {
                var lastSlash = normalizedInput.LastIndexOf('/');
                if (lastSlash >= 0 && lastSlash + 1 < normalizedInput.Length)
                {
                    var tail = normalizedInput.Substring(lastSlash + 1);
                    if (string.IsNullOrWhiteSpace(tail))
                    {
                        var previousSlash = normalizedInput.LastIndexOf('/', Math.Max(0, lastSlash - 1));
                        if (previousSlash >= 0 && previousSlash + 1 < lastSlash)
                            tail = normalizedInput.Substring(previousSlash + 1, lastSlash - previousSlash - 1);
                    }
                    return TrimGitSuffix(Uri.UnescapeDataString(tail));
                }
            }

            var lastSegmentIndex = normalizedInput.LastIndexOf('/');
            var candidate = lastSegmentIndex >= 0 ? normalizedInput.Substring(lastSegmentIndex + 1) : normalizedInput;
            return TrimGitSuffix(Uri.UnescapeDataString(candidate));
        }

        private static string TrimGitSuffix(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var trimmedName = name.Trim();
            if (trimmedName.EndsWith(".git"))
                trimmedName = trimmedName.Substring(0, trimmedName.Length - 4);

            return trimmedName;
        }
    }
}
