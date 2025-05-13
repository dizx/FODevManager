using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Utils;
using FODevManager.Messages;
using FODevManager.Shared.Utils;
using System.Text.RegularExpressions;

namespace FODevManager.Services
{
    public class ProfileService
    {
        private readonly string _defaultSourceDirectory;
        private readonly string _deploymentBasePath;
        private readonly string _profileStoragePath;
        private readonly FileService _fileService;
        private readonly VisualStudioSolutionService _solutionService;
        private readonly ModelDeploymentService _modelDeploymentService;

        public ProfileService(AppConfig config, FileService fileService, VisualStudioSolutionService solutionService, ModelDeploymentService modelDeploymentService)
        {
            _defaultSourceDirectory = config.DefaultSourceDirectory;
            _deploymentBasePath = config.DeploymentBasePath;
            _profileStoragePath = config.ProfileStoragePath;
            _fileService = fileService;
            _solutionService = solutionService;
            _modelDeploymentService = modelDeploymentService;
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);
        }

        public void CreateProfile(string profileName)
        {
            if (_fileService.ExistProfile(profileName))
            {
                MessageLogger.Warning("Profile already exists.");
                return;
            }

            // Create the solution file and get its path
            string solutionFilePath = _solutionService.CreateSolutionFile(profileName);

            var profile = new ProfileModel
            {
                ProfileName = profileName,
                SolutionFilePath = solutionFilePath
            };

            _fileService.SaveProfile(profile, skipExistCheck: true);
            
            MessageLogger.Info($"✅ Profile '{profileName}' created with solution file: {solutionFilePath}");
        }

        public bool SwitchProfile(string newProfileName)
        {
            var currentProfileName = GetActiveProfileName();
            if (currentProfileName == newProfileName)
            {
                MessageLogger.Info($"ℹ️ Profile '{newProfileName}' is already active.");
                return false;
            }

            if (currentProfileName.IsNullOrEmpty())
            {
                MessageLogger.Info("ℹ️ No active profile found. Proceeding to switch.");
            }

            if (!currentProfileName.IsNullOrEmpty())
            {
                var currentProfile = _fileService.LoadProfile(currentProfileName);
                foreach (var model in currentProfile.Environments)
                {
                    if (GitHelper.IsGitRepository(model.ModelRootFolder) && GitHelper.HasUncommittedChanges(model.ModelRootFolder))
                    {
                        MessageLogger.Error($"❌ Uncommitted Git changes found in model '{model.ModelName}'. Switch aborted.");
                        return false;
                    }
                }

                MessageLogger.Info($"🧹 Undeploying models from '{currentProfileName}'...");
                _modelDeploymentService.UnDeployAllModels(currentProfileName);
            }

            MessageLogger.Info($"📂 Switching to profile '{newProfileName}'...");
            UpdateDeploymentStatus(newProfileName);

            _modelDeploymentService.DeployAllUndeployedModels(newProfileName);
            ApplyDatabase(newProfileName);
            SetActiveProfile(newProfileName);
            MessageLogger.Highlight($"✅ Successfully switched to profile '{newProfileName}'.");

            return true;
        }


        public void ImportProfile(string importPath)
        {
            if (!File.Exists(importPath))
            {
                MessageLogger.Error($"❌ Profile file not found: {importPath}");
                return;
            }

            try
            {
                var profile = FileHelper.LoadJson<ProfileModel>(importPath);

                if (profile == null || string.IsNullOrWhiteSpace(profile.ProfileName))
                {
                    MessageLogger.Error("❌ Invalid profile file.");
                    return;
                }

                string solutionFilePath = _solutionService.CreateSolutionFile(profile.ProfileName);

                profile.SolutionFilePath = solutionFilePath;
                profile.IsActive = false;

                string profileDestPath = Path.Combine(_profileStoragePath, profile.ProfileName + ".json");

                if (File.Exists(profileDestPath))
                {
                    MessageLogger.Warning($"⚠️ Profile '{profile.ProfileName}' already exists. It will be overwritten.");
                }

                MessageLogger.Info($"📥 Importing profile '{profile.ProfileName}'...");


                foreach (var environment in profile.Environments)
                {
                    var modelFolderName = environment.GitUrl.IsNullOrEmpty() ? environment.ModelName : ExtractAzureDevOpsRepo(environment.GitUrl);
                    var modelFolder = Path.Combine(_defaultSourceDirectory, modelFolderName);
                    FileHelper.EnsureDirectoryExists(modelFolder);

                    if (!environment.GitUrl.IsNullOrEmpty())
                    {
                        if (GitHelper.IsGitRepository(modelFolder))
                        {
                            MessageLogger.Warning($"⚠️ Git repo already exists at {modelFolder}. Skipping clone.");
                        }
                        else if (!GitHelper.CloneRepository(environment.GitUrl, modelFolder))
                        {
                            MessageLogger.Error($"❌ Failed to clone repository for {environment.ModelName}.");
                            continue;
                        }
                    }

                    environment.ProjectFilePath = FileHelper.GetProjectFilePath(environment.ModelName, modelFolder); ;
                    environment.ModelRootFolder = modelFolder;
                    environment.MetadataFolder = FileHelper.GetMetadataFolder(environment.ModelName, modelFolder);

                    string deploymentLinkPath = Path.Combine(_deploymentBasePath, environment.ModelName);
                    bool isAlreadyDeployed = Directory.Exists(deploymentLinkPath);

                    environment.IsDeployed = isAlreadyDeployed;

                    _solutionService.AddProjectToSolution(profile.ProfileName, environment.ModelName, environment.ProjectFilePath);
                }

                FileHelper.SaveJson(profileDestPath, profile);
                MessageLogger.Highlight($"✅ Profile '{profile.ProfileName}' imported successfully.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Failed to import profile: {ex.Message}");
            }
        }

        
        public void SetDatabaseName(string profileName, string dbName)
        {
            var profile = _fileService.LoadProfile(profileName);
            profile.DatabaseName = dbName;
            _fileService.SaveProfile(profile);
            MessageLogger.Info($"✅ Database name '{dbName}' set for profile '{profileName}'.");
        }

        public void SetActiveProfile(string profileName)
        {
            foreach (var profile in _fileService.GetAllProfiles())
            {
                if (profile is null) continue;

                profile.IsActive = string.Equals(profile.ProfileName, profileName, StringComparison.OrdinalIgnoreCase);
                _fileService.SaveProfile(profile);
            }

            MessageLogger.Highlight($"📌 Profile '{profileName}' marked as active.");
        }

        public string? GetActiveProfileName()
        {
            var allProfiles = _fileService.GetAllProfileNames();

            foreach (var profileName in allProfiles)
            {
                var profile = _fileService.LoadProfile(profileName.ToString());
                if (profile != null && profile.IsActive)
                    return profile.ProfileName;
            }

            return null;
        }


        public void ApplyDatabase(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);

            if (profile.DatabaseName.IsNullOrEmpty())
            {
                MessageLogger.Warning("ℹ️ No database name configured for this profile.");
                return;
            }
            
            var currentDb = WebConfigHelper.GetCurrentDatabaseName();

            if (string.Equals(currentDb, profile.DatabaseName, StringComparison.OrdinalIgnoreCase))
            {
                MessageLogger.Info($"ℹ️ Database is already set to '{currentDb}'. No change needed.");
                return;
            }

            try
            {
                

                MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
                ServiceHelper.StopW3SVC();

                WebConfigHelper.UpdateWebConfigDatabase(profile.DatabaseName);
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error applying database '{profile.DatabaseName}': {ex.Message}");
            }
            finally
            {
                ServiceHelper.StartW3SVC();
            }
        }

        public void AddEnvironment(string profileName, string modelName, string environmentPath)
        {
            string modelRootPath = FileHelper.GetModelRootFolder(environmentPath);
            if (!Directory.Exists(modelRootPath))
            {
                MessageLogger.Error($"❌ Error: Model root folder not found at {modelRootPath}.");
                return;
            }

            //Finds model name from path
            if (modelName.IsNullOrEmpty())
            {
                var metadataPath = Path.Combine(modelRootPath, "Metadata");
                if (!Directory.Exists(metadataPath))
                    metadataPath = Path.Combine(Path.GetDirectoryName(modelRootPath), "Metadata");

                if (Directory.Exists(metadataPath))
                {
                    var subfolder = Directory.GetDirectories(metadataPath).FirstOrDefault();
                    if (!subfolder.IsNullOrEmpty())
                        modelName = Path.GetFileName(subfolder);
                }

                if (modelName.IsNullOrEmpty())
                    throw new Exception("❌ Unable to find model name from Metadata folder.");
            }

            var projectFilePath = GetProjectFilePath(modelName, modelRootPath);
            if (!File.Exists(projectFilePath))
            {
                MessageLogger.Info($"{projectFilePath} does not exist.");
                if(Singleton<Engine>.Instance.EnvironmentType == EnvironmentType.Console)
                    MessageLogger.Info("Usage: fodev.exe -profile \"ProfileName\" -model \"ModelName\" add \"ProjectFilePath\"");
                return;
            }

            var metaDataFolder = FileHelper.GetMetadataFolder(modelName, modelRootPath);
            if (!Directory.Exists(metaDataFolder))
            {
                MessageLogger.Error($"❌ Error: Metadata folder not found at {metaDataFolder}.");
                return;
            }

            var profile = _fileService.LoadProfile(profileName);

            if (profile.Environments.Any(e => e.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageLogger.Warning($"⚠️ Model '{modelName}' is already in the profile '{profileName}'. Skipping add.");
                return;
            }

            string deploymentLinkPath = Path.Combine(_deploymentBasePath, modelName);
            bool isAlreadyDeployed = Directory.Exists(deploymentLinkPath);

            profile.Environments.Add(new ProfileEnvironmentModel
            {
                ModelName = modelName,
                ModelRootFolder = modelRootPath,
                ProjectFilePath = projectFilePath,
                MetadataFolder = metaDataFolder,
                IsDeployed = isAlreadyDeployed
            });

            _fileService.SaveProfile(profile);

            _solutionService.AddProjectToSolution(profileName, modelName, projectFilePath);
            MessageLogger.Info($"✅ Model '{modelName}' added to profile '{profileName}' and included in solution.");

            _modelDeploymentService.CheckIfGitRepository(profileName, modelName);
        }

        private string GetProjectFilePath(string modelName, string projectFilePath)
        {
            if (projectFilePath.IsNullOrEmpty())
            {
                if(FileHelper.TryFilePath(Path.Combine(_defaultSourceDirectory, modelName, "Project", $"{modelName}.rnrproj"), out string returnPath))
                { 
                    return returnPath; 
                }
            }
            return FileHelper.GetProjectFilePath(modelName, projectFilePath);
        }

        private static string ExtractAzureDevOpsProject(string gitUrl)
        {
            var match = Regex.Match(gitUrl, @"visualstudio\.com\/([^\/]+)\/_git\/");

            if (match.Success && match.Groups.Count > 1)
                return Uri.UnescapeDataString(match.Groups[1].Value);

            return string.Empty;
        }

        private static string ExtractAzureDevOpsRepo(string gitUrl)
        {
            var match = Regex.Match(gitUrl, @"_git\/([^\/]+)$");

            if (match.Success && match.Groups.Count > 1)
                return Uri.UnescapeDataString(match.Groups[1].Value);

            return string.Empty;
        }


        public void OpenVisualStudioSolution(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);

            _solutionService.OpenSolution(profile.SolutionFilePath);
        }

        public void CheckProfile(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);
            if(profile == null)
            {
                throw new Exception($"Profile '{profileName}' is empty");
            }

            MessageLogger.Info($"Profile: {profile.ProfileName}");
            foreach (var env in profile.Environments)
            {
                _modelDeploymentService.CheckModelDeployment(profileName, env.ModelName);
                _modelDeploymentService.CheckIfGitRepository(profileName, env.ModelName);
            }
        }
        public void GitFetchLatest(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);

            MessageLogger.Info($"Fetch Git for profile: {profile.ProfileName}");
            foreach (var env in profile.Environments)
            {
                if (!env.ModelRootFolder.IsNullOrEmpty())
                {
                    GitHelper.FetchFromRemote(env.ModelName, env.ModelRootFolder);
                }    
            }
        }

        public void DeleteProfile(string profileName)
        {
            string solutionFilePath = _solutionService.GetSolutionFilePath(profileName);
            
            var profile = _fileService.LoadProfile(solutionFilePath);

            // Remove each project from the solution before deleting the profile
            foreach (var model in profile.Environments)
            {
                _solutionService.RemoveProjectFromSolution(profileName, model.ModelName);
            }

            if (File.Exists(solutionFilePath))
            {
                File.Delete(solutionFilePath);
                MessageLogger.Warning($"Solution file '{solutionFilePath}' deleted.");
            }

            _fileService.DeleteProfile(profileName);

            MessageLogger.Info($"Profile '{profileName}' and all associated models removed.");
        }


        public void RemoveModelFromProfile(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
            var model = profile.Environments.Find(m => m.ModelName == modelName);

            if (model == null)
            {
                MessageLogger.Warning($"Model '{modelName}' not found in profile '{profileName}'.");
                return;
            }

            _solutionService.RemoveProjectFromSolution(profileName, model.ModelName);

            profile.Environments.Remove(model);

            _fileService.SaveProfile(profile);

            MessageLogger.Info($"Model '{modelName}' removed from profile '{profileName}'.");
        }

        public void ListProfiles()
        {
            var profiles = _fileService.GetAllProfileNames();

            MessageLogger.Info("Installed Profiles:");
            foreach (var profileName in profiles)
            {
                MessageLogger.Info($"- {profileName}");
            }
        }

        public void ListModelsInProfile(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);

            if (profile.Environments.Count == 0)
            {
                MessageLogger.Warning($"No models found in profile '{profileName}'.");
                return;
            }

            MessageLogger.Info($"Models in Profile '{profileName}':");
            foreach (var model in profile.Environments)
            {
                string status = model.IsDeployed ? "✅ Deployed" : "❌ Not Deployed";
                string gitStatus = model.GitUrl.IsNullOrEmpty() ? "" : "✅ Git Repo" ; 
                MessageLogger.Info($"   - {model.ModelName}\t\t - {status} - { gitStatus }");
            }
        }

        
        public List<ProfileEnvironmentModel> GetModelsInProfile(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);
            if (profile.Environments.Count == 0)
            {
                return new List<ProfileEnvironmentModel>();
            }
            return profile.Environments;
        }

        public ProfileEnvironmentModel GetModel(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
            if (profile.Environments.Count == 0)
            {
                return new ProfileEnvironmentModel();
            }
            return profile.Environments.FirstOrDefault(x => x.ModelName == modelName);
        }

        public void UpdateDeploymentStatus(string profileName)
        {
            bool updated = false;
            var profile = _fileService.LoadProfile(profileName);

            foreach (var model in profile.Environments)
            {
                bool shouldBeMarkedAsDeployed = _modelDeploymentService.IsModelActuallyDeployed(model);
                if (model.IsDeployed != shouldBeMarkedAsDeployed)
                {
                    model.IsDeployed = shouldBeMarkedAsDeployed;
                    updated = true;
                }
            }

            if (updated)
            {
                _fileService.SaveProfile(profile);
                MessageLogger.Info($"🔍 Deployment status updated for profile '{profileName}'");
            }
        }

    }
}
