using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Utils;

namespace FODevManager.Services
{
    public class ProfileService
    {
        private readonly string _appDataPath;
        private readonly string _defaultSourceDirectory;
        private readonly VisualStudioSolutionService _solutionService;

        public ProfileService() 
        {
            var config = FileHelper.LoadJson<dynamic>("appsettings.json");
            _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FODevManager");
            _defaultSourceDirectory = Environment.ExpandEnvironmentVariables(config["DefaultSourceDirectory"]);
            _solutionService = new VisualStudioSolutionService(_defaultSourceDirectory);

            FileHelper.EnsureDirectoryExists(_appDataPath);
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);
        }

        public void CreateProfile(string profileName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");

            if (File.Exists(profilePath))
            {
                Console.WriteLine("Profile already exists.");
                return;
            }

            var profile = new ProfileModel { ProfileName = profileName };
            FileHelper.SaveJson(profilePath, profile);

            // Create a solution file
            _solutionService.CreateSolutionFile(profileName);

            Console.WriteLine($"Profile '{profileName}' and solution file '{profileName}.sln' created.");
        }

        public void AddEnvironment(string profileName, string modelName, string projectFilePath)
        {
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

            Console.WriteLine($"Profile: {profile.ProfileName}");
            foreach (var env in profile.Environments)
            {
                Console.WriteLine($"- Model: {env.ModelName}, Deployed: {env.IsDeployed}, Path Exists: {File.Exists(env.ProjectFilePath)}");
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
                Console.WriteLine($"- {model.ModelName} (Deployed: {model.IsDeployed})");
            }
        }


    }
}
