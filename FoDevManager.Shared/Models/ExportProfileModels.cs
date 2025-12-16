using System.Collections.Generic;

namespace FODevManager.Models
{
    /// <summary>
    /// Portable profile contract for Git syncing / sharing.
    /// Contains only data required to reconstruct a profile on another machine.
    /// </summary>
    public sealed class ExportProfileModel
    {
        public int ExportFormatVersion { get; set; } = 1;

        public string ProfileName { get; set; } = "";

        public string? DatabaseName { get; set; }

        public List<ExportRepositoryModel> Repositories { get; set; } = new();

        public List<ExportProfileEnvironmentModel> Models { get; set; } = new();

        /// <summary>
        /// Optional. If present it must be relative (portable). Prefer leaving empty and letting import decide.
        /// </summary>
        public string? SolutionFileRelativePath { get; set; }
    }

    public sealed class ExportRepositoryModel
    {
        public string RepoId { get; set; } = "";
        public string DisplayName { get; set; } = "";

        public string? GitUrl { get; set; }
        public string? PreferredBranch { get; set; }

        public bool AutoCheckoutOnProfileLoad { get; set; } = true;
        public bool AutoStashOnDirtyCheckout { get; set; } = false;

        public List<ExportProfileEnvironmentModel> Models { get; set; } = new();
    }

    public sealed class ExportProfileEnvironmentModel
    {
        public string ModelName { get; set; } = "";
        public ModelType ModelType { get; set; } = ModelType.Source;
        public bool IsMainFOModel { get; set; } = false;

        /// <summary>
        /// Optional; if set, must be a repo URL and should not be a local path.
        /// Typically you keep GitUrl on the repository instead.
        /// </summary>
        public string? GitUrl { get; set; }
    }
}
