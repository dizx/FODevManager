using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Shared.Models;
using FODevManager.Shared.Utils;
using FODevManager.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
                
                if(EnsureRepositories(currentProfile))
                    _fileService.SaveProfile(currentProfile, updateExternal: true);

                if (_checkUncommittedBeforeSwitch)
                {
                    foreach (var repo in currentProfile.Repositories)
                    {
                        if (!GitHelper.IsGitRepository(repo.RepoRootFolder))
                            continue;

                        if (GitHelper.HasUncommittedChanges(repo.RepoRootFolder))
                        {
                            MessageLogger.Error($"❌ Uncommitted Git changes found in repo '{repo.DisplayName}'. Switch aborted.");
                            return false;
                        }
                    }
                }

                MessageLogger.Info($"🧹 Undeploying models from '{currentProfileName}'...");
                _modelDeploymentService.UnDeployAllModels(currentProfileName);
            }

            MessageLogger.Info($"📂 Switching to profile '{newProfileName}'...");

            var newProfile = _fileService.LoadProfile(newProfileName);
            
            if (EnsureRepositories(newProfile))
                _fileService.SaveProfile(newProfile, updateExternal: true);

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
                    stashMessage: stashMsg,
                    createIfMissing: true
                );

                repo.LastKnownBranch = GitHelper.GetActiveBranch(repo.RepoRootFolder);
            }

            _fileService.SaveProfile(profile);

        }

        private static ModelSyncResult GetEnvironmentDiff(ProfileModel current, ProfileModel imported)
        {
            var result = new ModelSyncResult();

            var currentNames = new HashSet<string>(
                current.AllModels.Select(e => e.ModelName),
                StringComparer.OrdinalIgnoreCase);

            var importedNames = new HashSet<string>(
                imported.AllModels.Select(e => e.ModelName),
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

            if (currentProfile.ProfileFilePath.IsNullOrEmpty())
            {
                MessageLogger.Warning("CheckProfileModelChanges: ProfileFilePath is not set. Attempting first-time export to repo Artifacts...");

                if (!TryCreateExternalProfileExport(currentProfile, out var bootstrappedPath))
                {
                    MessageLogger.Warning("CheckProfileModelChanges: Create profile export failed. Skipping model sync check.");
                    return new ModelSyncResult();
                }

                MessageLogger.Info($"CheckProfileModelChanges: Profile export OK. Using '{bootstrappedPath}'.");
            }

            var importPath = currentProfile.ProfileFilePath;

            if (!File.Exists(importPath))
            {
                MessageLogger.Warning($"CheckProfileModelChanges: Profile file not found: {importPath}");
                return new ModelSyncResult();
            }

            try
            {
                if (!TryLoadExternalProfile(importPath, out var importedProfile, out var isLegacy))
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

                //TODO: This code can be removed in the future when we are sure legacy profiles are no longer in use
                if (diff.IsLegacy && !diff.HasChanges)
                {
                    _fileService.SaveProfile(currentProfile, updateExternal: true);
                }

                return diff;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"CheckProfileModelChanges: Failed to load or compare profiles: {ex.Message}");
                return new ModelSyncResult();
            }
        }

        private bool TryCreateExternalProfileExport(ProfileModel currentProfile, out string exportedProfilePath)
        {
            exportedProfilePath = string.Empty;

            if (currentProfile == null)
                throw new ArgumentNullException(nameof(currentProfile));

            // Ensure repo map exists so TryGetRepoRootFolder can resolve cleanly
            EnsureRepositories(currentProfile);

            var mainFoModel = currentProfile
                .AllModels
                .FirstOrDefault(model =>
                    model.IsMainFOModel &&
                    !model.ModelRootFolder.IsNullOrEmpty());

            if (mainFoModel == null)
            {
                MessageLogger.Warning("Profile export: No Main FO model found. Cannot export external profile.");
                return false;
            }

            var repoRootFolder = currentProfile.TryGetRepoRootFolder(mainFoModel) ?? mainFoModel.ModelRootFolder;
            if (repoRootFolder.IsNullOrEmpty() || !Directory.Exists(repoRootFolder))
            {
                MessageLogger.Error($"Profile export: Repo root folder not found: '{repoRootFolder}'.");
                return false;
            }

            var artifactsFolder = Path.Combine(repoRootFolder, "Artifacts");
            FileHelper.EnsureDirectoryExists(artifactsFolder);

            exportedProfilePath = Path.Combine(artifactsFolder, $"{currentProfile.ProfileName}.json");

            // Set ProfileFilePath so the rest of the system uses it, then export
            currentProfile.ProfileFilePath = exportedProfilePath;

            // Persist local + write external export
            _fileService.SaveProfile(currentProfile, updateExternal: true);

            if (!File.Exists(exportedProfilePath))
            {
                MessageLogger.Error($"Profile export: Failed to export profile to '{exportedProfilePath}'.");
                return false;
            }

            MessageLogger.Highlight($"✅ Profile export: Exported external profile to '{exportedProfilePath}'.");
            return true;
        }

        public ProfileModel ImportProfile(string importPath)
        {
            if (!File.Exists(importPath))
            {
                MessageLogger.Error($"Profile file not found: {importPath}");
                return null!;
            }

            try
            {
                if (!TryLoadExternalProfile(importPath, out var sourceProfile, out var isLegacy))
                {
                    MessageLogger.Error("Invalid profile file.");
                    return null!;
                }

                if (isLegacy)
                {
                    EnsureRepositories(sourceProfile);
                }

                sourceProfile.ProfileFilePath = importPath;
                sourceProfile.IsActive = false;

                var profileDestPath = Path.Combine(_profileStoragePath, sourceProfile.ProfileName + ".json");
                if (File.Exists(profileDestPath))
                {
                    MessageLogger.Warning($"Profile '{sourceProfile.ProfileName}' already exists. It will be overwritten.");
                }

                MessageLogger.Info($"Importing profile '{sourceProfile.ProfileName}'...");

                // 1) Import repositories (clone once per repo, configure models)
                foreach (var repository in sourceProfile.Repositories ?? new List<RepositoryModel>())
                {
                    ImportRepository(repository);
                }

                // 2) Import standalone models (non-repo)
                foreach (var standaloneModel in sourceProfile.StandaloneModels ?? new List<ProfileEnvironmentModel>())
                {
                    ImportStandaloneModel(standaloneModel);
                }

                ResolveSolutionFilePathIfRelative(sourceProfile);

                // 3) Ensure SolutionFilePath (prefer main FO repo solution if present)
                if (sourceProfile.SolutionFilePath.IsNullOrEmpty())
                {
                    var mainFoModel = sourceProfile.AllModels
                        .FirstOrDefault(model => model.IsMainFOModel && !model.ModelRootFolder.IsNullOrEmpty());

                    if (mainFoModel != null)
                    {
                        var existingVsSolution = FindExistingSolutionFile(mainFoModel.ModelRootFolder!, sourceProfile.ProfileName);
                        if (!existingVsSolution.IsNullOrEmpty())
                        {
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

                // 4) Add ALL source models to the solution (no extra list)
                foreach (var model in sourceProfile.AllModels)
                {
                    if (model.ModelType == ModelType.Source)
                    {
                        _solutionService.AddProjectToSolution(sourceProfile, model);
                    }
                }

                // 5) Persist imported profile
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

        private void ImportRepository(RepositoryModel repository)
        {
            if (repository == null)
                return;

            if (repository.GitUrl.IsNullOrEmpty())
            {
                MessageLogger.Warning($"Repo '{repository.DisplayName}' has no GitUrl. Skipping clone.");
                return;
            }

            var repoFolderName = GitHelper.ExtractAzureDevOpsRepo(repository.GitUrl);
            if (repoFolderName.IsNullOrEmpty())
                repoFolderName = repository.RepoRootFolder;

            var repoRootFolder = Path.Combine(_defaultSourceDirectory, repoFolderName);
            FileHelper.EnsureDirectoryExists(repoRootFolder);

            if (GitHelper.IsGitRepository(repoRootFolder))
            {
                MessageLogger.Info($"✅ Repo already exists at {repoRootFolder}. Skipping clone.");
            }
            else
            {
                if (!GitHelper.CloneRepository(repository.GitUrl, repoRootFolder))
                {
                    MessageLogger.Error($"Failed to clone repository '{repository.DisplayName}'.");
                    return;
                }
            }

            repository.RepoRootFolder = repoRootFolder;

            foreach (var model in repository.Models ?? new List<ProfileEnvironmentModel>())
            {
                // Backwards-compat: model points to repo root
                model.ModelRootFolder = repoRootFolder;

                ConfigureModelPathsAndDeployment(model, repoRootFolder);
            }
        }

        private void ImportStandaloneModel(ProfileEnvironmentModel model)
        {
            if (model == null)
                return;

            var modelRootFolder = Path.Combine(_defaultSourceDirectory, model.ModelName);
            FileHelper.EnsureDirectoryExists(modelRootFolder);

            model.ModelRootFolder = modelRootFolder;

            ConfigureModelPathsAndDeployment(model, modelRootFolder);
        }

        public ProfileModel ImportProfileFromRepoUrl(string repoUrl)
        {
            if (repoUrl.IsNullOrEmpty())
            {
                MessageLogger.Error("ImportProfileFromRepoUrl: repoUrl is empty.");
                return null!;
            }

            var repoFolderName = GitHelper.ExtractAzureDevOpsRepo(repoUrl);
            if (repoFolderName.IsNullOrEmpty())
            {
                MessageLogger.Error($"ImportProfileFromRepoUrl: Could not derive folder name from URL: {repoUrl}");
                return null!;
            }

            var targetRepoRoot = Path.Combine(_defaultSourceDirectory, repoFolderName);
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);

            if (GitHelper.IsGitRepository(targetRepoRoot))
            {
                MessageLogger.Warning($"Repo already exists at '{targetRepoRoot}'. Skipping clone.");
            }
            else
            {
                if (Directory.Exists(targetRepoRoot) && Directory.EnumerateFileSystemEntries(targetRepoRoot).Any())
                {
                    MessageLogger.Error($"Target folder exists and is not empty (and not a git repo): {targetRepoRoot}");
                    return null!;
                }

                if (!GitHelper.CloneRepository(repoUrl, targetRepoRoot))
                {
                    MessageLogger.Error($"Failed to clone repository: {repoUrl}");
                    return null!;
                }
            }

            var profileJsonPath = FindProfileJsonInArtifacts(targetRepoRoot);
            if (profileJsonPath.IsNullOrEmpty())
            {
                MessageLogger.Error($"No profile json found under Artifact/Artifacts in repo: {targetRepoRoot}");
                return null!;
            }

            MessageLogger.Highlight($"📦 Importing profile from: {profileJsonPath}");
            return ImportProfile(profileJsonPath);
        }

        private static string? FindProfileJsonInArtifacts(string repoRootFolder)
        {
            if (!Directory.Exists(repoRootFolder))
                return null;

            var artifactsFolder = Directory.GetDirectories(repoRootFolder, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(dir =>
                    Path.GetFileName(dir).SameAs("Artifact") ||
                    Path.GetFileName(dir).SameAs("Artifacts"));

            if (artifactsFolder == null)
                return null;

            var jsonFiles = Directory.GetFiles(artifactsFolder, "*.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (jsonFiles.Count == 0)
                return null;

            // Prefer a file that looks like a profile export if there are multiple
            var preferred = jsonFiles.FirstOrDefault();

            return preferred ?? jsonFiles[0];
        }

        private string ResolveSolutionBaseFolder(ProfileModel profile)
        {
            var mainFoModel = profile.AllModels.FirstOrDefault(model => model.IsMainFOModel);

            if (mainFoModel != null)
            {
                var repoRoot = profile.TryGetRepoRootFolder(mainFoModel);
                if (!repoRoot.IsNullOrEmpty())
                    return repoRoot!;
            }

            return Path.Combine(_defaultSourceDirectory, profile.ProfileName);
        }

        private void ResolveSolutionFilePathIfRelative(ProfileModel profile)
        {
            if (profile.SolutionFilePath.IsNullOrEmpty())
                return;

            if (Path.IsPathRooted(profile.SolutionFilePath))
                return;

            var baseFolder = ResolveSolutionBaseFolder(profile);

            var relative = profile.SolutionFilePath.Replace('/', Path.DirectorySeparatorChar);
            profile.SolutionFilePath = Path.GetFullPath(Path.Combine(baseFolder, relative));
        }

        private void ConfigureModelPathsAndDeployment(ProfileEnvironmentModel model, string modelRootFolder)
        {
            if (model.ModelType == ModelType.Source)
            {
                model.ProjectFilePath = FileHelper.GetProjectFilePath(model.ModelName, modelRootFolder);
                model.MetadataFolder = FileHelper.GetMetadataFolder(model.ModelName, modelRootFolder);
            }
            else
            {
                model.CompiledModelFolder = FileHelper.GetLibsFolder(model.ModelName, modelRootFolder);
            }

            var deploymentLinkPath = Path.Combine(_deploymentBasePath, model.ModelName);
            model.IsDeployed = Directory.Exists(deploymentLinkPath);
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

                var preferred = slns.FirstOrDefault(s => Path.GetFileNameWithoutExtension(s).SameAs(profileName));

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
            _fileService.SaveProfile(profile, updateExternal: true);
            
            if (profile.IsActive)
            {
                ApplyDatabase(profileName);
            }
            

            MessageLogger.Info($"✅ Database name '{dbName}' set for profile '{profileName}'.");
        }

        public void SetActiveProfile(string profileName)
        {
            foreach (var profile in _fileService.GetAllProfiles())
            {
                if (profile is null) continue;

                profile.IsActive = profile.ProfileName.SameAs(profileName);
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
                profile.DatabaseName = "AXDB";
                _fileService.SaveProfile(profile, updateExternal: true);
            }
            
            var currentDb = WebConfigHelper.GetCurrentDatabaseName();

            if (currentDb.SameAs(profile.DatabaseName))
            {
                MessageLogger.Info($"ℹ️ Database is already set to '{currentDb}'. No change needed.");
                return;
            }

            try
            {
                
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

        public void AddModel(string profileName, string modelName, string environmentPath)
        {
            var isGit = GitHelper.IsGitRepository(environmentPath);

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
                        _modelDeploymentService.AddModelToProfileIfNotExists(profileName, detectedName, folder, ModelType.Compiled);
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
                    _modelDeploymentService.AddModelToProfileIfNotExists(profileName, detectedName, environmentPath, ModelType.Source);
                    AddProjectToVsSolution(profileName, detectedName);
                    anyAdded = true;
                }
            }

            // 3. Direct compiled model folder
            if (!anyAdded && IsCompiledModelFolder(environmentPath, out var compiledName))
            {
                _modelDeploymentService.AddModelToProfileIfNotExists(profileName, compiledName, environmentPath, ModelType.Compiled);
                return;
            }

            // 4. Direct source model folder
            var parent = Directory.GetParent(environmentPath)?.Name;
            if (!anyAdded && parent.SameAs("Metadata"))
            {
                var sourceName = Path.GetFileName(environmentPath);
                _modelDeploymentService.AddModelToProfileIfNotExists(profileName, sourceName, Path.GetDirectoryName(Path.GetDirectoryName(environmentPath))!, ModelType.Source);
                AddProjectToVsSolution(profileName, sourceName);
                return;
            }

            // 5. Fallback
            if (!anyAdded)
            {
                _modelDeploymentService.AddModelToProfileIfNotExists(profileName, modelName, environmentPath, ModelType.Source);
                AddProjectToVsSolution(profileName, modelName);
            }
                
        }

        private static bool IsCompiledModelFolder(string path, out string modelName)
        {
            modelName = "";
            if (!Directory.Exists(path))
                return false;

            var folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (folderName.IsNullOrEmpty())
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
                AddProjectToVsSolution(profile, modelName);                
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

            AddProjectToVsSolution(profile, modelName);

            MessageLogger.Highlight($"✅ Converted model '{modelName}' registered into solution.");
        }

        private void AddProjectToVsSolution(string profileName, string modelName) => AddProjectToVsSolution(_fileService.LoadProfile(profileName), modelName);  

        private void AddProjectToVsSolution(ProfileModel profile, string modelName)
        {
            var model = profile.FindModel(modelName);
            if (model == null)
            {
                MessageLogger.Error($"❌ Error: Model '{modelName}' not found in profile after creation.");
                return;
            }
            _solutionService.AddProjectToSolution(profile, model);

            MessageLogger.Info($"✅ Model '{modelName}' added to profile '{profile.ProfileName}' and included in solution.");

        }

        private bool IsInstalledModel(string path) => path.StartsWith(_deploymentBasePath);

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
            foreach (var model in profile.AllModels)
            {
                _modelDeploymentService.CheckModelDeployment(profile, model.ModelName);
                _modelDeploymentService.CheckIfGitRepository(profileName, model.ModelName);
            }
            _fileService.SaveProfile(profile, updateExternal: true);
        }

        public void GitFetchLatest(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);

            MessageLogger.Info($"Fetch Git for profile: {profile.ProfileName}");
            foreach (var repo in profile.Repositories)
            {
                if (!repo.RepoRootFolder.IsNullOrEmpty())
                {
                    GitHelper.FetchFromRemote(repo.DisplayName, repo.RepoRootFolder);
                }    
            }
        }

        public void DeleteProfile(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);

            _modelDeploymentService.UnDeployAllModels(profileName);

            _fileService.DeleteProfile(profileName);

            MessageLogger.Info($"Profile '{profileName}' and all associated models removed.");
        }

        public bool GitResetProfile(ProfileModel profile)
        {
            if (profile == null)
            {
                MessageLogger.Error("GitResetProfile: profile is null.");
                return false;
            }

            EnsureRepositories(profile);

            if (profile.Repositories == null || profile.Repositories.Count == 0)
            {
                MessageLogger.Info("ℹ️ Git reset: No repositories found in profile.");
                return true;
            }

            var succeeded = 0;
            var failedRepos = new List<string>();

            MessageLogger.Highlight($"🔄 Git reset profile: {profile.ProfileName}");

            foreach (var repository in profile.Repositories)
            {
                if (repository?.RepoRootFolder.IsNullOrEmpty() != false)
                    continue;

                if (!GitHelper.IsGitRepository(repository.RepoRootFolder))
                    continue;

                var mainBranchName = repository.MainBranchName.IsNullOrEmpty() ? "main" : repository.MainBranchName;

                MessageLogger.Info($"➡️ {repository.DisplayName}: stash → checkout {mainBranchName} → fetch → pull");

                var ok = GitHelper.ResetToMainAndUpdate(repository.RepoRootFolder, mainBranchName);
                if (ok)
                {
                    succeeded++;
                    repository.LastKnownBranch = GitHelper.GetActiveBranch(repository.RepoRootFolder);
                }
                else
                {
                    failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                }
            }

            _fileService.SaveProfile(profile);

            if (failedRepos.Count > 0)
            {
                MessageLogger.Warning($"⚠️ Git reset finished with errors. OK: {succeeded}, Failed: {failedRepos.Count}");
                MessageLogger.Warning($"Failed repos: {string.Join(", ", failedRepos)}");
                return false;
            }

            MessageLogger.Highlight($"✅ Git reset finished. Repos updated: {succeeded}");
            return true;
        }

        public bool TagReleaseProfile(ProfileModel profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (profile.Repositories == null || profile.Repositories.Count == 0)
            {
                MessageLogger.Warning("⚠️ Tag release: profile has no repositories.");
                return false;
            }

            var tagName = $"Release-{DateTime.UtcNow:yyyy-MM-dd}";
            
            var firstGitRepoPath = profile.Repositories?
                .FirstOrDefault(r => r?.RepoRootFolder.IsNullOrEmpty() == false && GitHelper.IsGitRepository(r.RepoRootFolder))
                ?.RepoRootFolder;

            var createdBy = firstGitRepoPath.IsNullOrEmpty()
                ? $"{Environment.UserDomainName}\\{Environment.UserName}"
                : GitHelper.GetGitUserEmailOrFallback(firstGitRepoPath);

            var succeeded = 0;
            var failedRepos = new List<string>();

            MessageLogger.Highlight($"🏷️ Tag release: {profile.ProfileName} → {tagName}");

            foreach (var repository in profile.Repositories)
            {
                if (repository?.RepoRootFolder.IsNullOrEmpty() != false)
                    continue;

                if (!GitHelper.IsGitRepository(repository.RepoRootFolder))
                    continue;

                var mainBranchName = repository.MainBranchName.IsNullOrEmpty() ? "main" : repository.MainBranchName;
                var activeBranch = GitHelper.GetActiveBranch(repository.RepoRootFolder) ?? string.Empty;

                var isOnMain = activeBranch.Equals(mainBranchName, StringComparison.OrdinalIgnoreCase);
                var isOnRelease = GitHelper.IsReleaseBranchName(activeBranch);

                if (!isOnMain && !isOnRelease)
                {
                    MessageLogger.Error($"❌ {repository.DisplayName}: not on main/release branch (current: '{activeBranch}').");
                    failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                    continue;
                }

                if (GitHelper.HasUncommittedChanges(repository.RepoRootFolder))
                {
                    MessageLogger.Error($"❌ {repository.DisplayName}: has uncommitted changes. Tagging aborted for this repo.");
                    failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                    continue;
                }

                var messageLines = new List<string>
                {
                    $"Tag: {tagName}",
                    $"Profile: {profile.ProfileName}",
                    $"Created by: {createdBy}",
                    $"Branch: {activeBranch}",
                    $"Created at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                };

                MessageLogger.Info($"➡️ {repository.DisplayName}: create tag → push");

                var created = GitHelper.CreateTag(repository.RepoRootFolder, tagName, messageLines);
                if (!created)
                {
                    failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                    continue;
                }

                // Push tag to origin 
                if (!GitHelper.PushTag(repository.RepoRootFolder, tagName, "origin"))
                {
                    failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                    continue;
                }

                succeeded++;
            }

            if (failedRepos.Count > 0)
            {
                MessageLogger.Warning($"⚠️ Tag release finished with errors. OK: {succeeded}, Failed: {failedRepos.Count}");
                MessageLogger.Warning($"Failed repos: {string.Join(", ", failedRepos)}");
                return false;
            }

            MessageLogger.Highlight($"✅ Tag release finished. Repos tagged: {succeeded}");
            return true;
        }


        public void RemoveModelFromProfile(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
            var model = profile.FindModel(modelName);
            if (model == null)
            {
                MessageLogger.Warning($"Model '{modelName}' not found in profile '{profileName}'.");
                return;
            }

            _solutionService.RemoveProjectFromSolution(profileName, model.ModelName);

            profile.StandaloneModels.Remove(model);

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

        public ProfileModel LoadProfile(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);

            if(EnsureRepositories(profile))
                _fileService.SaveProfile(profile, updateExternal: true);

            UpdateGitBranchesInProfile(profile);

            return profile;

        }

        public void UpdateGitBranchesInProfile(ProfileModel profile)
        {
            foreach (var repo in profile.Repositories)
            {
                UpdateLastKnownGitState(repo);
            }

            _fileService.SaveProfile(profile);
        }

        private static void UpdateLastKnownGitState(RepositoryModel repository)
        {
            if (repository == null)
                throw new ArgumentNullException(nameof(repository));

            if (repository.RepoRootFolder.IsNullOrEmpty() || !GitHelper.IsGitRepository(repository.RepoRootFolder))
            {
                repository.LastKnownBranch = null;
                repository.LastKnownCommit = null;
                return;
            }

            repository.LastKnownBranch = GitHelper.GetActiveBranch(repository.RepoRootFolder);
            repository.LastKnownCommit = GitHelper.GetHeadCommit(repository.RepoRootFolder);
        }

        public void ListModelsInProfile(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);

            if (profile.AllModels.Count == 0)
            {
                MessageLogger.Warning($"No models found in profile '{profileName}'.");
                return;
            }

            MessageLogger.Info($"Models in Profile '{profileName}':");
            foreach (var model in profile.AllModels)
            {
                string status = model.IsDeployed ? "✅ Deployed" : "❌ Not Deployed";
                string gitStatus = model.GitUrl.IsNullOrEmpty() ? "" : "✅ Git Repo" ; 
                MessageLogger.Info($"   - {model.ModelName}\t\t - {status} - { gitStatus }");
            }
        }

        public List<ProfileEnvironmentModel> GetModelsInProfile(string profileName)
        {
            var profile = LoadProfile(profileName);
            return profile.AllModels;
        }

        public ProfileEnvironmentModel? GetModel(string profileName, string modelName)
        {
            var profile = LoadProfile(profileName);
            return profile.FindModel(modelName);
        }

       
        public void UpdateDeploymentStatus(string profileName)
        {
            var profile = LoadProfile(profileName);

            var updated = false;

            foreach (var model in profile.AllModels)
            {
                var shouldBeMarkedAsDeployed = _modelDeploymentService.IsModelActuallyDeployed(model);
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
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (profile.Repositories != null && profile.Repositories.Count > 0)
                return false;

            if (profile.StandaloneModels == null || profile.StandaloneModels.Count == 0)
                return false;

            var repoMap = new Dictionary<string, RepositoryModel>(StringComparer.OrdinalIgnoreCase);
            var standaloneModels = new List<ProfileEnvironmentModel>();

            foreach (var environmentModel in profile.StandaloneModels)
            {
                if (environmentModel == null)
                    continue;

                var repoRootFolder = (environmentModel.ModelRootFolder ?? string.Empty).Trim();
                if (repoRootFolder.IsNullOrEmpty() || !Directory.Exists(repoRootFolder))
                {
                    standaloneModels.Add(environmentModel);
                    continue;
                }

                // Only treat it as a repository if it's actually a git repo
                if (!GitHelper.IsGitRepository(repoRootFolder, out var detectedGitUrl))
                {
                    standaloneModels.Add(environmentModel);
                    continue;
                }

                // Prefer stored GitUrl if present, otherwise use detected origin
                var gitUrl = environmentModel.GitUrl.IsNullOrEmpty() ? (detectedGitUrl ?? string.Empty) : environmentModel.GitUrl;
                environmentModel.GitUrl = gitUrl;

                var repoKey = !environmentModel.GitUrl.IsNullOrEmpty() ? environmentModel.GitUrl : repoRootFolder;

                var normalizedRepoKey = RepositoryModel.NormalizeKey(repoKey);


                if (!repoMap.TryGetValue(normalizedRepoKey, out var repository))
                {
                    repository = new RepositoryModel
                    {
                        RepoRootFolder = repoRootFolder,
                        GitUrl = environmentModel.GitUrl.IsNullOrEmpty() ? null : environmentModel.GitUrl
                    };

                    repository.EnsureRepoId();
                    repository.EnsureDisplayName();

                    repoMap.Add(repository.GetRepoKey(), repository);
                }

                repository.Models.Add(environmentModel);
            }

            profile.Repositories = repoMap.Values
                .OrderBy(repository => repository.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            profile.StandaloneModels = standaloneModels;

            MessageLogger.Info($"📦 Repositories built: {profile.Repositories.Count}. Standalone models: {profile.StandaloneModels.Count}");
            return true;
        }

        private static bool TryLoadExternalProfile(string filePath, out ProfileModel profile, out bool isLegacy)
        {
            profile = null!;
            isLegacy = false;

            try
            {
                if (!File.Exists(filePath))
                    return false;

                var json = File.ReadAllText(filePath);

                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                if (document.RootElement.TryGetProperty("ExportFormatVersion", out _))
                {
                    var exportProfile = FileHelper.LoadJson<ExportProfileModel>(filePath);
                    if (exportProfile == null || exportProfile.ProfileName.IsNullOrEmpty())
                        return false;

                    profile = ExportProfileMapper.FromExport(exportProfile, filePath);
                    return true;
                }

                // Legacy format
                var legacyProfile = FileHelper.LoadJson<ProfileModel>(filePath);
                if (legacyProfile == null || legacyProfile.ProfileName.IsNullOrEmpty())
                    return false;

                isLegacy = true;
                profile = legacyProfile;
                return true;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to load profile file '{filePath}': {ex.Message}");
                return false;
            }
        }


        public void UpdateRepositoryProperties(string profileName, RepositoryModel updatedRepository)
        {
            if (profileName.IsNullOrEmpty())
            {
                MessageLogger.Error("UpdateRepositoryProperties: profileName is empty.");
                return;
            }

            if (updatedRepository == null)
            {
                MessageLogger.Error("UpdateRepositoryProperties: updatedRepository is null.");
                return;
            }

            var profile = _fileService.LoadProfile(profileName);

            var existingRepository = profile.Repositories
                .FirstOrDefault(repository => repository.RepoId.SameAs(updatedRepository.RepoId));

            if (existingRepository == null)
            {
                MessageLogger.Error($"UpdateRepositoryProperties: repo '{updatedRepository.DisplayName}' not found in profile '{profileName}'.");
                return;
            }

            existingRepository.DisplayName = updatedRepository.DisplayName ?? string.Empty;
            existingRepository.PreferredBranch = updatedRepository.PreferredBranch;
            existingRepository.AutoCheckoutOnProfileLoad = updatedRepository.AutoCheckoutOnProfileLoad;
            existingRepository.AutoStashOnDirtyCheckout = updatedRepository.AutoStashOnDirtyCheckout;
            existingRepository.Task = updatedRepository.Task ?? string.Empty;
            existingRepository.TaskComment = updatedRepository.TaskComment ?? string.Empty;

            _fileService.SaveProfile(profile, updateExternal: true);
            MessageLogger.Info($"✅ Repository properties saved: {existingRepository.DisplayName}");
        }

        public void UpdateModelProperties(string profileName, ProfileEnvironmentModel updatedEnvironment)
        {
            if (profileName.IsNullOrEmpty())
            {
                MessageLogger.Error("UpdateModelProperties: profileName is empty.");
                return;
            }

            if (updatedEnvironment == null || updatedEnvironment.ModelName.IsNullOrEmpty())
            {
                MessageLogger.Error("UpdateModelProperties: updatedEnvironment is null or ModelName is empty.");
                return;
            }

            var profile = _fileService.LoadProfile(profileName);

            var targetEnvironment = profile.AllModels
                .FirstOrDefault(model => model.ModelName.SameAs(updatedEnvironment.ModelName));

            if (targetEnvironment == null)
            {
                MessageLogger.Error($"UpdateModelProperties: model '{updatedEnvironment.ModelName}' not found in profile '{profileName}'.");
                return;
            }


            // Guardrail: only one Main FO model
            if (updatedEnvironment.IsMainFOModel)
            {
                foreach (var environment in profile.AllModels)
                    environment.IsMainFOModel = false;

                targetEnvironment.IsMainFOModel = true;
            }
            else
            {
                targetEnvironment.IsMainFOModel = false;
            }

            _fileService.SaveProfile(profile, updateExternal: true);
            MessageLogger.Info($"✅ Model properties saved: {targetEnvironment.ModelName}");
        }
    }
}

