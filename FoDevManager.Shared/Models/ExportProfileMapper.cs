using FODevManager.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FODevManager.Models
{
    public static class ExportProfileMapper
    {
        public static ExportProfileModel ToExport(ProfileModel profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            return new ExportProfileModel
            {
                ProfileName = profile.ProfileName,
                DatabaseName = profile.DatabaseName,
                SolutionFileRelativePath = ToSolutionRelativePath(profile),
                Repositories = (profile.Repositories ?? new List<RepositoryModel>())
                    .Select(ToExportRepository)
                    .ToList(),
                StandaloneModels = (profile.Models ?? new List<ProfileEnvironmentModel>())
                    .Select(ToExportEnvironment)
                    .ToList()
            };
        }

        public static ProfileModel FromExport(ExportProfileModel exportProfile, string? profileFilePath = null)
        {
            if (exportProfile == null) throw new ArgumentNullException(nameof(exportProfile));

            return new ProfileModel
            {
                ProfileName = exportProfile.ProfileName ?? string.Empty,
                DatabaseName = exportProfile.DatabaseName,
                ProfileFilePath = profileFilePath ?? string.Empty,
                IsActive = false,
                SolutionFilePath = exportProfile.SolutionFileRelativePath ?? string.Empty,
                Repositories = (exportProfile.Repositories ?? new List<ExportRepositoryModel>())
                    .Select(ToRepositoryModel)
                    .ToList(),

                Models = (exportProfile.StandaloneModels ?? new List<ExportEnvironmentModel>())
                    .Select(ToStandaloneProfileEnvironmentModel)
                    .ToList()
            };
        }

        private static ExportRepositoryModel ToExportRepository(RepositoryModel repository)
        {
            return new ExportRepositoryModel
            {
                RepoId = repository.RepoId ?? string.Empty,
                DisplayName = repository.DisplayName ?? string.Empty,
                GitUrl = repository.GitUrl,
                AutoCheckoutOnProfileLoad = repository.AutoCheckoutOnProfileLoad,
                AutoStashOnDirtyCheckout = repository.AutoStashOnDirtyCheckout,

                Models = (repository.Models ?? new List<ProfileEnvironmentModel>())
                    .Select(ToExportEnvironment)
                    .ToList()
            };
        }

        private static ExportEnvironmentModel ToExportEnvironment(ProfileEnvironmentModel environment)
        {
            return new ExportEnvironmentModel
            {
                ModelName = environment.ModelName,
                IsMainFOModel = environment.IsMainFOModel,
                ModelType = environment.ModelType,                
            };
        }

        private static RepositoryModel ToRepositoryModel(ExportRepositoryModel exportRepository)
        {
            var repository = new RepositoryModel
            {
                RepoId = exportRepository.RepoId ?? string.Empty,
                DisplayName = exportRepository.DisplayName ?? string.Empty,
                GitUrl = exportRepository.GitUrl,
                AutoCheckoutOnProfileLoad = exportRepository.AutoCheckoutOnProfileLoad,
                AutoStashOnDirtyCheckout = exportRepository.AutoStashOnDirtyCheckout
            };

            repository.Models = (exportRepository.Models ?? new List<ExportEnvironmentModel>())
                .Select(exportEnvironment => ToProfileEnvironmentModel(exportEnvironment, exportRepository))
                .ToList();

            return repository;
        }

        private static ProfileEnvironmentModel ToProfileEnvironmentModel(
            ExportEnvironmentModel exportEnvironment,
            ExportRepositoryModel exportRepository)
        {

            return new ProfileEnvironmentModel
            {
                ModelName = exportEnvironment.ModelName ?? string.Empty,
                IsMainFOModel = exportEnvironment.IsMainFOModel,
                ModelType = exportEnvironment.ModelType                
            };
        }

        private static ProfileEnvironmentModel ToStandaloneProfileEnvironmentModel(ExportEnvironmentModel exportEnvironment)
        {
            return new ProfileEnvironmentModel
            {
                ModelName = exportEnvironment.ModelName ?? string.Empty,
                IsMainFOModel = exportEnvironment.IsMainFOModel,
                ModelType = exportEnvironment.ModelType,
                
            };
        }


        private static string? ToSolutionRelativePath(ProfileModel profile)
        {
            var absoluteSolutionPath = profile.SolutionFilePath;
            if (absoluteSolutionPath.IsNullOrEmpty())
                return $"{profile.ProfileName}.sln";

            string baseFolder = GetSolutionBaseFolder(profile);

            try
            {
                var fullSolutionPath = Path.GetFullPath(absoluteSolutionPath);
                var relative = Path.GetRelativePath(baseFolder, fullSolutionPath);

                // Keep it git-friendly and consistent
                return relative.Replace('\\', '/');
            }
            catch
            {
                // Worst-case: at least preserve solution name
                return Path.GetFileName(absoluteSolutionPath);
            }
        }

        private static string GetSolutionBaseFolder(ProfileModel profile)
        {
            // Prefer main FO repo root folder if available
            var mainFoModel = profile.GetAllModels()
                .FirstOrDefault(model => model.IsMainFOModel);

            if (mainFoModel != null)
            {
                var repoRoot = profile.TryGetRepoRootFolder(mainFoModel);
                if (!repoRoot.IsNullOrEmpty())
                    return Path.GetFullPath(repoRoot);
            }

            // Fall back: profile folder under default source dir is not available here,
            // so we just use solution's directory if we can.
            if (!profile.SolutionFilePath.IsNullOrEmpty())
            {
                var solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(profile.SolutionFilePath));
                if (!solutionDirectory.IsNullOrEmpty())
                    return solutionDirectory!;
            }

            return AppContext.BaseDirectory;
        }

    }
}
