
using System.Collections.Generic;

namespace FODevManager.Models
{
    public class ProfileModel
    {
        public string ProfileName { get; set; } = "";
        public string SolutionFilePath { get; set; } = "";

        public string ProfileFilePath { get; set; } = "";

        public List<RepositoryModel> Repositories { get; set; } = new();


        public List<ProfileEnvironmentModel> Models { get; set; } = new();

        public string? DatabaseName { get; set; }
        
        public bool IsActive { get; set; } = false;
        
    }

    public static class ProfileModelExtensions
    {
        public static IEnumerable<ProfileEnvironmentModel> GetAllModels(this ProfileModel profile)
        {
            if (profile == null)
                return Enumerable.Empty<ProfileEnvironmentModel>();

            var repoModels = profile.Repositories?
                .SelectMany(r => r.Models ?? new List<ProfileEnvironmentModel>()) ?? Enumerable.Empty<ProfileEnvironmentModel>();

            var standalone = profile.Models ?? Enumerable.Empty<ProfileEnvironmentModel>();

            return repoModels.Concat(standalone);
        }

        public static IEnumerable<RepositoryModel> GetRepositories(this ProfileModel profile)
            => profile.Repositories ?? Enumerable.Empty<RepositoryModel>();

        /// <summary>
        /// Returns the repo root folder (where .git lives) for the given model, if the model belongs to a repository.
        /// Falls back to model.ModelRootFolder for standalone/backwards compatible models.
        /// </summary>
        public static string? TryGetRepoRootFolder(this ProfileModel profile, ProfileEnvironmentModel model)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (model == null)
                throw new ArgumentNullException(nameof(model));

            // Repository-backed model lookup
            if (profile.Repositories != null && profile.Repositories.Count > 0)
            {
                var repo = profile.Repositories.FirstOrDefault(r =>
                    r.Models != null &&
                    r.Models.Any(m =>
                        string.Equals(m.ModelName, model.ModelName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(m.MetadataFolder, model.MetadataFolder, StringComparison.OrdinalIgnoreCase)));

                if (repo != null && !string.IsNullOrWhiteSpace(repo.RepoRootFolder))
                    return repo.RepoRootFolder;
            }

            // Standalone/backwards compat
            return string.IsNullOrWhiteSpace(model.ModelRootFolder) ? null : model.ModelRootFolder;
        }

        /// <summary>
        /// Convenience overload: resolve repo root by model name (best-effort).
        /// </summary>
        public static string? TryGetRepoRootFolder(this ProfileModel profile, string modelName)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (string.IsNullOrWhiteSpace(modelName))
                return null;

            // Prefer repository-backed model
            var repoModel = profile.Repositories?
                .SelectMany(r => r.Models ?? Enumerable.Empty<ProfileEnvironmentModel>())
                .FirstOrDefault(m => string.Equals(m.ModelName, modelName, StringComparison.OrdinalIgnoreCase));

            if (repoModel != null)
                return profile.TryGetRepoRootFolder(repoModel);

            // Standalone
            var standalone = profile.Models?
                .FirstOrDefault(m => string.Equals(m.ModelName, modelName, StringComparison.OrdinalIgnoreCase));

            return standalone == null ? null : profile.TryGetRepoRootFolder(standalone);
        }

        public static RepositoryModel? FindRepositoryByRoot(this ProfileModel profile, string repoRootFolder)
        {
            return (profile.Repositories ?? new List<RepositoryModel>())
                .FirstOrDefault(repo => string.Equals(repo.RepoRootFolder, repoRootFolder, StringComparison.OrdinalIgnoreCase));
        }

        public static RepositoryModel? FindRepositoryByGitUrl(this ProfileModel profile, string gitUrl)
        {
            if (string.IsNullOrWhiteSpace(gitUrl))
                return null;

            return (profile.Repositories ?? new List<RepositoryModel>())
                .FirstOrDefault(repo => string.Equals(repo.GitUrl, gitUrl, StringComparison.OrdinalIgnoreCase));
        }

        public static RepositoryModel? FindRepositoryByRepoKey(this ProfileModel profile, string repoKey)
        {
            var normalizedKey = RepositoryModel.NormalizeKey(repoKey);

            return (profile.Repositories ?? new List<RepositoryModel>())
                .FirstOrDefault(repo => string.Equals(repo.GetRepoKey(), normalizedKey, StringComparison.OrdinalIgnoreCase));
        }

        public static RepositoryModel? FindRepositoryForModel(this ProfileModel profile, ProfileEnvironmentModel model)
        {
            if (profile.Repositories == null || profile.Repositories.Count == 0)
                return null;

            var metadataFolder = model.MetadataFolder ?? string.Empty;

            return profile.Repositories.FirstOrDefault(repo =>
                repo.Models != null &&
                repo.Models.Any(repoModel =>
                    string.Equals(repoModel.ModelName, model.ModelName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(repoModel.MetadataFolder ?? string.Empty, metadataFolder, StringComparison.OrdinalIgnoreCase)));
        }

    }
}

   

