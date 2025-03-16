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
