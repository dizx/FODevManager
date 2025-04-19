using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Utils;
using FoDevManager.Messages;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using FoDevManager.Utils;

namespace FODevManager.Services
{
    public class ProfileService
    {
        private readonly string _appDataPath;
        private readonly string _defaultSourceDirectory;
        private readonly VisualStudioSolutionService _solutionService;
        private readonly ModelDeploymentService _modelDeploymentService;

        private string ProfilePath(string profileName) => Path.Combine(_appDataPath, $"{profileName}.json");

        public ProfileService(AppConfig config, VisualStudioSolutionService solutionService, ModelDeploymentService modelDeploymentService)
        {
            _appDataPath = config.ProfileStoragePath;
            _defaultSourceDirectory = config.DefaultSourceDirectory;
            _solutionService = solutionService;
            _modelDeploymentService = modelDeploymentService;
            FileHelper.EnsureDirectoryExists(_appDataPath);
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);
        }

        public void CreateProfile(string profileName)
        {
            string profilePath = ProfilePath(profileName);

            if (File.Exists(profilePath))
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

            FileHelper.SaveJson(profilePath, profile);
            MessageLogger.Info($"✅ Profile '{profileName}' created with solution file: {solutionFilePath}");
        }

        public void SetDatabase(string profileName, string dbName)
        {
            var profile = LoadProfile(profileName);
            profile.DatabaseName = dbName;
            SaveProfile(profile);
            MessageLogger.Info($"✅ Database name '{dbName}' set for profile '{profileName}'.");
        }

        public void ApplyDatabase(string profileName)
        {
            var profile = LoadProfile(profileName);

            if (string.IsNullOrEmpty(profile.DatabaseName))
            {
                MessageLogger.Warning("ℹ️ No database name configured for this profile.");
                return;
            }

            try
            {
                var currentDb = WebConfigHelper.GetCurrentDatabaseName();

                if (string.Equals(currentDb, profile.DatabaseName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageLogger.Info($"ℹ️ Database is already set to '{currentDb}'. No change needed.");
                    return;
                }

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


        public void AddEnvironment(string profileName, string modelName, string projectFilePath)
        {
            projectFilePath = GetProjectFilePath(profileName, modelName, projectFilePath);
            if (!File.Exists(projectFilePath))
            {
                MessageLogger.Info($"{projectFilePath} does not exist.");
                MessageLogger.Info("Usage: fodev.exe -profile \"ProfileName\" -model \"ModelName\" add \"ProjectFilePath\"");
                return;
            }

            var metaDataFolder = FileHelper.GetMetadataFolder(modelName, projectFilePath);
            if (!Directory.Exists(metaDataFolder))
            {
                MessageLogger.Error($"❌ Error: Metadata folder not found at {metaDataFolder}.");
                return;
            }

            string modelRootPath = FileHelper.GetModelRootFolder(projectFilePath);
            if (!Directory.Exists(modelRootPath))
            {
                MessageLogger.Error($"❌ Error: Model root folder not found at {modelRootPath}.");
                return;
            }

            var profile = LoadProfile(profileName);
            
            profile.Environments.Add(new ProfileEnvironmentModel
            {
                ModelName = modelName,
                ModelRootFolder = modelRootPath,
                ProjectFilePath = projectFilePath,
                MetadataFolder = metaDataFolder,
                IsDeployed = false
            });

            SaveProfile(profile);

            // Add project to the solution
            _solutionService.AddProjectToSolution(profileName, modelName, projectFilePath);

            MessageLogger.Info($"Model '{modelName}' added to profile '{profileName}' and included in solution.");
        }

        private string GetProjectFilePath(string profileName, string modelName, string projectFilePath)
        {
            if (string.IsNullOrEmpty(projectFilePath))
            {
                return Path.Combine(_defaultSourceDirectory, modelName, $"{modelName}.rnrproj");
            }

            return FileHelper.GetProjectFilePath(profileName, modelName, projectFilePath);
        }

        public void OpenVisualStudioSolution(string profileName)
        {
            var profile = LoadProfile(profileName);

            _solutionService.OpenSolution(profile.SolutionFilePath);
        }

        public void CheckProfile(string profileName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");
            if (!File.Exists(profilePath))
            {
                MessageLogger.Warning("Profile does not exist.");
                return;
            }

            var profile = LoadProfile(profileName);
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
            var profile = LoadProfile(profileName);

            MessageLogger.Info($"Fetch Git for profile: {profile.ProfileName}");
            foreach (var env in profile.Environments)
            {
                if (!string.IsNullOrEmpty(env.ModelRootFolder))
                {
                    GitHelper.FetchFromRemote(env.ModelName, env.ModelRootFolder);
                }    
            }
        }



        public void DeleteProfile(string profileName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");
            string solutionFilePath = _solutionService.GetSolutionFilePath(profileName);

            if (!File.Exists(profilePath))
            {
                MessageLogger.Warning($"Profile '{profileName}' does not exist.");
                return;
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);

            // Remove each project from the solution before deleting the profile
            foreach (var model in profile.Environments)
            {
                _solutionService.RemoveProjectFromSolution(profileName, model.ModelName);
            }

            File.Delete(profilePath);
            if (File.Exists(solutionFilePath))
            {
                File.Delete(solutionFilePath);
                MessageLogger.Warning($"Solution file '{solutionFilePath}' deleted.");
            }

            MessageLogger.Info($"Profile '{profileName}' and all associated models removed.");
        }


        public void RemoveModelFromProfile(string profileName, string modelName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");

            if (!File.Exists(profilePath))
            {
                MessageLogger.Warning($"Profile '{profileName}' does not exist.");
                return;
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);
            var model = profile.Environments.Find(m => m.ModelName == modelName);

            if (model == null)
            {
                MessageLogger.Warning($"Model '{modelName}' not found in profile '{profileName}'.");
                return;
            }

            profile.Environments.Remove(model);

            SaveProfile(profile);

            MessageLogger.Info($"Model '{modelName}' removed from profile '{profileName}'.");
        }

        public void ListProfiles()
        {
            if (!Directory.Exists(_appDataPath))
            {
                MessageLogger.Warning("No profiles found.");
                return;
            }

            var files = Directory.GetFiles(_appDataPath, "*.json");

            if (files.Length == 0)
            {
                MessageLogger.Warning("No profiles found.");
                return;
            }

            MessageLogger.Info("Installed Profiles:");
            foreach (var file in files)
            {
                string profileName = Path.GetFileNameWithoutExtension(file);
                MessageLogger.Info($"- {profileName}");
            }
        }

        public void ListModelsInProfile(string profileName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");

            if (!File.Exists(profilePath))
            {
                MessageLogger.Warning($"Profile '{profileName}' does not exist.");
                return;
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);

            if (profile.Environments.Count == 0)
            {
                MessageLogger.Warning($"No models found in profile '{profileName}'.");
                return;
            }

            MessageLogger.Info($"Models in Profile '{profileName}':");
            foreach (var model in profile.Environments)
            {
                string status = model.IsDeployed ? "✅ Deployed" : "❌ Not Deployed";
                string gitStatus = string.IsNullOrEmpty(model.GitUrl) ? "" : "✅ Git Repo" ; 
                MessageLogger.Info($"   - {model.ModelName}\t\t - {status} - { gitStatus }");
            }
        }

        public List<string> GetAllProfiles()
        {
            var profileNames = new List<string>();

            if (!Directory.Exists(_appDataPath))
            {
                return new List<string>();
            }

            var files = Directory.GetFiles(_appDataPath, "*.json");

            if (files.Length == 0)
            {
                return new List<string>();
            }

            foreach (var file in files)
            {
                string profileName = Path.GetFileNameWithoutExtension(file);
                profileNames.Add(profileName);

            }

            return profileNames;
        }
        private ProfileModel LoadProfile(string profileName)
        {
            string profilePath = ProfilePath(profileName);

            if (!File.Exists(profilePath))
            {
                throw new Exception($"Profile '{profileName}' does not exist");
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);

            return profile == null ? throw new Exception($"Can't load profile '{profileName}'") : profile;
        }

        private void SaveProfile(ProfileModel profile)
        {
            var profileName = profile.ProfileName;

            string profilePath = ProfilePath(profileName);

            if (!File.Exists(profilePath))
            {
                throw new Exception($"Profile '{profileName}' does not exist");
            }

            FileHelper.SaveJson(profilePath, profile);

        }

        public List<ProfileEnvironmentModel> GetModelsInProfile(string profileName)
        {
            var models = new List<ProfileEnvironmentModel>();
            var profile = LoadProfile(profileName);

            if (profile.Environments.Count == 0)
            {
                return new List<ProfileEnvironmentModel>();
            }

            
            foreach (var model in profile.Environments)
            {
                models.Add(model);
            }
            return models;
        }
    }
}
