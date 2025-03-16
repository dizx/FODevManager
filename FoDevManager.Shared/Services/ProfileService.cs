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
            _modelDeploymentService = modelDeploymentService;
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
            if (string.IsNullOrEmpty(projectFilePath))
            {
                projectFilePath = Path.Combine(_defaultSourceDirectory, modelName, $"{modelName}.rnrproj");
            }

            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");

            if (!File.Exists(profilePath))
            {
                Console.WriteLine($"Profile '{profileName}' does not exist.");
                return;
            }

            if (!Path.IsPathRooted(projectFilePath))
            {
                projectFilePath = Path.Combine(_defaultSourceDirectory, modelName, $"{modelName}.rnrproj");
            }

            if (!File.Exists(projectFilePath))
            {
                Console.WriteLine($"{projectFilePath} does not exist.");
                Console.WriteLine("Usage: fodev.exe -profile \"ProfileName\" -model \"ModelName\" add \"ProjectFilePath\"");
                return;
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);
            profile.Environments.Add(new ProfileEnvironmentModel
            {
                ModelName = modelName,
                ProjectFilePath = projectFilePath,
                IsDeployed = false
            });

            FileHelper.SaveJson(profilePath, profile);

            // Add project to the solution
            _solutionService.AddProjectToSolution(profileName, modelName, projectFilePath);

            Console.WriteLine($"Model '{modelName}' added to profile '{profileName}' and included in solution.");
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
    }
}
