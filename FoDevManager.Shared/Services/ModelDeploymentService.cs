using System;
using System.Diagnostics;
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
        private readonly string _appDataPath;
        private readonly string _defaultSourceDirectory;
        private string ProfilePath(string profileName) => Path.Combine(_appDataPath, $"{profileName}.json");

        public ModelDeploymentService(AppConfig config)
        {
            _appDataPath = config.ProfileStoragePath;
            _deploymentBasePath = config.DeploymentBasePath;
            _defaultSourceDirectory = config.DefaultSourceDirectory;

            // Ensure directories exist
            FileHelper.EnsureDirectoryExists(_deploymentBasePath);
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);
        }


        public void DeployModel(string profileName, string modelName)
        {
            Console.WriteLine("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            StopW3SVC();

            try
            {
                DeploySingleModel(profileName, modelName);
                UpdateProfileFile(profileName, new ProfileEnvironmentModel { ModelName = modelName, IsDeployed = true });
            }
            finally
            {
                Console.WriteLine("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                StartW3SVC();
            }
        }

        public void DeployAllUndeployedModels(string profileName)
        {
            Console.WriteLine("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            StopW3SVC();

            try
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
                        DeploySingleModel(profileName, model.ModelName);
                        model.IsDeployed = true;
                        anyUndeployed = true;
                    }
                }

                if (!anyUndeployed)
                {
                    Console.WriteLine($"✅ All models in profile '{profileName}' are already deployed.");
                    return;
                }

                FileHelper.SaveJson(profilePath, profile);
                Console.WriteLine($"✅ Deployment complete. Updated profile '{profileName}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error deploying models: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                StartW3SVC();
            }
        }

        private void DeploySingleModel(string profileName, string modelName)
        {
            try
            {
                string targetDir = Path.Combine(_deploymentBasePath, profileName);
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                string linkPath = Path.Combine(targetDir, "Metadata");
                string sourcePath = Path.Combine(_defaultSourceDirectory, modelName, "Metadata");

                if (!Directory.Exists(sourcePath))
                {
                    Console.WriteLine($"❌ Error: Metadata folder not found at {sourcePath}.");
                    return;
                }

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


        private void UpdateProfileFile(string profileName, ProfileEnvironmentModel updatedEnvironment)
        {
            string profilePath = ProfilePath(profileName);

            if (!File.Exists(profilePath))
            {
                Console.WriteLine($"❌ Error: Profile '{profileName}' does not exist.");
                return;
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);

            // Find the model in the profile
            var existingEnvironment = profile.Environments.FirstOrDefault(e => e.ModelName == updatedEnvironment.ModelName);

            if (existingEnvironment == null)
            {
                Console.WriteLine($"❌ Error: Model '{updatedEnvironment.ModelName}' not found in profile '{profileName}'.");
                return;
            }

            // Update the existing model entry
            if(!string.IsNullOrEmpty(updatedEnvironment.ProjectFilePath))
                existingEnvironment.ProjectFilePath = updatedEnvironment.ProjectFilePath;
            existingEnvironment.IsDeployed = updatedEnvironment.IsDeployed;

            // Save the updated profile
            FileHelper.SaveJson(profilePath, profile);
            Console.WriteLine($"✅ Updated model '{updatedEnvironment.ModelName}' in profile '{profileName}'.");
        }

        public void CheckModelDeployment(string profileName, string modelName)
        {
            string linkPath = Path.Combine(_deploymentBasePath, profileName);

            if (Directory.Exists(linkPath))
            {
                Console.WriteLine($"✅ Model '{modelName}' is deployed at {linkPath}.");
                UpdateProfileFile(profileName, new ProfileEnvironmentModel { ModelName = modelName, IsDeployed = true });
            }
            else
            {
                Console.WriteLine($"❌ Model '{modelName}' is NOT deployed.");
                UpdateProfileFile(profileName, new ProfileEnvironmentModel { ModelName = modelName, IsDeployed = false });
            }
        }

        private void StopW3SVC()
        {
            try
            {
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "net",
                        Arguments = "stop W3SVC",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();

                Console.WriteLine("✅ W3SVC stopped.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to stop W3SVC: {ex.Message}");
            }
        }

        private void StartW3SVC()
        {
            try
            {
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "net",
                        Arguments = "start W3SVC",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                process.WaitForExit();

                Console.WriteLine("✅ W3SVC restarted.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to start W3SVC: {ex.Message}");
            }
        }
    }

}

