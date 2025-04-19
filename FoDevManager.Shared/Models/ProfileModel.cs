using System.Collections.Generic;

namespace FODevManager.Models
{
    public class ProfileModel
    {
        public string ProfileName { get; set; } = "";
        public string SolutionFilePath { get; set; } = "";
        public List<ProfileEnvironmentModel> Environments { get; set; } = new();

        public string? DatabaseName { get; set; }
        
        public bool IsActive { get; set; } = false;

    }

}
