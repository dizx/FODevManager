using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Utils;
using Microsoft.Extensions.Configuration;

namespace FODevManager.Services
{
    public class ModelDeploymentService
    {
        private readonly string _deploymentBasePath;
        private readonly string _defaultSourceDirectory;
        public ModelDeploymentService(AppConfig config)
        {
            _deploymentBasePath = config.DeploymentBasePath;
            _defaultSourceDirectory = config.DefaultSourceDirectory;

            // Ensure directories exist
            FileHelper.EnsureDirectoryExists(_deploymentBasePath);
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);
        }


        public void DeployModel(string profileName, string modelName)
        {
            string targetDir = Path.Combine(_deploymentBasePath, profileName);

            FileHelper.EnsureDirectoryExists(targetDir);

            string linkPath = Path.Combine(targetDir, "Metadata");
            string sourcePath = Path.Combine(_defaultSourceDirectory, modelName, "Metadata");

            if (!Directory.Exists(sourcePath))
            {
                Console.WriteLine($"Error: Metadata folder not found at {sourcePath}.");
                return;
            }

            try
            {
                if (Directory.Exists(linkPath))
                {
                    Console.WriteLine($"Removing existing link: {linkPath}");
                    Directory.Delete(linkPath);
                }

                Directory.CreateSymbolicLink(linkPath, sourcePath);
                Console.WriteLine($"✅ Model '{modelName}' deployed successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error deploying model '{modelName}': {ex.Message}");
            }
        }

        public void DeployAllUndeployedModels(string profileName)
        {
            string profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FODevManager", $"{profileName}.json");

            if (!File.Exists(profilePath))
            {
                Console.WriteLine($"❌ Error: Profile '{profileName}' does not exist.");
                return;
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);
            bool anyUndeployed = false;

            foreach (var model in profile.Environments)
            {
                string linkPath = Path.Combine(_deploymentBasePath, profileName, "Metadata");

                if (!model.IsDeployed && !Directory.Exists(linkPath))
                {
                    Console.WriteLine($"🔄 Deploying model: {model.ModelName}...");
                    DeployModel(profileName, model.ModelName);
                    model.IsDeployed = true;
                    anyUndeployed = true;
                }
            }

            if (!anyUndeployed)
            {
                Console.WriteLine($"✅ All models in profile '{profileName}' are already deployed.");
                return;
            }

            // Save updated profile
            FileHelper.SaveJson(profilePath, profile);
            Console.WriteLine($"✅ Deployment complete. Updated profile '{profileName}'.");
        }

        public void CheckModelDeployment(string profileName, string modelName)
        {
            string linkPath = Path.Combine(_deploymentBasePath, profileName, "Metadata");

            if (Directory.Exists(linkPath))
            {
                Console.WriteLine($"✅ Model '{modelName}' is deployed at {linkPath}.");
            }
            else
            {
                Console.WriteLine($"❌ Model '{modelName}' is NOT deployed.");
            }
        }


    }
}
