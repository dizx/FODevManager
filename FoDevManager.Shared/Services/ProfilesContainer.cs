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
                var profilesByName = new Dictionary<string, ProfileModel>(StringComparer.OrdinalIgnoreCase);

                foreach (var profile in loadedProfiles)
                {
                    if (profile?.ProfileName.IsNullOrEmpty() != false)
                        continue;

                    if (profilesByName.ContainsKey(profile.ProfileName))
                    {
                        MessageLogger.Warning($"Duplicate profile name '{profile.ProfileName}' detected while refreshing profiles. Keeping the latest loaded profile.");
                    }

                    profilesByName[profile.ProfileName] = profile;
                }

                _profilesByName = profilesByName;
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
                MessageLogger.Warning($"Failed to load profile '{profileName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Flattens all model entries across every profile.</summary>
        public IEnumerable<ProfileModelEntry> AllModelEntries =>
            Profiles.SelectMany(profile => profile.AllModelEntries);

        /// <summary>Flattens all models across every profile.</summary>
        public IEnumerable<ProfileEnvironmentModel> AllModels =>
            Profiles.SelectMany(profile => profile.AllModels);

        /// <summary>
        /// Returns all models that are currently marked as deployed across all profiles.
        /// Also returns the set of profiles that contain at least one deployed model (i.e. profiles that will become dirty if those models are undeployed).
        /// </summary>
        public List<ProfileEnvironmentModel> GetDeployedModelsAcrossAllProfiles(out List<ProfileModel> dirtyProfiles)
        {
            var deployedModels = new List<ProfileEnvironmentModel>();
            var dirty = new List<ProfileModel>();

            foreach (var profile in Profiles)
            {
                // IMPORTANT: we use the cached profile instances, so any mutation of IsDeployed
                // updates the same graph that we later save.
                var deployedInProfile = profile.AllModels
                    .Where(m => m != null && m.IsDeployed)
                    .ToList();

                if (deployedInProfile.Count == 0)
                    continue;

                deployedModels.AddRange(deployedInProfile);
                dirty.Add(profile);
            }

            dirtyProfiles = dirty;
            return deployedModels;
        }

        /// <summary>Saves a set of profiles back to disk (deduped by profile name).</summary>
        public void SaveProfiles(IEnumerable<ProfileModel> profilesToSave, bool updateExternal = true)
        {
            if (profilesToSave == null)
                return;

            foreach (var profile in profilesToSave)
            {
                if (profile == null)
                    continue;

                _fileService.SaveProfile(profile, updateExternal: updateExternal);
            }
        }
    }
}
