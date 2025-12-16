using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Models.FODevManager.Models;
using FODevManager.Shared.Models;
using FODevManager.Shared.Utils;
using FODevManager.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FODevManager.Services
{
    public class ProfileService
    {
        private readonly string _defaultSourceDirectory;
        private readonly string _deploymentBasePath;
        private readonly string _profileStoragePath;
        private readonly bool _checkUncommittedBeforeSwitch;
        private readonly FileService _fileService;
        private readonly VisualStudioSolutionService _solutionService;
        private readonly ModelDeploymentService _modelDeploymentService;

        public ProfileService(AppConfig config, FileService fileService, VisualStudioSolutionService solutionService, ModelDeploymentService modelDeploymentService)
        {
            _defaultSourceDirectory = config.DefaultSourceDirectory;
            _deploymentBasePath = config.DeploymentBasePath;
            _profileStoragePath = config.ProfileStoragePath;
            _checkUncommittedBeforeSwitch = config.CheckUncommittedBeforeSwitch;
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
                EnsureRepositories(currentProfile);

                
                if (_checkUncommittedBeforeSwitch)
                {
                    foreach (var repo in currentProfile.Repositories)
                    {
                        if (!GitHelper.IsGitRepository(repo.RepoRootFolder))
                            continue;

                        if (GitHelper.HasUncommittedChanges(repo.RepoRootFolder))
                        {
                            MessageLogger.Error($"❌ Uncommitted Git changes found in repo '{repo.RepoId}'. Switch aborted.");
                            return false;
                        }
                    }
                }

                MessageLogger.Info($"🧹 Undeploying models from '{currentProfileName}'...");
                _modelDeploymentService.UnDeployAllModels(currentProfileName);
            }

            MessageLogger.Info($"📂 Switching to profile '{newProfileName}'...");

            var newProfile = _fileService.LoadProfile(newProfileName);
            EnsureRepositories(newProfile);

            SwitchBranchesInProfile(newProfile);

            UpdateDeploymentStatus(newProfileName);

            _modelDeploymentService.DeployAllUndeployedModels(newProfileName);
            ApplyDatabase(newProfileName);
            SetActiveProfile(newProfileName);
            MessageLogger.Highlight($"✅ Successfully switched to profile '{newProfileName}'.");

            return true;
        }

        private void SwitchBranchesInProfile(ProfileModel profile)
        {
            foreach (var repo in profile.Repositories)
            {
                if (!repo.AutoCheckoutOnProfileLoad)
                    continue;

                if (repo.PreferredBranch.IsNullOrEmpty())
                    continue;

                var stashMsg = $"FO Dev Manager: profile '{profile.ProfileName}' switch";

                GitHelper.ChangeBranch(
                    repo.RepoRootFolder,
                    repo.PreferredBranch!,
                    autoStashIfDirty: repo.AutoStashOnDirtyCheckout,
                    stashMessage: stashMsg
                );

                repo.LastKnownBranch = GitHelper.GetActiveBranch(repo.RepoRootFolder);
            }

            _fileService.SaveProfile(profile);

        }

        private static ModelSyncResult GetEnvironmentDiff(ProfileModel current, ProfileModel imported)
        {
            var result = new ModelSyncResult();

            var currentNames = new HashSet<string>(
                current.Models.Select(e => e.ModelName),
                StringComparer.OrdinalIgnoreCase);

            var importedNames = new HashSet<string>(
                imported.Models.Select(e => e.ModelName),
                StringComparer.OrdinalIgnoreCase);

            // Added models (in imported but not in current)
            foreach (var name in importedNames)
            {
                if (!currentNames.Contains(name))
                    result.AddAdded(name);
            }

            // Removed models (in current but not in imported)
            foreach (var name in currentNames)
            {
                if (!importedNames.Contains(name))
                    result.AddRemoved(name);
            }

            return result;
        }

        public Task<ModelSyncResult> CheckProfileModelChangesAsync(ProfileModel currentProfile)
        {
            var result = CheckProfileModelChanges(currentProfile);
            return Task.FromResult(result);
        }

        private ModelSyncResult CheckProfileModelChanges(ProfileModel currentProfile)
        {
            if (currentProfile == null)
            {
                MessageLogger.Error("CheckProfileModelChanges: currentProfile is null.");
                return new ModelSyncResult();
            }

            if (string.IsNullOrWhiteSpace(currentProfile.ProfileFilePath))
            {
                MessageLogger.Warning("CheckProfileModelChanges: ProfileFilePath is not set. Skipping model sync check.");
                return new ModelSyncResult();
            }

            var importPath = currentProfile.ProfileFilePath;

            if (!File.Exists(importPath))
            {
                MessageLogger.Warning($"CheckProfileModelChanges: Profile file not found: {importPath}");
                return new ModelSyncResult();
            }

            try
            {
                var importedProfile = FileHelper.LoadJson<ProfileModel>(importPath);
                if (importedProfile == null || string.IsNullOrWhiteSpace(importedProfile.ProfileName))
                {
                    MessageLogger.Error("CheckProfileModelChanges: Imported profile is invalid.");
                    return new ModelSyncResult();
                }

                var diff = GetEnvironmentDiff(currentProfile, importedProfile);

                if (diff.HasChanges)
                {
                    if (diff.AddedModels.Any())
                        MessageLogger.Info($"CheckProfileModelChanges: Added models: {string.Join(", ", diff.AddedModels)}");

                    if (diff.RemovedModels.Any())
                        MessageLogger.Info($"CheckProfileModelChanges: Removed models: {string.Join(", ", diff.RemovedModels)}");
                }                

                return diff;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"CheckProfileModelChanges: Failed to load or compare profiles: {ex.Message}");
                return new ModelSyncResult();
            }
        }


        public ProfileModel ImportProfile(string importPath)
        {
            if(!_fileService.ExistProfile(importPath))
            {
                throw new Exception($"Profile '{importPath}' does not exist.");
            }

            try
            { 
                var sourceProfile = _fileService.LoadProfile(importPath);

                if (sourceProfile == null || string.IsNullOrWhiteSpace(sourceProfile.ProfileName))
                {
                    MessageLogger.Error("Invalid profile file.");
                    return null!;
                }

                sourceProfile.ProfileFilePath = importPath;
                sourceProfile.IsActive = false;

                var profileDestPath = Path.Combine(_profileStoragePath, sourceProfile.ProfileName + ".json");
                if (File.Exists(profileDestPath))
                {
                    MessageLogger.Warning($"Profile '{sourceProfile.ProfileName}' already exists. It will be overwritten.");
                }

                MessageLogger.Info($"Importing profile '{sourceProfile.ProfileName}'...");

                var sourceEnvironments = new List<ProfileEnvironmentModel>();

                foreach (var environment in sourceProfile.Models)
                {
                    var modelFolderName = environment.GitUrl.IsNullOrEmpty()
                        ? environment.ModelName
                        : ExtractAzureDevOpsRepo(environment.GitUrl);

                    var modelFolder = Path.Combine(_defaultSourceDirectory, modelFolderName);
                    FileHelper.EnsureDirectoryExists(modelFolder);

                    if (!environment.GitUrl.IsNullOrEmpty())
                    {
                        if (GitHelper.IsGitRepository(modelFolder))
                        {
                            MessageLogger.Warning($"Git repo already exists at {modelFolder}. Skipping clone.");
                        }
                        else if (!GitHelper.CloneRepository(environment.GitUrl, modelFolder))
                        {
                            MessageLogger.Error($"Failed to clone repository for {environment.ModelName}.");
                            continue;
                        }
                    }

                    environment.ModelRootFolder = modelFolder;

                    if (environment.ModelType == ModelType.Source)
                    {
                        environment.ProjectFilePath = FileHelper.GetProjectFilePath(environment.ModelName, modelFolder);
                        environment.MetadataFolder = FileHelper.GetMetadataFolder(environment.ModelName, modelFolder);
                        sourceEnvironments.Add(environment);
                    }
                    else
                    {
                        environment.CompiledModelFolder = FileHelper.GetLibsFolder(environment.ModelName, modelFolder);
                    }

                    // Deployment flag
                    string deploymentLinkPath = Path.Combine(_deploymentBasePath, environment.ModelName);
                    environment.IsDeployed = Directory.Exists(deploymentLinkPath);
                }

                if(sourceProfile.SolutionFilePath.IsNullOrEmpty())
                {
                    // Decide on the solution path now (before adding projects)
                    var mainFoEnvironment = sourceProfile.Models.FirstOrDefault(e => e.IsMainFOModel && !string.IsNullOrWhiteSpace(e.ModelRootFolder));

                    if (mainFoEnvironment != null)
                    {
                        var existingVsSolution = FindExistingSolutionFile(mainFoEnvironment.ModelRootFolder!, sourceProfile.ProfileName);
                        if (!existingVsSolution.IsNullOrEmpty())
                        {
                            // Use existing solution in the main repo
                            sourceProfile.SolutionFilePath = Path.GetFullPath(existingVsSolution);
                            MessageLogger.Highlight($"Using existing solution: {sourceProfile.SolutionFilePath}");
                        }
                        else
                        {
                            sourceProfile.SolutionFilePath = _solutionService.CreateSolutionFile(sourceProfile);
                        }
                    }
                    else
                    {
                        sourceProfile.SolutionFilePath = _solutionService.CreateSolutionFile(sourceProfile);
                    }

                }

                // Add source projects 
                foreach (var env in sourceEnvironments)
                {
                    _solutionService.AddProjectToSolution(sourceProfile, env);
                }

                // Persist the imported profile
                FileHelper.SaveJson(profileDestPath, sourceProfile);
                MessageLogger.Highlight($"Profile '{sourceProfile.ProfileName}' imported successfully.");

                return sourceProfile;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to import profile: {ex.Message}");
                return null!;
            }
        }

        private static string? FindExistingSolutionFile(string repoRoot, string profileName)
        {
            try
            {
                // Only look in the root folder, not subfolders
                var slns = Directory.GetFiles(repoRoot, "*.sln", SearchOption.TopDirectoryOnly)
                                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                                    .ToArray();

                if (slns.Length == 0) return null;

                var preferred = slns.FirstOrDefault(s =>
                    string.Equals(Path.GetFileNameWithoutExtension(s),
                                  profileName,
                                  StringComparison.OrdinalIgnoreCase));

                return preferred ?? slns.First();
            }
            catch (Exception ex)
            {
                MessageLogger.Warning($"Failed to scan for existing solution in '{repoRoot}': {ex.Message}");
                return null;
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
            if (IsInstalledModel(environmentPath))
            {
                HandleInstalledModel(profileName, modelName, environmentPath);
                return;
            }

            bool anyAdded = false;

            // 1. Compiled models under Libs
            if (HasCompiledModelsInLibs(environmentPath, out var compiledFolders))
            {
                MessageLogger.Highlight("📦 Detected compiled model(s) under Libs\\. Adding...");

                foreach (var folder in compiledFolders)
                {
                    if (IsCompiledModelFolder(folder, out var detectedName))
                    {
                        AddModelToProfileIfNotExists(profileName, detectedName, folder, ModelType.Compiled);
                        anyAdded = true;
                    }
                }
             }

            // 2. Source models under Metadata
            if (HasSourceModelsInMetadata(environmentPath, out var sourceFolders))
            {
                MessageLogger.Highlight("📦 Detected source model(s) under Metadata\\. Adding...");

                foreach (var folder in sourceFolders)
                {
                    var detectedName = Path.GetFileName(folder);
                    AddModelToProfileIfNotExists(profileName, detectedName, environmentPath, ModelType.Source);
                    anyAdded = true;
                }
            }

            // 3. Direct compiled model folder
            if (!anyAdded && IsCompiledModelFolder(environmentPath, out var compiledName))
            {
                AddModelToProfileIfNotExists(profileName, compiledName, environmentPath, ModelType.Compiled);
                return;
            }

            // 4. Direct source model folder
            var parent = Directory.GetParent(environmentPath)?.Name;
            if (!anyAdded && string.Equals(parent, "Metadata", StringComparison.OrdinalIgnoreCase))
            {
                var sourceName = Path.GetFileName(environmentPath);
                AddModelToProfileIfNotExists(profileName, sourceName, Path.GetDirectoryName(Path.GetDirectoryName(environmentPath))!, ModelType.Source);
                return;
            }

            // 5. Fallback
            if (!anyAdded)
                AddModelToProfileIfNotExists(profileName, modelName, environmentPath, ModelType.Source);
        }


        private static bool IsCompiledModelFolder(string path, out string modelName)
        {
            modelName = "";
            if (!Directory.Exists(path))
                return false;

            var folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(folderName))
                return false;

            var xref = Path.Combine(path, $"{folderName}.xref");
            if (File.Exists(xref))
            {
                modelName = folderName;
                return true;
            }

            return false;
        }

        private static bool HasCompiledModelsInLibs(string envPath, out List<string> compiledFolders)
        {
            compiledFolders = new List<string>();
            var libs = Path.Combine(envPath, "Libs");
            if (!Directory.Exists(libs))
                return false;

            foreach (var dir in Directory.EnumerateDirectories(libs))
                if (IsCompiledModelFolder(dir, out _))
                    compiledFolders.Add(dir);

            return compiledFolders.Count > 0;
        }

        private static bool HasSourceModelsInMetadata(string envPath, out string[] sourceFolders)
        {
            sourceFolders = new string[] { };
            var metadataRoot = Path.Combine(envPath, "Metadata");
            if (Directory.Exists(metadataRoot))
            {
                sourceFolders = Directory.GetDirectories(metadataRoot);

                return sourceFolders.Length >= 1;
            }
            return false;
        }


        public void CreateModel(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
            
            if (_modelDeploymentService.CreateModel(modelName, profile))
            {
                AddProjectToVsSolution(profileName, modelName);
                _modelDeploymentService.CheckIfGitRepository(profileName, modelName);
            }
            
        }

        private void HandleInstalledModel(string profileName, string modelName, string environmentPath)
        {
            var profile = _fileService.LoadProfile(profileName);

            string targetFolderName = modelName.IsNullOrEmpty()
                ? Path.GetFileName(environmentPath.TrimEnd(Path.DirectorySeparatorChar))
                : modelName;

            bool success = _modelDeploymentService.ConvertInstalledModelToProjectModel(targetFolderName, profile);
            if (!success)
            {
                MessageLogger.Error($"❌ Failed to convert installed model at '{environmentPath}'.");
                return;
            }

            RegisterModelToSolution(profile, modelName);
        }

        private void RegisterModelToSolution(ProfileModel profile, string modelName)
        {
            AddProjectToVsSolution(profile.ProfileName, modelName);
            _modelDeploymentService.CheckIfGitRepository(profile.ProfileName, modelName);

            MessageLogger.Highlight($"✅ Converted model '{modelName}' registered into solution.");
        }


        private void AddModelToProfileIfNotExists(string profileName, string modelName, string environmentPath, ModelType modelType)
        {
            var projectFilePath = string.Empty;
            var metaDataFolder = string.Empty;
            var compiledModelFolder = string.Empty;
            var modelRootPath = FileHelper.GetModelRootFolder(environmentPath);
            if (!Directory.Exists(modelRootPath))
            {
                MessageLogger.Error($"❌ Error: Model root folder not found at {modelRootPath}.");
                return;
            }

            if (modelType == ModelType.Source)
            {
                if (modelName.IsNullOrEmpty())
                {
                    modelName = DetectModelNameFromMetadata(modelRootPath);
                    if (modelName.IsNullOrEmpty())
                    {
                        throw new Exception("❌ Unable to find model name from Metadata folder.");
                    }
                }

                projectFilePath = GetProjectFilePath(modelName, modelRootPath);
                if (!File.Exists(projectFilePath))
                {
                    MessageLogger.Info($"{projectFilePath} does not exist.");
                    if (Singleton<Engine>.Instance.EnvironmentType == EnvironmentType.Console)
                        MessageLogger.Info("Usage: fodev.exe -profile \"ProfileName\" -model \"ModelName\" add \"ProjectFilePath\"");
                    return;
                }

                metaDataFolder = FileHelper.GetMetadataFolder(modelName, modelRootPath);
                if (!Directory.Exists(metaDataFolder))
                {
                    MessageLogger.Error($"❌ Error: Metadata folder not found at {metaDataFolder}.");
                    return;
                }
            }
            else
            {
                compiledModelFolder = environmentPath;
                projectFilePath = string.Empty;
                metaDataFolder = string.Empty;
            }

            var profile = _fileService.LoadProfile(profileName);

            if (profile.Models.Any(e => e.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageLogger.Warning($"⚠️ Model '{modelName}' is already in the profile '{profileName}'. Skipping add.");
                return;
            }

            string deploymentLinkPath = Path.Combine(_deploymentBasePath, modelName);
            bool isAlreadyDeployed = Directory.Exists(deploymentLinkPath);

            profile.Models.Add(new ProfileEnvironmentModel
            {
                ModelName = modelName,
                ModelRootFolder = modelRootPath,
                ProjectFilePath = projectFilePath,
                MetadataFolder = metaDataFolder,
                CompiledModelFolder = compiledModelFolder,
                IsDeployed = isAlreadyDeployed,
                ModelType = modelType

            });

            _fileService.SaveProfile(profile, updateExternal: true);
            _modelDeploymentService.CheckIfGitRepository(profileName, modelName);

            if (modelType == ModelType.Source)
            {
                AddProjectToVsSolution(profileName, modelName);
            }
            else
            {
                MessageLogger.Info($"✅ Compiled Model '{modelName}' added to profile");
            }
        }

        private void AddProjectToVsSolution(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
            var newModel = profile.Models.Find(e => e.ModelName == modelName);
            if (newModel == null)
            {
                MessageLogger.Error($"❌ Error: Model '{modelName}' not found in profile after creation.");
                return;
            }
            _solutionService.AddProjectToSolution(profile, newModel);

            MessageLogger.Info($"✅ Model '{modelName}' added to profile '{profileName}' and included in solution.");

        }

        private bool IsInstalledModel(string path) => path.StartsWith(_deploymentBasePath, StringComparison.OrdinalIgnoreCase);

        private string DetectModelNameFromMetadata(string modelRootPath)
        {
            string metadataPath = Path.Combine(modelRootPath, "Metadata");

            if (!Directory.Exists(metadataPath))
                metadataPath = Path.Combine(Path.GetDirectoryName(modelRootPath), "Metadata");

            if (Directory.Exists(metadataPath))
            {
                var subfolder = Directory.GetDirectories(metadataPath).FirstOrDefault();
                if (!subfolder.IsNullOrEmpty())
                    return Path.GetFileName(subfolder);
            }

            return string.Empty;
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
            foreach (var env in profile.Models)
            {
                _modelDeploymentService.CheckModelDeployment(profileName, env.ModelName);
                _modelDeploymentService.CheckIfGitRepository(profileName, env.ModelName);
            }
        }
        public void GitFetchLatest(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);

            MessageLogger.Info($"Fetch Git for profile: {profile.ProfileName}");
            foreach (var env in profile.Models)
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
            
            var profile = _fileService.LoadProfile(profileName);

            // Remove each project from the solution before deleting the profile
            foreach (var model in profile.Models)
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
            var model = profile.Models.Find(m => m.ModelName == modelName);

            if (model == null)
            {
                MessageLogger.Warning($"Model '{modelName}' not found in profile '{profileName}'.");
                return;
            }

            _solutionService.RemoveProjectFromSolution(profileName, model.ModelName);

            profile.Models.Remove(model);

            _fileService.SaveProfile(profile, updateExternal: true);

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

            if (profile.Models.Count == 0)
            {
                MessageLogger.Warning($"No models found in profile '{profileName}'.");
                return;
            }

            MessageLogger.Info($"Models in Profile '{profileName}':");
            foreach (var model in profile.Models)
            {
                string status = model.IsDeployed ? "✅ Deployed" : "❌ Not Deployed";
                string gitStatus = model.GitUrl.IsNullOrEmpty() ? "" : "✅ Git Repo" ; 
                MessageLogger.Info($"   - {model.ModelName}\t\t - {status} - { gitStatus }");
            }
        }

        public ProfileModel? GetActiveProfile()
        {
            var profiles = _fileService.GetAllProfiles();

            var activeProfile = profiles.FirstOrDefault(p => p.IsActive);

            if (activeProfile == null)
            {
                MessageLogger.Warning("⚠️ No active profile found.");
                return null;
            }

            return activeProfile;
        }

        public List<ProfileEnvironmentModel> GetModelsInProfile(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);
            if (profile.Models.Count == 0)
            {
                return new List<ProfileEnvironmentModel>();
            }
            return profile.Models;
        }

        public ProfileEnvironmentModel GetModel(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
            if (profile.Models.Count == 0)
            {
                return new ProfileEnvironmentModel();
            }
            return profile.Models.FirstOrDefault(x => x.ModelName == modelName);
        }

        public void UpdateDeploymentStatus(string profileName)
        {
            bool updated = false;
            var profile = _fileService.LoadProfile(profileName);

            foreach (var model in profile.Models)
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

        private bool EnsureRepositories(ProfileModel profile)
        {
            if (profile.Repositories != null && profile.Repositories.Count > 0)
                return false;

            if (profile.Models == null || profile.Models.Count == 0)
                return false;

            var repoMap = new Dictionary<string, RepositoryModel>(StringComparer.OrdinalIgnoreCase);

            foreach (var m in profile.Models)
            {
                // Your current ModelRootFolder is already the repo root folder in most flows
                // (it’s used for git checks and cloning). :contentReference[oaicite:2]{index=2}
                var repoRoot = (m.ModelRootFolder ?? "").Trim();
                if (repoRoot.IsNullOrEmpty())
                    continue;

                // Key: prefer GitUrl if present, else folder path
                var key = !m.GitUrl.IsNullOrEmpty() ? m.GitUrl : repoRoot;

                if (!repoMap.TryGetValue(key, out var repo))
                {
                    repo = new RepositoryModel
                    {
                        RepoId = SlugRepoId(key),
                        RepoRootFolder = repoRoot,
                        GitUrl = m.GitUrl.IsNullOrEmpty() ? null : m.GitUrl
                    };
                    repoMap[key] = repo;
                }

                repo.Models.Add(m);
            }

            profile.Repositories = repoMap.Values
                .OrderBy(r => r.RepoId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Optional: keep Environments for backwards compat, or clear it once UI is updated.
            // profile.Environments = new();

            MessageLogger.Info($"📦 Repositories built: {profile.Repositories.Count}");
            return true;
        }

        private static string SlugRepoId(string input)
        {
            if (input.IsNullOrEmpty()) return Guid.NewGuid().ToString("N");

            // Simple stable-ish id: folder name or repo name tail
            var tail = input.Replace('\\', '/').TrimEnd('/');
            tail = tail.Contains("/") ? tail.Split('/').Last() : tail;
            tail = tail.Replace(".git", "", StringComparison.OrdinalIgnoreCase);
            return tail.IsNullOrEmpty() ? Guid.NewGuid().ToString("N") : tail;
        }

    }
}
