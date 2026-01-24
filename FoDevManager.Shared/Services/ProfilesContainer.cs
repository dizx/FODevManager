using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Shared.Models;
using FODevManager.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FODevManager.Services
{
    public sealed class ProfilesContainer
    {
        private readonly FileService _fileService;
        private readonly object _syncRoot = new();
        private Dictionary<string, ProfileModel> _profilesByName = new(StringComparer.OrdinalIgnoreCase);

        public ProfilesContainer(FileService fileService)
        {
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            Refresh();
        }

        /// <summary>Latest in-memory snapshot of all profiles.</summary>
        public IReadOnlyList<ProfileModel> Profiles
        {
            get
            {
                lock (_syncRoot)
                {
                    return _profilesByName.Values.ToList();
                }
            }
        }

        /// <summary>Reloads all profiles from disk into memory.</summary>
        public void Refresh()
        {
            var loadedProfiles = _fileService.GetAllProfiles();

            lock (_syncRoot)
            {
                _profilesByName = loadedProfiles
                    .Where(p => !p.ProfileName.IsNullOrEmpty())
                    .ToDictionary(p => p.ProfileName, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>Gets a profile from cache; optionally reloads from disk when missing.</summary>
        public ProfileModel? TryGet(string profileName, bool reloadIfMissing = false)
        {
            if (profileName.IsNullOrEmpty())
                return null;

            lock (_syncRoot)
            {
                if (_profilesByName.TryGetValue(profileName, out var cached))
                    return cached;
            }

            if (!reloadIfMissing)
                return null;

            try
            {
                var loaded = _fileService.LoadProfile(profileName);
                lock (_syncRoot)
                {
                    _profilesByName[profileName] = loaded;
                }
                return loaded;
            }
            catch (Exception ex)
            {
                MessageLogger.Warning($"⚠️ Failed to load profile '{profileName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Flattens all model entries across every profile.</summary>
        public IEnumerable<ProfileModelEntry> AllModelEntries =>
            Profiles.SelectMany(profile => profile.AllModelEntries);

        /// <summary>Flattens all models across every profile.</summary>
        public IEnumerable<ProfileEnvironmentModel> AllModels =>
            Profiles.SelectMany(profile => profile.AllModels);
    }
}