using System;
using System.Collections.Generic;
using System.Linq;

namespace FODevManager.Models
{
    public static class ExportProfileMapper
    {
        public static ExportProfileModel ToExport(ProfileModel profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var export = new ExportProfileModel
            {
                ProfileName = profile.ProfileName ?? string.Empty,
                DatabaseName = profile.DatabaseName,
                SolutionFileRelativePath = null
            };

            if (profile.Repositories != null)
            {
                export.Repositories = profile.Repositories
                    .Select(ToExport)
                    .ToList();
            }

            if (profile.Models != null)
            {
                export.Models = profile.Models
                    .Select(environment => ToExport(environment, explicitGitUrl: environment.GitUrl))
                    .ToList();
            }

            return export;
        }

        public static ProfileModel ToProfile(ExportProfileModel exportProfile)
        {
            if (exportProfile == null)
                throw new ArgumentNullException(nameof(exportProfile));

            var profile = new ProfileModel
            {
                ProfileName = exportProfile.ProfileName ?? string.Empty,
                DatabaseName = exportProfile.DatabaseName,
                SolutionFilePath = string.Empty,
                ProfileFilePath = string.Empty,
                IsActive = false,
                Repositories = new List<RepositoryModel>(),
                Models = new List<ProfileEnvironmentModel>()
            };

            if (exportProfile.Repositories != null)
            {
                foreach (var exportRepository in exportProfile.Repositories)
                {
                    var repoModel = ToRepository(exportRepository);
                    profile.Repositories.Add(repoModel);
                }
            }

            if (exportProfile.Models != null)
            {
                foreach (var exportEnvironment in exportProfile.Models)
                {
                    profile.Models.Add(ToEnvironment(exportEnvironment, fallbackGitUrl: exportEnvironment.GitUrl));
                }
            }

            return profile;
        }

        private static ExportRepositoryModel ToExport(RepositoryModel repository)
        {
            var exportRepository = new ExportRepositoryModel
            {
                RepoId = repository.RepoId ?? string.Empty,
                DisplayName = repository.DisplayName ?? string.Empty,
                GitUrl = repository.GitUrl,
                PreferredBranch = repository.PreferredBranch,
                AutoCheckoutOnProfileLoad = repository.AutoCheckoutOnProfileLoad,
                AutoStashOnDirtyCheckout = repository.AutoStashOnDirtyCheckout,
                Models = new List<ExportProfileEnvironmentModel>()
            };

            if (repository.Models != null)
            {
                exportRepository.Models = repository.Models
                    .Select(environment => ToExport(environment, explicitGitUrl: repository.GitUrl))
                    .ToList();
            }

            return exportRepository;
        }

        private static ExportProfileEnvironmentModel ToExport(ProfileEnvironmentModel environment, string? explicitGitUrl)
        {
            return new ExportProfileEnvironmentModel
            {
                ModelName = environment.ModelName ?? string.Empty,
                ModelType = environment.ModelType,
                IsMainFOModel = environment.IsMainFOModel,
                GitUrl = string.IsNullOrWhiteSpace(explicitGitUrl) ? null : explicitGitUrl
            };
        }

        private static RepositoryModel ToRepository(ExportRepositoryModel exportRepository)
        {
            var repository = new RepositoryModel
            {
                RepoId = exportRepository.RepoId ?? string.Empty,
                DisplayName = exportRepository.DisplayName ?? string.Empty,
                RepoRootFolder = string.Empty,
                GitUrl = exportRepository.GitUrl,
                PreferredBranch = exportRepository.PreferredBranch,
                AutoCheckoutOnProfileLoad = exportRepository.AutoCheckoutOnProfileLoad,
                AutoStashOnDirtyCheckout = exportRepository.AutoStashOnDirtyCheckout,
                LastKnownBranch = null,
                LastKnownCommit = null,
                PeriTask = string.Empty,
                PeriTaskComment = string.Empty,
                Models = new List<ProfileEnvironmentModel>()
            };

            if (exportRepository.Models != null)
            {
                foreach (var exportEnvironment in exportRepository.Models)
                {
                    repository.Models.Add(ToEnvironment(exportEnvironment, fallbackGitUrl: exportRepository.GitUrl));
                }
            }

            return repository;
        }

        private static ProfileEnvironmentModel ToEnvironment(ExportProfileEnvironmentModel exportEnvironment, string? fallbackGitUrl)
        {
            return new ProfileEnvironmentModel
            {
                ModelName = exportEnvironment.ModelName ?? string.Empty,
                ModelType = exportEnvironment.ModelType,
                IsMainFOModel = exportEnvironment.IsMainFOModel,
                GitUrl = string.IsNullOrWhiteSpace(exportEnvironment.GitUrl) ? (fallbackGitUrl ?? string.Empty) : exportEnvironment.GitUrl,

                ModelRootFolder = string.Empty,
                ProjectFilePath = string.Empty,
                MetadataFolder = string.Empty,
                CompiledModelFolder = string.Empty,

                PeriTask = string.Empty,
                PeriTaskComment = string.Empty,
                IsDeployed = false
            };
        }
    }
}
