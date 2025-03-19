using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Utils;
using FoDevManager.Messages;
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
            MessageLogger.Write("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            StopW3SVC();

            try
            {
                var profile = LoadProfileFile(profileName);

                DeploySingleModel(profile, modelName);
                UpdateProfileFile(profileName, new ProfileEnvironmentModel { ModelName = modelName, IsDeployed = true });
            }
            finally
            {
                MessageLogger.Write("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                StartW3SVC();
            }
        }

        public void DeployAllUndeployedModels(string profileName)
        {
            MessageLogger.Write("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
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
                        MessageLogger.Write($"🔄 Deploying model: {model.ModelName}...");
                        DeploySingleModel(profile, model.ModelName);
                        model.IsDeployed = true;
                        anyUndeployed = true;
                    }
                }

                if (!anyUndeployed)
                {
                    MessageLogger.Write($"✅ All models in profile '{profileName}' are already deployed.");
                    return;
                }

                SaveProfileFile(profile);

                MessageLogger.Write($"✅ Deployment complete. Updated profile '{profileName}'.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error deploying models: {ex.Message}");
            }
            finally
            {
                MessageLogger.Write("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                StartW3SVC();
            }
        }

        public void UnDeployModel(string profileName, string modelName)
        {
            MessageLogger.Write("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            StopW3SVC();

            try
            {
                var profile = LoadProfileFile(profileName);
                var environment = GetProfileEnvironment(profile, modelName);
                string linkPath = Path.Combine(_deploymentBasePath, modelName);

                if (!Directory.Exists(linkPath))
                {
                    MessageLogger.Error($"❌ Model '{modelName}' is NOT deployed.");
                    return;
                }

                try
                {
                    MessageLogger.Write($"🔄 Removing deployment link for model '{modelName}'...");
                    Directory.Delete(linkPath, true);
                    MessageLogger.Highlight($"✅ Model '{modelName}' successfully undeployed.");

                    // Update profile status
                    environment.IsDeployed = false;
                    SaveProfileFile(profile);
                }
                catch (Exception ex)
                {
                    MessageLogger.Error($"❌ Error undeploying model '{modelName}': {ex.Message}");
                }
            }
            finally
            {
                MessageLogger.Write("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                StartW3SVC();
            }
        }

        public void UnDeployAllModels(string profileName)
        {
            MessageLogger.Write("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
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
                        MessageLogger.Write($"🔄 Removing deployment link for model '{model.ModelName}'...");
                        Directory.Delete(linkPath, true);
                        model.IsDeployed = false;
                        anyDeployed = true;
                    }
                }

                if (!anyDeployed)
                {
                    MessageLogger.Write($"✅ All models in profile '{profileName}' are already undeployed.");
                    return;
                }

                SaveProfileFile(profile);
                MessageLogger.Write($"✅ Undeployment complete. Updated profile '{profileName}'.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error undeploying models: {ex.Message}");
            }
            finally
            {
                MessageLogger.Write("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
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
                    MessageLogger.Error($"❌ Error: Metadata folder not found at {sourcePath}.");
                    return;
                }

                if (Directory.Exists(linkPath))
                {
                    MessageLogger.Error($"Removing existing link: {linkPath}");
                    Directory.Delete(linkPath);
                }

                Directory.CreateSymbolicLink(linkPath, sourcePath);
                MessageLogger.Write($"✅ Model '{modelName}' deployed successfully.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error deploying model '{modelName}': {ex.Message}");
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
            MessageLogger.Write($"✅ Updated model '{updatedEnvironment.ModelName}' in profile '{profileName}'.");
        }

        public void CheckModelDeployment(string profileName, string modelName)
        {
            var env = GetProfileEnvironment(LoadProfileFile(profileName), modelName);

            MessageLogger.Write($"✅ Model '{env.ModelName}' source path exists: {File.Exists(env.ProjectFilePath)}");

            string linkPath = Path.Combine(_deploymentBasePath, modelName);

            if (Directory.Exists(linkPath))
            {
                MessageLogger.Write($"✅ Model '{modelName}' is deployed at {linkPath}.");
                
                if (env.IsDeployed == false)
                {
                    env.IsDeployed = true;
                    UpdateProfileFile(profileName, env);
                }
            }
            else
            {
                MessageLogger.Warning($"❌ Model '{modelName}' is NOT deployed.");
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

                MessageLogger.Write("✅ W3SVC stopped.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Failed to stop W3SVC: {ex.Message}");
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

                MessageLogger.Write("✅ W3SVC restarted.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Failed to start W3SVC: {ex.Message}");
            }
        }

        public bool CheckIfGitRepository(string profileName, string modelName)
        {
            var profile = LoadProfileFile(profileName);
            var model = GetProfileEnvironment(profile, modelName);

            if (model == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' not found in profile '{profileName}'.");
                return false;
            }

            try
            {
                string projectRootPath = FileHelper.GetModelRootFolder(model.ProjectFilePath);

                if (GitHelper.IsGitRepository(projectRootPath, out string gitRemoteUrl))
                {
                    MessageLogger.Write($"✅ Model '{modelName}' Git repository: {gitRemoteUrl}");

                    model.GitUrl = gitRemoteUrl;
                    SaveProfileFile(profile);

                    return true;
                }
                else
                {
                    MessageLogger.Write($"❌ Model '{modelName}' is NOT a Git repository.");
                }
            }
            catch
            {
                MessageLogger.Error($"❌ Eror getting git status for '{modelName}' in profile '{profileName}'");
            }

            return false;

        }

        public void OpenGitRepositoryUrl(string profileName, string modelName)
        {
            var profile = LoadProfileFile(profileName);
            var model = GetProfileEnvironment(profile, modelName);

            if (model == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' not found in profile '{profileName}'.");
                return;
            }

            string projectRootPath = FileHelper.GetModelRootFolder(model.ProjectFilePath);

            if (GitHelper.IsGitRepository(projectRootPath))
            {
                GitHelper.OpenGitRemoteUrl(projectRootPath);
            }
            else
            {
                MessageLogger.Write($"❌ The model '{modelName}' in profile '{profileName}' is not a Git repository.");
            }
        }


    }

}

