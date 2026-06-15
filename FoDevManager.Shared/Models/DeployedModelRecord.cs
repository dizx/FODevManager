using FODevManager.Models;

namespace FODevManager.Shared.Models
{
    public sealed class DeployedModelRecord
    {
        public string ModelName { get; set; } = string.Empty;

        public string ProfileName { get; set; } = string.Empty;

        public ModelType ModelType { get; set; } = ModelType.Source;

        public string SourcePath { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public string PackageId { get; set; } = string.Empty;

        public string PackageVersion { get; set; } = string.Empty;

        public DateTime DeployedAtUtc { get; set; } = DateTime.UtcNow;

        public bool IsUnmanaged { get; set; } = false;
    }
}
