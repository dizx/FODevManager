using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Utils;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
                var profile = LoadProfileFile(profileName);

                DeploySingleModel(profile, modelName);
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
                string profilePath = ProfilePath(profileName);

                var profile = LoadProfileFile(profileName);

                bool anyUndeployed = false;

                foreach (var model in profile.Environments)
                {
                    string linkPath = Path.Combine(_deploymentBasePath, profileName);

                    if (!model.IsDeployed && !Directory.Exists(linkPath))
                    {
                        Console.WriteLine($"🔄 Deploying model: {model.ModelName}...");
                        DeploySingleModel(profile, model.ModelName);
                        model.IsDeployed = true;
                        anyUndeployed = true;
                    }
                }

                if (!anyUndeployed)
                {
                    Console.WriteLine($"✅ All models in profile '{profileName}' are already deployed.");
                    return;
                }

                SaveProfileFile(profile);

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

        public void UnDeployModel(string profileName, string modelName)
        {
            Console.WriteLine("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            StopW3SVC();

            try
            {
                var profile = LoadProfileFile(profileName);
                var environment = GetProfileEnvironment(profile, modelName);
                string linkPath = Path.Combine(_deploymentBasePath, modelName);

                if (!Directory.Exists(linkPath))
                {
                    Console.WriteLine($"❌ Model '{modelName}' is NOT deployed.");
                    return;
                }

                try
                {
                    Console.WriteLine($"🔄 Removing deployment link for model '{modelName}'...");
                    Directory.Delete(linkPath, true);
                    Console.WriteLine($"✅ Model '{modelName}' successfully undeployed.");

                    // Update profile status
                    environment.IsDeployed = false;
                    SaveProfileFile(profile);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error undeploying model '{modelName}': {ex.Message}");
                }
            }
            finally
            {
                Console.WriteLine("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                StartW3SVC();
            }
        }

        public void UnDeployAllModels(string profileName)
        {
            Console.WriteLine("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            StopW3SVC();

            try
            {
                var profile = LoadProfileFile(profileName);
                bool anyDeployed = false;

                foreach (var model in profile.Environments)
                {
                    string linkPath = Path.Combine(_deploymentBasePath, model.ModelName);

                    if (Directory.Exists(linkPath))
                    {
                        Console.WriteLine($"🔄 Removing deployment link for model '{model.ModelName}'...");
                        Directory.Delete(linkPath, true);
                        model.IsDeployed = false;
                        anyDeployed = true;
                    }
                }

                if (!anyDeployed)
                {
                    Console.WriteLine($"✅ All models in profile '{profileName}' are already undeployed.");
                    return;
                }

                SaveProfileFile(profile);
                Console.WriteLine($"✅ Undeployment complete. Updated profile '{profileName}'.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error undeploying models: {ex.Message}");
            }
            finally
            {
                Console.WriteLine("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                StartW3SVC();
            }
        }


        private ProfileModel LoadProfileFile(string profileName)
        {
            string profilePath = ProfilePath(profileName);

            if (!File.Exists(profilePath))
            {
                throw new Exception($"Profile '{profileName}' does not exist");
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);

            return profile == null ? throw new Exception($"Can't load profile '{profileName}'") : profile;
        }

        private void SaveProfileFile(ProfileModel profile)
        {
            var profileName = profile.ProfileName;

            string profilePath = ProfilePath(profileName);

            if (!File.Exists(profilePath))
            {
                throw new Exception($"Profile '{profileName}' does not exist");
            }

            FileHelper.SaveJson(profilePath, profile);
            
        }

        private void DeploySingleModel(ProfileModel profile, string modelName)
        {
            try
            {
                var environment = GetProfileEnvironment(profile, modelName);
                string targetDir = Path.Combine(_deploymentBasePath, modelName);

                string linkPath = targetDir;
                //string sourcePath = Path.Combine(Path.GetDirectoryName(environment.ProjectFilePath), "Metadata");
                string sourcePath = environment.MetadataFolder;

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

        private ProfileEnvironmentModel GetProfileEnvironment(ProfileModel profile, string modelName)
        {
            var environment = profile.Environments.FirstOrDefault(e => e.ModelName == modelName);

            if (environment == null)
            {
                throw new Exception($"Model '{modelName}' not found in profile '{profile.ProfileName}'.");
            }

            return environment;
        }

        private void UpdateProfileFile(string profileName, ProfileEnvironmentModel updatedEnvironment)
        {
            var profile = LoadProfileFile(profileName);

            // Find the model in the profile
            var existingEnvironment = GetProfileEnvironment(profile, updatedEnvironment.ModelName);

            // Update the existing model entry
            if(!string.IsNullOrEmpty(updatedEnvironment.ProjectFilePath))
                existingEnvironment.ProjectFilePath = updatedEnvironment.ProjectFilePath;
            existingEnvironment.IsDeployed = updatedEnvironment.IsDeployed;

            // Save the updated profile
            SaveProfileFile(profile);
            Console.WriteLine($"✅ Updated model '{updatedEnvironment.ModelName}' in profile '{profileName}'.");
        }

        public void CheckModelDeployment(string profileName, string modelName)
        {
            var env = GetProfileEnvironment(LoadProfileFile(profileName), modelName);

            Console.WriteLine($"- Model: {env.ModelName}, Source Path Exists: {File.Exists(env.ProjectFilePath)}");

            string linkPath = Path.Combine(_deploymentBasePath, modelName);

            if (Directory.Exists(linkPath))
            {
                Console.WriteLine($"✅ Model '{modelName}' is deployed at {linkPath}.");
                
                if (env.IsDeployed == false)
                {
                    env.IsDeployed = true;
                    UpdateProfileFile(profileName, env);
                }
            }
            else
            {
                Console.WriteLine($"❌ Model '{modelName}' is NOT deployed.");
                if(env.IsDeployed == true)
                {
                    env.IsDeployed = false;
                    UpdateProfileFile(profileName, env);
                }
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

