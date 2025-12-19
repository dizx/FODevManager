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

        public string GitUrl { get; set; } = "";

        public string Task { get; set; } = "";
        public string TaskComment { get; set; } = "";

        public bool IsDeployed { get; set; } = false;

        public bool IsMainFOModel { get; set; } = false;

        public ModelType ModelType { get; set; } = ModelType.Source;

    }
    public enum ModelType
    {
        Source,   
        Compiled  
    }

   
}
