
using FODevManager.Shared.Models;
using FODevManager.Utils;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FODevManager.Models
{
    public class ProfileModel
    {
        public string ProfileName { get; set; } = "";
        public string SolutionFilePath { get; set; } = "";

        public string ProfileFilePath { get; set; } = "";

        public List<RepositoryModel> Repositories { get; set; } = new();

        public List<ProfileEnvironmentModel> StandaloneModels { get; set; } = new();

        public string? DatabaseName { get; set; }
        
        public bool IsActive { get; set; } = false;

        [JsonIgnore]
        public List<ProfileModelEntry> AllModelEntries
        {
            get
            {
                var repositories = Repositories ?? new List<RepositoryModel>();
                var standaloneModels = StandaloneModels ?? new List<ProfileEnvironmentModel>();

                var repositoryEntries = repositories
                    .SelectMany(repository =>
                        (repository.Models ?? new List<ProfileEnvironmentModel>())
                            .Select(environment => new ProfileModelEntry(repository, environment)));

                var standaloneEntries = standaloneModels
                    .Select(environment => new ProfileModelEntry(null, environment));

                return repositoryEntries
                    .Concat(standaloneEntries)
                    .ToList();
            }
        }

        [JsonIgnore]
        public List<ProfileEnvironmentModel> AllModels => AllModelEntries.Select(entry => entry.Model).ToList();
    }

    public static class ProfileModelExtensions
    {
        public static ProfileEnvironmentModel? FindModel(this ProfileModel profile, string modelName)
        {
            if (profile == null || modelName.IsNullOrEmpty())
                return null;

            return profile.AllModels.FirstOrDefault(model => model.ModelName.SameAs(modelName));
        }

        public static ProfileModelEntry? FindModelEntry(this ProfileModel profile, string modelName)
        {
            if (profile == null || modelName.IsNullOrEmpty())
                return null;

            return profile.AllModelEntries.FirstOrDefault(model => model.ModelName.SameAs(modelName));
        }

        public static IEnumerable<RepositoryModel> GetRepositories(this ProfileModel profile)
            => profile.Repositories ?? Enumerable.Empty<RepositoryModel>();

        public static string? TryGetRepoRootFolder(this ProfileModel profile, ProfileEnvironmentModel model)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (profile.Repositories != null && profile.Repositories.Count > 0)
            {
                var repo = profile.Repositories.FirstOrDefault(r =>
                    r.Models != null &&
                    r.Models.Any(modelInRepo =>
                        ReferenceEquals(modelInRepo, model)
                        || (modelInRepo.ModelName.SameAs(model.ModelName)
                            && (modelInRepo.MetadataFolder ?? string.Empty).SameAs(model.MetadataFolder ?? string.Empty)
                            && (modelInRepo.CompiledModelFolder ?? string.Empty).SameAs(model.CompiledModelFolder ?? string.Empty))));

                if (repo != null && !repo.RepoRootFolder.IsNullOrEmpty())
                    return repo.RepoRootFolder;
            }

            return model.ModelRootFolder.IsNullOrEmpty() ? null : model.ModelRootFolder;
        }

        public static string? TryGetRepoRootFolder(this ProfileModel profile, string modelName)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (modelName.IsNullOrEmpty())
                return null;

            var repoModel = profile.Repositories?
                .SelectMany(r => r.Models ?? Enumerable.Empty<ProfileEnvironmentModel>())
                .FirstOrDefault(m => m.ModelName.SameAs(modelName));

            if (repoModel != null)
                return profile.TryGetRepoRootFolder(repoModel);

            var standalone = profile.StandaloneModels?
                .FirstOrDefault(m => m.ModelName.SameAs(modelName));

            return standalone == null ? null : profile.TryGetRepoRootFolder(standalone);
        }

        public static RepositoryModel? FindRepositoryByRoot(this ProfileModel profile, string repoRootFolder)
        {
            return (profile.Repositories ?? new List<RepositoryModel>())
                .FirstOrDefault(repo => repo.RepoRootFolder.SameAs(repoRootFolder));
        }

        public static RepositoryModel? FindRepositoryByGitUrl(this ProfileModel profile, string? gitUrl)
        {
            if (gitUrl.IsNullOrEmpty())
                return null;

            return (profile.Repositories ?? new List<RepositoryModel>())
                .FirstOrDefault(repo => repo.GitUrl.SameAs(gitUrl));
        }

        public static RepositoryModel? FindRepositoryByRepoKey(this ProfileModel profile, string repoKey)
        {
            var normalizedKey = RepositoryModel.NormalizeKey(repoKey);

            return (profile.Repositories ?? new List<RepositoryModel>())
                .FirstOrDefault(repo => repo.GetRepoKey().SameAs(normalizedKey));
        }

        public static RepositoryModel? FindRepositoryForModel(this ProfileModel profile, ProfileEnvironmentModel model)
        {
            if (profile.Repositories == null || profile.Repositories.Count == 0)
                return null;

            var metadataFolder = model.MetadataFolder ?? string.Empty;
            var compiledFolder = model.CompiledModelFolder ?? string.Empty;

            return profile.Repositories.FirstOrDefault(repo =>
                repo.Models != null &&
                repo.Models.Any(repoModel =>
                    ReferenceEquals(repoModel, model)
                    || (repoModel.ModelName.SameAs(model.ModelName)
                        && (repoModel.MetadataFolder ?? string.Empty).SameAs(metadataFolder)
                        && (repoModel.CompiledModelFolder ?? string.Empty).SameAs(compiledFolder))));
        }

    }
}
