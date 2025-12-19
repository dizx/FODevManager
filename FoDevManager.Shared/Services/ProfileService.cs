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
                if (!TryLoadExternalProfile(importPath, out var importedProfile))
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
            if (!File.Exists(importPath))
            {
                MessageLogger.Error($"Profile file not found: {importPath}");
                return null!;
            }

            try
            {
                if (!TryLoadExternalProfile(importPath, out var sourceProfile))
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
                        .FirstOrDefault(model => model.IsMainFOModel && !string.IsNullOrWhiteSpace(model.ModelRootFolder));

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
                MessageLogger.Warning($"Repo '{repository.RepoId}' has no GitUrl. Skipping clone.");
                return;
            }

            var repoFolderName = ExtractAzureDevOpsRepo(repository.GitUrl);
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
                    MessageLogger.Error($"Failed to clone repository '{repository.RepoId}'.");
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
            _fileService.SaveProfile(profile, updateExternal: true);
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

            if (profile.StandaloneModels.Any(e => e.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageLogger.Warning($"⚠️ Model '{modelName}' is already in the profile '{profileName}'. Skipping add.");
                return;
            }

            string deploymentLinkPath = Path.Combine(_deploymentBasePath, modelName);
            bool isAlreadyDeployed = Directory.Exists(deploymentLinkPath);

            profile.StandaloneModels.Add(new ProfileEnvironmentModel
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
            var newModel = profile.AllModels.Find(e => e.ModelName == modelName);
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
            foreach (var env in profile.StandaloneModels)
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
            foreach (var model in profile.AllModels)
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

                if (!repoMap.TryGetValue(repoKey, out var repository))
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

            // ✅ IMPORTANT: remove repo-backed models from the standalone list
            profile.StandaloneModels = standaloneModels;

            MessageLogger.Info($"📦 Repositories built: {profile.Repositories.Count}. Standalone models: {profile.StandaloneModels.Count}");
            return true;
        }

        private static bool TryLoadExternalProfile(string filePath, out ProfileModel profile)
        {
            profile = null!;

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
                    if (exportProfile == null || string.IsNullOrWhiteSpace(exportProfile.ProfileName))
                        return false;

                    profile = ExportProfileMapper.FromExport(exportProfile, filePath);
                    return true;
                }

                // Legacy format
                var legacyProfile = FileHelper.LoadJson<ProfileModel>(filePath);
                if (legacyProfile == null || string.IsNullOrWhiteSpace(legacyProfile.ProfileName))
                    return false;

                profile = legacyProfile;
                return true;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to load profile file '{filePath}': {ex.Message}");
                return false;
            }
        }

    }
}
