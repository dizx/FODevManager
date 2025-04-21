using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Utils;
using FODevManager.Messages;
using System.Reflection;
using System.Dynamic;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FODevManager.Services
{
    public class ModelDeploymentService
    {
        private readonly FileService _fileService;
        private readonly string _deploymentBasePath;
        private readonly string _defaultSourceDirectory;

        public ModelDeploymentService(AppConfig config, FileService fileService)
        {
            _fileService = fileService;
            _deploymentBasePath = config.DeploymentBasePath;
            _defaultSourceDirectory = config.DefaultSourceDirectory;

            // Ensure directories exist
            FileHelper.EnsureDirectoryExists(_deploymentBasePath);
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);
        }


        public void DeployModel(string profileName, string modelName)
        {
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);

                DeploySingleModel(profile, modelName);
                UpdateProfileFile(profileName, new ProfileEnvironmentModel { ModelName = modelName, IsDeployed = true });
            }
            finally
            {
                MessageLogger.Info("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                ServiceHelper.StartW3SVC();
            }
        }

        public void DeployAllUndeployedModels(string profileName)
        {
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);

                bool anyUndeployed = false;

                foreach (var model in profile.Environments)
                {
                    string linkPath = Path.Combine(_deploymentBasePath, profileName);

                    if (!model.IsDeployed && !Directory.Exists(linkPath))
                    {
                        MessageLogger.Info($"🔄 Deploying model: {model.ModelName}...");
                        DeploySingleModel(profile, model.ModelName);
                        model.IsDeployed = true;
                        anyUndeployed = true;
                    }
                }

                if (!anyUndeployed)
                {
                    MessageLogger.Info($"✅ All models in profile '{profileName}' are already deployed.");
                    return;
                }

                _fileService.SaveProfile(profile);

                MessageLogger.Info($"✅ Deployment complete. Updated profile '{profileName}'.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error deploying models: {ex.Message}");
            }
            finally
            {
                MessageLogger.Info("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                ServiceHelper.StartW3SVC();
            }
        }

        public void UnDeployModel(string profileName, string modelName)
        {
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);
                var environment = GetProfileEnvironment(profile, modelName);
                string linkPath = Path.Combine(_deploymentBasePath, modelName);

                if (!Directory.Exists(linkPath))
                {
                    MessageLogger.Error($"❌ Model '{modelName}' is NOT deployed.");
                    return;
                }

                try
                {
                    MessageLogger.Info($"🔄 Removing deployment link for model '{modelName}'...");
                    Directory.Delete(linkPath, true);
                    MessageLogger.Highlight($"✅ Model '{modelName}' successfully undeployed.");

                    // Update profile status
                    environment.IsDeployed = false;
                    _fileService.SaveProfile(profile);
                }
                catch (Exception ex)
                {
                    MessageLogger.Error($"❌ Error undeploying model '{modelName}': {ex.Message}");
                }
            }
            finally
            {
                MessageLogger.Info("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                ServiceHelper.StartW3SVC();
            }
        }

        public void UnDeployAllModels(string profileName)
        {
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);
                bool anyDeployed = false;

                foreach (var model in profile.Environments)
                {
                    string linkPath = Path.Combine(_deploymentBasePath, model.ModelName);

                    if (Directory.Exists(linkPath))
                    {
                        MessageLogger.Info($"🔄 Removing deployment link for model '{model.ModelName}'...");
                        Directory.Delete(linkPath, true);
                        model.IsDeployed = false;
                        anyDeployed = true;
                    }
                }

                if (!anyDeployed)
                {
                    MessageLogger.Info($"✅ All models in profile '{profileName}' are already undeployed.");
                    return;
                }

                _fileService.SaveProfile(profile);
                MessageLogger.Info($"✅ Undeployment complete. Updated profile '{profileName}'.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error undeploying models: {ex.Message}");
            }
            finally
            {
                MessageLogger.Info("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                ServiceHelper.StartW3SVC();
            }
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
                MessageLogger.Info($"✅ Model '{modelName}' deployed successfully.");
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
            var profile = _fileService.LoadProfile(profileName);

            // Find the model in the profile
            var existingEnvironment = GetProfileEnvironment(profile, updatedEnvironment.ModelName);

            if (!string.IsNullOrEmpty(updatedEnvironment.ModelRootFolder))
                existingEnvironment.ModelRootFolder = updatedEnvironment.ModelRootFolder;

            // Update the existing model entry
            if (!string.IsNullOrEmpty(updatedEnvironment.ProjectFilePath))
                existingEnvironment.ProjectFilePath = updatedEnvironment.ProjectFilePath;
            existingEnvironment.IsDeployed = updatedEnvironment.IsDeployed;

            // Save the updated profile
            _fileService.SaveProfile(profile);
            MessageLogger.Info($"✅ Updated model '{updatedEnvironment.ModelName}' in profile '{profileName}'.");
        }

        public void CheckModelDeployment(string profileName, string modelName)
        {
            var env = GetProfileEnvironment(_fileService.LoadProfile(profileName), modelName);

            if(string.IsNullOrEmpty(env.ModelRootFolder))
            {
                string modelRootPath = FileHelper.GetModelRootFolder(env.ProjectFilePath);
                if (!Directory.Exists(modelRootPath))
                {
                    MessageLogger.Error($"❌ Error: Model root folder not found at {modelRootPath}.");
                    return;
                }
                env.ModelRootFolder = modelRootPath;
            }

            MessageLogger.Info($"✅ Model '{env.ModelName}' source path exists: {File.Exists(env.ProjectFilePath)}");

            string linkPath = Path.Combine(_deploymentBasePath, modelName);

            if (Directory.Exists(linkPath))
            {
                MessageLogger.Info($"✅ Model '{modelName}' is deployed at {linkPath}.");
                
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

            UpdateProfileFile(profileName, env);
        }

        public bool IsModelActuallyDeployed(ProfileEnvironmentModel env)
        {
            if (string.IsNullOrEmpty(env.ModelRootFolder))
            {
                return Directory.Exists(env.ModelRootFolder);
            }
            return false;
        }

        public bool CheckIfGitRepository(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
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
                    MessageLogger.Info($"✅ Model '{modelName}' Git repository: {gitRemoteUrl}");

                    MessageLogger.Info($"✅ Model '{modelName}' Git active branch: {GitHelper.GetActiveBranch(projectRootPath)}");

                    model.GitUrl = gitRemoteUrl;
                    _fileService.SaveProfile(profile); 

                    return true;
                }
                else
                {
                    MessageLogger.Warning($"❌ Model '{modelName}' is NOT a Git repository.");
                }
            }
            catch
            {
                MessageLogger.Error($"❌ Eror getting git status for '{modelName}' in profile '{profileName}'");
            }

            return false;

        }

        public string? GetActiveGitBranch(string profileName, string modelName)
        {
            try
            {
                var model = GetProfileEnvironment(_fileService.LoadProfile(profileName), modelName);
                if (model == null)
                {
                    return "";
                }

                //string projectRootPath = FileHelper.GetModelRootFolder(model.ModelRootFolder);

                if (GitHelper.IsGitRepository(model.ModelRootFolder, out string gitRemoteUrl))
                {
                    return GitHelper.GetActiveBranch(model.ModelRootFolder);
                }
            }
            catch
            {
                return "";
            }
            return "";
        }

        public void OpenGitRepositoryUrl(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
            var model = GetProfileEnvironment(profile, modelName);

            if (model == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' not found in profile '{profileName}'.");
                return;
            }

            if (GitHelper.IsGitRepository(model.ModelRootFolder))
            {
                GitHelper.OpenGitRemoteUrl(model.ModelRootFolder);
            }
            else
            {
                MessageLogger.Info($"❌ The model '{modelName}' in profile '{profileName}' is not a Git repository.");
            }
        }

        public bool AssignPeriTask(string profileName, string modelName, string periTask)
        {
            var profile = _fileService.LoadProfile(profileName);
            var model = GetProfileEnvironment(profile, modelName);

            if (string.IsNullOrWhiteSpace(periTask))
            {
                MessageLogger.Warning("⚠️ PeriTask cannot be empty.");
                return false;
            }

            string featureBranch = $"feature/task-{periTask}";

            string repoPath = model.ModelRootFolder;
            if (!Directory.Exists(repoPath))
            {
                MessageLogger.Error($"❌ Model root folder does not exist: {repoPath}");
                return false;
            }

            if (GitHelper.ChangeBranch(repoPath, featureBranch))
            {
                model.PeriTask = periTask;
                _fileService.SaveProfile(profile);
                MessageLogger.Highlight($"✅ Assigned PeriTask '{periTask}' and switched to branch '{featureBranch}'.");
            }
            else
            {
                MessageLogger.Error($"❌ Failed to switch to branch '{featureBranch}' for model '{modelName}'.");
                return false;
            }

            return true;
        }


    }
}

