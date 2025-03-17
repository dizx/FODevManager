using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Utils;
using Microsoft.Extensions.Configuration;

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
            //_modelDeploymentService = modelDeploymentService;
            FileHelper.EnsureDirectoryExists(_appDataPath);
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);
        }

        public void CreateProfile(string profileName)
        {
            string profilePath = ProfilePath(profileName);

            if (File.Exists(profilePath))
            {
                Console.WriteLine("Profile already exists.");
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
            Console.WriteLine($"✅ Profile '{profileName}' created with solution file: {solutionFilePath}");
        }

        public void AddEnvironment(string profileName, string modelName, string projectFilePath)
        {
            projectFilePath = GetProjectFilePath(profileName, modelName, projectFilePath);

            if (!File.Exists(projectFilePath))
            {
                Console.WriteLine($"{projectFilePath} does not exist.");
                Console.WriteLine("Usage: fodev.exe -profile \"ProfileName\" -model \"ModelName\" add \"ProjectFilePath\"");
                return;
            }

            var metaDataFolder = GetMetadataFolder(modelName, projectFilePath);
            
            if (!Directory.Exists(metaDataFolder))
            {
                Console.WriteLine($"❌ Error: Metadata folder not found at {metaDataFolder}.");
                return;
            }

            var profile = LoadProfileFile(profileName);
            
            profile.Environments.Add(new ProfileEnvironmentModel
            {
                ModelName = modelName,
                ProjectFilePath = projectFilePath,
                MetadataFolder = metaDataFolder,
                IsDeployed = false
            });

            SaveProfileFile(profile);

            // Add project to the solution
            _solutionService.AddProjectToSolution(profileName, modelName, projectFilePath);

            Console.WriteLine($"Model '{modelName}' added to profile '{profileName}' and included in solution.");
        }

        private string GetProjectFilePath(string profileName, string modelName, string projectFilePath)
        {
            if (string.IsNullOrEmpty(projectFilePath))
            {
                return Path.Combine(_defaultSourceDirectory, modelName, $"{modelName}.rnrproj");
            }

            if (!Path.IsPathRooted(projectFilePath))
            {
                return Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj");
            }

            if (!Path.HasExtension(projectFilePath))
            {
                if(PathContainsDirectory(projectFilePath, "project") && PathContainsDirectory(projectFilePath, modelName))
                {
                    return projectFilePath = Path.Combine(projectFilePath, $"{modelName}.rnrproj");
                }
                if (PathContainsDirectory(projectFilePath, "project") && !PathContainsDirectory(projectFilePath, modelName))
                {
                    if(File.Exists(Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj")))
                        return projectFilePath = Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj");
                }
                if (File.Exists(Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj")))
                    return projectFilePath = Path.Combine(projectFilePath, modelName, $"{modelName}.rnrproj");

                if (File.Exists(Path.Combine(projectFilePath, "project", modelName, $"{modelName}.rnrproj")))
                    return Path.Combine(projectFilePath, "project", modelName, $"{modelName}.rnrproj");

            }
            return Path.Combine(projectFilePath, "project", modelName, modelName, $"{modelName}.rnrproj");
        }

        static bool PathContainsDirectory(string path, string directoryName)
        {
            string fullPath = Path.GetFullPath(path); // Normalize path
            string normalizedDir = Path.DirectorySeparatorChar + directoryName + Path.DirectorySeparatorChar;

            return fullPath.Contains(normalizedDir, StringComparison.OrdinalIgnoreCase);
        }

        private string GetMetadataFolder(string modelName, string projectFilePath)
        {
            string currentPath = Path.GetDirectoryName(projectFilePath);

            // Move up to 3 levels and check for Metadata folder
            for (int i = 0; i < 3; i++)
            {
                if (string.IsNullOrEmpty(currentPath)) break;

                string metadataPath = Path.Combine(currentPath, "Metadata", modelName);
                if (Directory.Exists(metadataPath))
                {
                    return metadataPath;
                }

                metadataPath = Path.Combine(currentPath, "Metadata");
                if (Directory.Exists(metadataPath))
                {
                    return Path.Combine(metadataPath, modelName);
                }

                // Move one level up
                currentPath = Directory.GetParent(currentPath)?.FullName;
            }

            // Default: return the highest-level Metadata folder if not found
            return Path.Combine(currentPath ?? projectFilePath, "Metadata", modelName);
        }

        static string GetParentDirectory(string path)
        {
            DirectoryInfo parentDir = Directory.GetParent(Path.GetDirectoryName(path));
            return parentDir?.FullName + Path.DirectorySeparatorChar;
        }

        public void CheckProfile(string profileName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");
            if (!File.Exists(profilePath))
            {
                Console.WriteLine("Profile does not exist.");
                return;
            }

            var profile = JsonSerializer.Deserialize<ProfileModel>(File.ReadAllText(profilePath));
            if(profile == null)
            {
                throw new Exception($"Profile '{profileName}' is empty");
            }

            Console.WriteLine($"Profile: {profile.ProfileName}");
            foreach (var env in profile.Environments)
            {
                _modelDeploymentService.CheckModelDeployment(profileName, env.ModelName);
            }
        }

        public void DeleteProfile(string profileName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");
            string solutionFilePath = _solutionService.GetSolutionFilePath(profileName);

            if (!File.Exists(profilePath))
            {
                Console.WriteLine($"Profile '{profileName}' does not exist.");
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
                Console.WriteLine($"Solution file '{solutionFilePath}' deleted.");
            }

            Console.WriteLine($"Profile '{profileName}' and all associated models removed.");
        }


        public void RemoveModelFromProfile(string profileName, string modelName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");

            if (!File.Exists(profilePath))
            {
                Console.WriteLine($"Profile '{profileName}' does not exist.");
                return;
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);
            var model = profile.Environments.Find(m => m.ModelName == modelName);

            if (model == null)
            {
                Console.WriteLine($"Model '{modelName}' not found in profile '{profileName}'.");
                return;
            }

            profile.Environments.Remove(model);
            FileHelper.SaveJson(profilePath, profile);

            Console.WriteLine($"Model '{modelName}' removed from profile '{profileName}'.");
        }

        public void ListProfiles()
        {
            if (!Directory.Exists(_appDataPath))
            {
                Console.WriteLine("No profiles found.");
                return;
            }

            var files = Directory.GetFiles(_appDataPath, "*.json");

            if (files.Length == 0)
            {
                Console.WriteLine("No profiles found.");
                return;
            }

            Console.WriteLine("Installed Profiles:");
            foreach (var file in files)
            {
                string profileName = Path.GetFileNameWithoutExtension(file);
                Console.WriteLine($"- {profileName}");
            }
        }

        public void ListModelsInProfile(string profileName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");

            if (!File.Exists(profilePath))
            {
                Console.WriteLine($"Profile '{profileName}' does not exist.");
                return;
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);

            if (profile.Environments.Count == 0)
            {
                Console.WriteLine($"No models found in profile '{profileName}'.");
                return;
            }

            Console.WriteLine($"Models in Profile '{profileName}':");
            foreach (var model in profile.Environments)
            {
                string status = model.IsDeployed ? "✅ Deployed" : "❌ Not Deployed";
                Console.WriteLine($"   - {model.ModelName} ({status})");
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

        public List<ProfileEnvironmentModel> GetModelsInProfile(string profileName)
        {
            var models = new List<ProfileEnvironmentModel>();
            var profile = LoadProfileFile(profileName);

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
