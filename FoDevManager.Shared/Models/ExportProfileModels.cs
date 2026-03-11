using System.Collections.Generic;

namespace FODevManager.Models
{
    public sealed class ExportProfileModel
    {
        public int ExportFormatVersion { get; set; } = 1;

        public string ProfileName { get; set; } = "";

        public string? DatabaseName { get; set; }

        /// <summary>
        /// Optional. If set, it should be relative to the main repo root (or profile root) and resolved on import.
        /// </summary>
        public string? SolutionFileRelativePath { get; set; }

        public List<ExportRepositoryModel> Repositories { get; set; } = new();

        /// <summary>
        /// Standalone models not tied to any repository.
        /// </summary>
        public List<ExportEnvironmentModel> StandaloneModels { get; set; } = new();
    }

    public sealed class ExportRepositoryModel
    {
        public string RepoId { get; set; } = "";
        public string DisplayName { get; set; } = "";

        public string? GitUrl { get; set; }
        
        public bool AutoCheckoutOnProfileLoad { get; set; } = true;
        public bool AutoStashOnDirtyCheckout { get; set; } = false;

        public List<ExportEnvironmentModel> Models { get; set; } = new();
    }

    public sealed class ExportEnvironmentModel
    {
        public string ModelName { get; set; } = "";

        public bool IsMainFOModel { get; set; } = false;

        public ModelType ModelType { get; set; } = ModelType.Source;

        public string PackageId { get; set; } = "";

        public string PackageVersion { get; set; } = "";

        public string PackageUrl { get; set; } = "";
        
    }
}

