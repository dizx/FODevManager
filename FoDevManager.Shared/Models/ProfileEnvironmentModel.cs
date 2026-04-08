using FODevManager.Shared.Models;

namespace FODevManager.Models
{
    public class ProfileEnvironmentModel
    {
        public string ModelName { get; set; } = "";

        public string ModelRootFolder { get; set; } = "";

        public string ProjectFilePath { get; set; } = "";

        public string MetadataFolder { get; set; } = "";

        public string CompiledModelFolder { get; set; } = "";

        public string PackageId { get; set; } = "";

        public string PackageVersion { get; set; } = "";

        public bool IsDeployed { get; set; } = false;

        public bool IsMainFOModel { get; set; } = false;

        public ModelType ModelType { get; set; } = ModelType.Source;

    }
    public enum ModelType
    {
        Source = 0,
        Compiled = 1,
        CompiledNuget = 2
    }


}
