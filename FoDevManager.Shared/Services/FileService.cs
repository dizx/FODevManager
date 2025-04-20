using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Services;
using FODevManager.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.Services
{
    public class FileService
    {
        private readonly string _appDataPath;
        public FileService(AppConfig config)
        {
            _appDataPath = config.ProfileStoragePath;
            FileHelper.EnsureDirectoryExists(_appDataPath);
        }

        private string ProfilePath(string profileName) => Path.Combine(_appDataPath, $"{profileName}.json");

        public ProfileModel LoadProfile(string profileName)
        {
            string profilePath = ProfilePath(profileName);

            if (!File.Exists(profilePath))
            {
                throw new Exception($"Profile '{profileName}' does not exist");
            }

            var profile = FileHelper.LoadJson<ProfileModel>(profilePath);

            return profile == null ? throw new Exception($"Can't load profile '{profileName}'") : profile;
        }

        public List<ProfileModel> GetAllProfiles()
        {
            var profiles = new List<ProfileModel>();

            if (!Directory.Exists(_appDataPath))
                return profiles;

            var files = Directory.GetFiles(_appDataPath, "*.json");

            foreach (var fileName in files)
            {
                try
                {
                    profiles.Add(LoadProfile(Path.GetFileNameWithoutExtension(fileName)));
                }
                catch (Exception ex)
                {
                    MessageLogger.Warning($"⚠️ Failed to load profile from '{fileName}': {ex.Message}");
                }
            }

            return profiles;
        }


        public List<string> GetAllProfileNames()
        {
            var profileNames = new List<string>();

            if (!Directory.Exists(_appDataPath))
                return profileNames;

            var files = Directory.GetFiles(_appDataPath, "*.json");

            foreach (var fileName in files)
            {
                try
                {
                    var profile = LoadProfile(Path.GetFileNameWithoutExtension(fileName));
                    if (profile != null && !string.IsNullOrEmpty(profile.ProfileName))
                    {
                        profileNames.Add(profile.ProfileName);
                    }
                }
                catch (Exception ex)
                {
                    MessageLogger.Warning($"⚠️ Failed to load profile from '{fileName}': {ex.Message}");
                }
            }

            return profileNames;
        }

        public void SaveProfile(ProfileModel profile, bool skipExistCheck = false)
        {
            var profileName = profile.ProfileName;

            string profilePath = ProfilePath(profileName);

            if (!skipExistCheck && !File.Exists(profilePath))
            {
                throw new Exception($"Profile '{profileName}' does not exist");
            }

            FileHelper.SaveJson(profilePath, profile);

        }

        public bool ExistProfile(string profileName)
        {
            string profilePath = ProfilePath(profileName);

            return File.Exists(profilePath);
        }

        public void DeleteProfile(string profileName)
        {
            string profilePath = Path.Combine(_appDataPath, $"{profileName}.json");

            if (!File.Exists(profilePath))
            {
                MessageLogger.Warning($"Profile '{profileName}' does not exist.");
                return;
            }

            File.Delete(profilePath);
        }
    }
}
