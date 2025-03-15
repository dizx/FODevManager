using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using FODevManager.Models;

namespace FODevManager.Services
{
    public class ModelDeploymentService
    {
        private readonly string _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FODevManager");

        public void DeployModel(string profileName, string modelName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");
            if (!File.Exists(profilePath))
            {
                Console.WriteLine("Profile does not exist.");
                return;
            }

            var profile = JsonSerializer.Deserialize<ProfileModel>(File.ReadAllText(profilePath));
            var model = profile.Environments.Find(m => m.ModelName == modelName);
            if (model == null)
            {
                Console.WriteLine("Model not found in profile.");
                return;
            }

            string targetDir = Path.Combine(@"C:\AOSService\PackagesLocalDirectory", profileName);
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            string linkPath = Path.Combine(targetDir, "Metadata");
            string sourcePath = Path.Combine(Path.GetDirectoryName(model.ProjectFilePath), "Metadata");

            if (Directory.Exists(linkPath))
                Directory.Delete(linkPath);

            Directory.CreateSymbolicLink(linkPath, sourcePath);
            model.IsDeployed = true;

            File.WriteAllText(profilePath, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Model '{modelName}' deployed successfully.");
        }

        public void CheckModelDeployment(string profileName, string modelName)
        {
            string linkPath = Path.Combine(@"C:\AOSService\PackagesLocalDirectory", profileName, "Metadata");
            Console.WriteLine(Directory.Exists(linkPath) ? $"Model '{modelName}' is deployed." : $"Model '{modelName}' is NOT deployed.");
        }
    }
}
