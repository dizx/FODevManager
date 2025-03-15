namespace FODevManager.Models
{
    public class ProfileEnvironmentModel
    {
        public string ModelName { get; set; } = "";
        public string ProjectFilePath { get; set; } = "";
        public bool IsDeployed { get; set; } = false;
    }
}
