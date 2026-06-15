using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Shared.Models;
using FODevManager.Shared.Utils;
using FODevManager.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
        private readonly DeployablePackageService _deployablePackageService;
        private readonly ModelVersionService _modelVersionService;
        private readonly IDeploymentLedgerService _deploymentLedgerService;
        private readonly ProfilesContainer _profilesContainer;

        public ProfileService(AppConfig config, FileService fileService, VisualStudioSolutionService solutionService, ModelDeploymentService modelDeploymentService, DeployablePackageService deployablePackageService, ModelVersionService modelVersionService, ProfilesContainer profilesContainer, IDeploymentLedgerService deploymentLedgerService)
        {
            _defaultSourceDirectory = config.DefaultSourceDirectory;
            _deploymentBasePath = config.DeploymentBasePath;
            _profileStoragePath = config.ProfileStoragePath;
            _checkUncommittedBeforeSwitch = config.CheckUncommittedBeforeSwitch;
            _fileService = fileService;
            _solutionService = solutionService;
            _modelDeploymentService = modelDeploymentService;
            _deployablePackageService = deployablePackageService;
            _modelVersionService = modelVersionService;
            _deploymentLedgerService = deploymentLedgerService;
            _profilesContainer = profilesContainer;
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);
            

        }

        public void CreateProfile(string profileName)
        {
            if (_fileService.ExistProfile(profileName))
            {
                MessageLogger.Warning("Profile already exists");
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
            SetActiveProfile(profileName);
            
            MessageLogger.Info($"✅ Profile '{profileName}' created with solution file: {solutionFilePath}");
        }

        public bool SwitchProfile(string newProfileName)
        {
            var currentProfileName = GetActiveProfileName();

            
            if (currentProfileName == newProfileName)
            {
                MessageLogger.Info($"ℹ️ Profile '{newProfileName}' is already active");
                return false;
            }

            if (currentProfileName.IsNullOrEmpty())
            {
                MessageLogger.Info("ℹ️ No active profile found. Proceeding to switch");
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
                            MessageLogger.Error($"❌ Uncommitted Git changes found in repo '{repo.DisplayName}'. Switch aborted");
                            return false;
                        }
                    }
                }

                MessageLogger.Info("📦 Cleaning up managed model deployments before profile switch");
                _modelDeploymentService.UnDeployManagedDeployments();
            }

            MessageLogger.Info($"🔄 Switching to profile '{newProfileName}'.");

            var newProfile = _fileService.LoadProfile(newProfileName);
            
            if (EnsureRepositories(newProfile))
                _fileService.SaveProfile(newProfile, updateExternal: true);

            SwitchBranchesInProfile(newProfile);

            UpdateDeploymentStatus(newProfileName);

            if (_deployablePackageService.EnsureCompiledNugetModels(newProfile))
                _fileService.SaveProfile(newProfile, updateExternal: true);
            _modelDeploymentService.DeployAllModelsForProfileSwitch(newProfileName);
            ApplyDatabase(newProfileName);
            SetActiveProfile(newProfileName);
            MessageLogger.Highlight($"✅ Successfully switched to profile '{newProfileName}'");

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
                MessageLogger.Error("CheckProfileModelChanges: currentProfile is null");
                return new ModelSyncResult();
            }

            if (currentProfile.ProfileFilePath.IsNullOrEmpty())
            {
                MessageLogger.Warning("CheckProfileModelChanges: ProfileFilePath is not set. Attempting first-time export to repo Artifacts..");

                if (!TryCreateExternalProfileExport(currentProfile, out var bootstrappedPath))
                {
                    MessageLogger.Warning("CheckProfileModelChanges: Create profile export failed. Skipping model sync check");
                    return new ModelSyncResult();
                }

                MessageLogger.Info($"CheckProfileModelChanges: Profile export OK. Using '{bootstrappedPath}'");
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
                    MessageLogger.Error("CheckProfileModelChanges: Imported profile is invalid");
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
                MessageLogger.Warning("Profile export: No Main FO model found. Cannot export external profile");
                return false;
            }

            var repoRootFolder = currentProfile.TryGetRepoRootFolder(mainFoModel) ?? mainFoModel.ModelRootFolder;
            if (repoRootFolder.IsNullOrEmpty() || !Directory.Exists(repoRootFolder))
            {
                MessageLogger.Error($"Profile export: Repo root folder not found: '{repoRootFolder}'");
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
                MessageLogger.Error($"Profile export: Failed to export profile to '{exportedProfilePath}'");
                return false;
            }

            MessageLogger.Highlight($"✅ Profile export: Exported external profile to '{exportedProfilePath}'");
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
                    MessageLogger.Error("Invalid profile file");
                    return null!;
                }

                if (isLegacy)
                {
                    EnsureRepositories(sourceProfile);
                }

                sourceProfile.ProfileFilePath = importPath;
                sourceProfile.IsActive = false;
                PreserveLocalDatabaseOverride(sourceProfile);

                var profileDestPath = Path.Combine(_profileStoragePath, sourceProfile.ProfileName + ".json");
                if (File.Exists(profileDestPath))
                {
                    MessageLogger.Warning($"Profile '{sourceProfile.ProfileName}' already exists. It will be overwritten");
                }

                MessageLogger.Info($"Importing profile '{sourceProfile.ProfileName}'..");

                // 1) Import repositories (clone once per repo, configure models)
                foreach (var repository in sourceProfile.Repositories ?? new List<RepositoryModel>())
                {
                    EnsureRepositoryAvailable(repository);
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
                MessageLogger.Highlight($"Profile '{sourceProfile.ProfileName}' imported successfully");

                return sourceProfile;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to import profile: {ex.Message}");
                return null!;
            }
        }

        private void PreserveLocalDatabaseOverride(ProfileModel importedProfile)
        {
            if (importedProfile == null)
                throw new ArgumentNullException(nameof(importedProfile));

            if (importedProfile.ProfileName.IsNullOrEmpty() || !_fileService.ExistProfile(importedProfile.ProfileName))
                return;

            var existingProfile = _fileService.LoadProfile(importedProfile.ProfileName);
            var existingDatabaseName = NormalizeDatabaseName(existingProfile.DatabaseName);
            var importedDatabaseName = NormalizeDatabaseName(importedProfile.DatabaseName);

            if (existingDatabaseName.SameAs(importedDatabaseName))
                return;

            // A non-default local database name is treated as an explicit machine-local override.
            if (existingDatabaseName.SameAs("AXDB"))
                return;

            importedProfile.DatabaseName = existingProfile.DatabaseName;
            MessageLogger.Info(
                $"Preserving local database '{existingProfile.DatabaseName}' for profile '{importedProfile.ProfileName}' instead of imported value '{importedDatabaseName}'");
        }

        private static string NormalizeDatabaseName(string? databaseName)
        {
            var normalized = (databaseName ?? string.Empty).Trim();
            return normalized.IsNullOrEmpty() ? "AXDB" : normalized;
        }

        

        private void ImportStandaloneModel(ProfileEnvironmentModel model)
        {
            if (model == null)
                return;

            var modelRootFolder = Path.Combine(_defaultSourceDirectory, model.ModelName);
            FileHelper.EnsureDirectoryExists(modelRootFolder);

            model.ModelRootFolder = modelRootFolder;

            ConfigureModelPathsAndDeployment(null, model, modelRootFolder);
        }

        public ProfileModel ImportProfileFromRepoUrl(string repoUrl)
        {
            if (repoUrl.IsNullOrEmpty())
            {
                MessageLogger.Error("ImportProfileFromRepoUrl: repoUrl is empty");
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
                MessageLogger.Warning($"Repo already exists at '{targetRepoRoot}'. Skipping clone");
            }
            else
            {
                if (Directory.Exists(targetRepoRoot) && Directory.EnumerateFileSystemEntries(targetRepoRoot).Any())
                {
                    MessageLogger.Error($"Target folder exists and is not empty (and not a git repo): {targetRepoRoot}");
                    return null!;
                }

                if (!GitHelper.CloneRepository(repoUrl, targetRepoRoot, allowCredentialPrompt: true))
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

            MessageLogger.Highlight($"📥 Importing profile from: {profileJsonPath}");
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

        private void ConfigureModelPathsAndDeployment(RepositoryModel? repository, ProfileEnvironmentModel model, string modelRootFolder)
        {
            if (model.ModelType == ModelType.Source)
            {
                model.ProjectFilePath = FileHelper.GetProjectFilePath(model.ModelName, modelRootFolder);
                model.MetadataFolder = FileHelper.GetMetadataFolder(model.ModelName, modelRootFolder);
                model.CompiledModelFolder = string.Empty;
                model.PackageId = string.Empty;
                model.PackageVersion = string.Empty;
            }
            else if (model.ModelType == ModelType.Compiled)
            {
                model.ProjectFilePath = string.Empty;
                model.MetadataFolder = string.Empty;
                model.CompiledModelFolder = FileHelper.GetLibsFolder(model.ModelName, modelRootFolder);
            }
            else if (repository != null)
            {
                model.ProjectFilePath = string.Empty;
                model.MetadataFolder = string.Empty;
                model.CompiledModelFolder = string.Empty;
                model.ModelRootFolder = repository.RepoRootFolder;
            }
            else
            {
                model.ProjectFilePath = string.Empty;
                model.MetadataFolder = string.Empty;
                MessageLogger.Warning($"Compiled NuGet model '{model.ModelName}' is standalone. Repository package resolution is skipped");
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

            var newDbName = (dbName ?? string.Empty).Trim();
            if (newDbName.IsNullOrEmpty())
            {
                MessageLogger.Warning("⚠️ Database name cannot be empty");
                return;
            }

            // Normalize "AXDB" as the default value
            var normalizedNew = newDbName.SameAs("AXDB") ? "AXDB" : newDbName;

            // If unchanged, do nothing (prevents repeated saves/exports)
            if ((profile.DatabaseName ?? "AXDB").SameAs(normalizedNew))
            {
                MessageLogger.Info($"ℹ️ Database name unchanged ('{normalizedNew}')");
                return;
            }

            var oldWasDefault = (profile.DatabaseName ?? "AXDB").SameAs("AXDB");
            var newIsDefault = normalizedNew.SameAs("AXDB");

            profile.DatabaseName = normalizedNew;

            // Always save the local profile file
            // Only export (updateExternal) ONCE: when moving away from default AXDB
            var shouldUpdateExternal = oldWasDefault && !newIsDefault;

            _fileService.SaveProfile(profile, updateExternal: shouldUpdateExternal);

            if (profile.IsActive)
            {
                ApplyDatabase(profileName);
            }

            MessageLogger.Info($"✅ Database name '{dbName}' set for profile '{profileName}'");
        }

        public void SetActiveProfile(string profileName)
        {
            foreach (var profile in _fileService.GetAllProfiles())
            {
                if (profile is null) continue;

                profile.IsActive = profile.ProfileName.SameAs(profileName);
                _fileService.SaveProfile(profile);
            }

            MessageLogger.Highlight($"✅ Profile '{profileName}' marked as active");
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
                MessageLogger.Info($"ℹ️ Database is already set to '{currentDb}'. No change needed");
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
            if (LooksLikeNugetUrl(environmentPath))
            {
                MessageLogger.Error("AddModel: a NuGet URL requires repository selection. Use AddNugetModel instead");
                return;
            }

            if (IsInstalledModel(environmentPath))
            {
                HandleInstalledModel(profileName, modelName, environmentPath);
                return;
            }

            bool anyAdded = false;

            if (HasCompiledModelsInLibs(environmentPath, out var compiledFolders))
            {
                MessageLogger.Highlight("Detected compiled model(s) under Libs. Adding..");

                foreach (var folder in compiledFolders)
                {
                    if (IsCompiledModelFolder(folder, out var detectedName))
                    {
                        _modelDeploymentService.AddModelToProfileIfNotExists(profileName, detectedName, folder, ModelType.Compiled);
                        anyAdded = true;
                    }
                }
            }

            if (HasSourceModelsInMetadata(environmentPath, out var sourceFolders))
            {
                MessageLogger.Highlight("Detected source model(s) under Metadata. Adding..");

                foreach (var folder in sourceFolders)
                {
                    var detectedName = Path.GetFileName(folder);
                    var sourceRoot = ResolveSourceModelRoot(folder, environmentPath);
                    _modelDeploymentService.AddModelToProfileIfNotExists(profileName, detectedName, sourceRoot, ModelType.Source);
                    AddProjectToVsSolution(profileName, detectedName);
                    anyAdded = true;
                }
            }

            if (!anyAdded && IsCompiledModelFolder(environmentPath, out var compiledName))
            {
                _modelDeploymentService.AddModelToProfileIfNotExists(profileName, compiledName, environmentPath, ModelType.Compiled);
                return;
            }

            var parent = Directory.GetParent(environmentPath)?.Name;
            if (!anyAdded && parent.SameAs("Metadata"))
            {
                var sourceName = Path.GetFileName(environmentPath);
                var sourceRoot = ResolveSourceModelRoot(environmentPath, Path.GetDirectoryName(Path.GetDirectoryName(environmentPath))!);
                _modelDeploymentService.AddModelToProfileIfNotExists(profileName, sourceName, sourceRoot, ModelType.Source);
                AddProjectToVsSolution(profileName, sourceName);
                return;
            }

            if (!anyAdded)
            {
                var sourceRoot = ResolveSourceModelRoot(environmentPath, environmentPath);
                _modelDeploymentService.AddModelToProfileIfNotExists(profileName, modelName, sourceRoot, ModelType.Source);
                AddProjectToVsSolution(profileName, modelName);
            }
        }

        private static string ResolveSourceModelRoot(string preferredPath, string fallbackPath)
        {
            try
            {
                return FileHelper.GetModelRootFolder(preferredPath);
            }
            catch
            {
                try
                {
                    return FileHelper.GetModelRootFolder(fallbackPath);
                }
                catch
                {
                    return fallbackPath;
                }
            }
        }

        public void AddNugetModel(string profileName, string repoId, string modelName, string packageUrl)
        {
            if (profileName.IsNullOrEmpty() || repoId.IsNullOrEmpty() || packageUrl.IsNullOrEmpty())
            {
                MessageLogger.Error("AddNugetModel: profile, repository, and package URL are required");
                return;
            }

            var profile = _fileService.LoadProfile(profileName);
            var repository = profile.Repositories.FirstOrDefault(repo => repo.RepoId.SameAs(repoId));
            if (repository == null)
            {
                MessageLogger.Error($"AddNugetModel: repository '{repoId}' not found in profile '{profileName}'");
                return;
            }

            if (!_deployablePackageService.AddOrUpdatePackageFromUrl(profile, repository, packageUrl.Trim()))
            {
                MessageLogger.Warning($"Package from '{packageUrl}' was added to Build\\isv.config, but no compiled models were discovered yet");
            }

            _fileService.SaveProfile(profile, updateExternal: true);
            MessageLogger.Info($"NuGet package '{packageUrl}' added to repository '{repository.DisplayName}'");
        }

        private static string UpdatePackageOverviewUrl(string existingPackageUrl, string packageId, string packageVersion)
        {
            if (existingPackageUrl.IsNullOrEmpty()
                || packageId.IsNullOrEmpty()
                || packageVersion.IsNullOrEmpty()
                || !Uri.TryCreate(existingPackageUrl, UriKind.Absolute, out var existingUri))
            {
                return existingPackageUrl ?? string.Empty;
            }

            var segments = existingUri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Uri.UnescapeDataString)
                .ToArray();

            var nugetIndex = Array.FindIndex(segments, segment => segment.SameAs("NuGet"));
            if (nugetIndex < 0 || nugetIndex + 3 >= segments.Length)
                return existingPackageUrl;

            segments[nugetIndex + 1] = packageId;
            segments[nugetIndex + 2] = "overview";
            segments[nugetIndex + 3] = packageVersion;

            var builder = new UriBuilder(existingUri)
            {
                Path = "/" + string.Join("/", segments.Select(Uri.EscapeDataString)),
                Query = string.Empty,
                Fragment = string.Empty
            };

            return builder.Uri.AbsoluteUri;
        }

        private static bool LooksLikeNugetUrl(string value)
        {
            if (value.IsNullOrEmpty())
                return false;

            return Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme.SameAs("http") || uri.Scheme.SameAs("https"));
        }

        private static bool TryParseNugetPackageUrl(string packageUrl, out string packageId, out string packageVersion)
        {
            packageId = string.Empty;
            packageVersion = string.Empty;

            if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out var packageUri))
                return false;

            var segments = packageUri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Uri.UnescapeDataString)
                .ToArray();

            var nugetIndex = Array.FindIndex(segments, segment => segment.SameAs("NuGet"));
            if (nugetIndex < 0 || nugetIndex + 3 >= segments.Length)
                return false;

            packageId = segments[nugetIndex + 1];
            packageVersion = segments[nugetIndex + 3];

            return !packageId.IsNullOrEmpty()
                && segments[nugetIndex + 2].SameAs("overview")
                && !packageVersion.IsNullOrEmpty();
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
            if (envPath.IsNullOrEmpty())
                return false;

            var trimmed = envPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmed);

            // Support both "Libs" and "Lib" folder names.
            var libsCandidates = (folderName.SameAs("Libs") || folderName.SameAs("Lib"))
                ? new[] { trimmed }
                : new[]
                {
                    Path.Combine(trimmed, "Libs"),
                    Path.Combine(trimmed, "Lib")
                };

            foreach (var libsRoot in libsCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(libsRoot))
                    continue;

                foreach (var dir in Directory.EnumerateDirectories(libsRoot))
                    if (IsCompiledModelFolder(dir, out _))
                        compiledFolders.Add(dir);
            }

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
                MessageLogger.Error($"❌ Failed to convert installed model at '{environmentPath}'");
                return;
            }

            AddProjectToVsSolution(profile, modelName);

            MessageLogger.Highlight($"✅ Converted model '{modelName}' registered into solution");
        }

        private void AddProjectToVsSolution(string profileName, string modelName) => AddProjectToVsSolution(_fileService.LoadProfile(profileName), modelName);  

        private void AddProjectToVsSolution(ProfileModel profile, string modelName)
        {
            var model = profile.FindModel(modelName);
            if (model == null)
            {
                MessageLogger.Error($"❌ Error: Model '{modelName}' not found in profile after creation");
                return;
            }
            _solutionService.AddProjectToSolution(profile, model);

            MessageLogger.Info($"✅ Model '{modelName}' added to profile '{profile.ProfileName}' and included in solution");

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
            EnsureRepositories(profile);

            foreach (var repository in profile.Repositories ?? new List<RepositoryModel>())
            {
                EnsureRepositoryAvailable(repository);
            }

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

            MessageLogger.Info($"Profile '{profileName}' and all associated models removed");
        }

        public bool GitResetProfile(ProfileModel profile)
        {
            if (profile == null)
            {
                MessageLogger.Error("GitResetProfile: profile is null");
                return false;
            }

            EnsureRepositories(profile);

            if (profile.Repositories == null || profile.Repositories.Count == 0)
            {
                MessageLogger.Info("ℹ️ Git reset: No repositories found in profile");
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

                MessageLogger.Info($"🔄 {repository.DisplayName}: stash → checkout {mainBranchName} → fetch → pull");

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
                MessageLogger.Warning("⚠️ Tag release: profile has no repositories");
                return false;
            }

            var releaseDateUtc = DateTime.UtcNow;

            var firstGitRepoPath = profile.Repositories?
                .FirstOrDefault(r => r?.RepoRootFolder.IsNullOrEmpty() == false && GitHelper.IsGitRepository(r.RepoRootFolder))
                ?.RepoRootFolder;

            var createdBy = firstGitRepoPath.IsNullOrEmpty()
                ? $"{Environment.UserDomainName}\\{Environment.UserName}"
                : GitHelper.GetGitUserEmailOrFallback(firstGitRepoPath);

            var succeeded = 0;
            var createdTags = 0;
            var failedRepos = new List<string>();

            MessageLogger.Highlight($"🏷️ Tag release: {profile.ProfileName} → {releaseDateUtc:yyyy-MM-dd}");

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
                    MessageLogger.Error($"❌ {repository.DisplayName}: not on main/release branch (current: '{activeBranch}')");
                    failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                    continue;
                }

                if (GitHelper.HasUncommittedChanges(repository.RepoRootFolder))
                {
                    MessageLogger.Error($"❌ {repository.DisplayName}: has uncommitted changes. Tagging aborted for this repo");
                    failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                    continue;
                }

                var releaseTargets = GetReleaseTagTargets(repository, releaseDateUtc).ToList();
                if (releaseTargets.Count == 0)
                {
                    MessageLogger.Info($"ℹ️ {repository.DisplayName}: no source model changes detected since the last release tag");
                    continue;
                }

                if (releaseTargets.Any(target => GitHelper.TagExists(repository.RepoRootFolder, target.TagName)))
                {
                    MessageLogger.Error($"❌ {repository.DisplayName}: one or more release tags already exist. Tagging aborted for this repo");
                    failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                    continue;
                }

                var originalDescriptorContents = releaseTargets.ToDictionary(
                    target => target.DescriptorFilePath,
                    target => File.ReadAllText(target.DescriptorFilePath),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var releaseTarget in releaseTargets)
                {
                    if (!TryIncrementDescriptorRevision(releaseTarget.DescriptorFilePath, out var updatedVersion))
                    {
                        RestoreDescriptorFiles(originalDescriptorContents);
                        MessageLogger.Error($"❌ {repository.DisplayName}: failed to update descriptor for model '{releaseTarget.Model.ModelName}'");
                        failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                        releaseTargets.Clear();
                        break;
                    }

                    releaseTarget.NewVersion = updatedVersion;
                    releaseTarget.TagName = BuildReleaseTagName(releaseDateUtc, releaseTarget.Model.ModelName, updatedVersion);
                }

                if (releaseTargets.Count == 0)
                    continue;

                var descriptorFilesToCommit = releaseTargets
                    .Select(target => target.DescriptorFileRelativePath)
                    .ToList();

                var commitMessage = "Bump release versions: " + string.Join(", ",
                    releaseTargets.Select(target => $"{target.Model.ModelName} {target.NewVersion}"));

                if (!GitHelper.CommitFiles(repository.RepoRootFolder, descriptorFilesToCommit, commitMessage))
                {
                    RestoreDescriptorFiles(originalDescriptorContents);
                    failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                    continue;
                }

                var repoFailed = false;
                foreach (var releaseTarget in releaseTargets)
                {
                    var messageLines = new List<string>
                    {
                        $"Tag: {releaseTarget.TagName}",
                        $"Profile: {profile.ProfileName}",
                        $"Model: {releaseTarget.Model.ModelName}",
                        $"Version: {releaseTarget.NewVersion}",
                        $"Created by: {createdBy}",
                        $"Branch: {activeBranch}",
                        $"Created at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
                    };

                    MessageLogger.Info($"🏷️ {repository.DisplayName}: create tag + push → {releaseTarget.TagName}");

                    if (!GitHelper.CreateTag(repository.RepoRootFolder, releaseTarget.TagName, messageLines))
                    {
                        repoFailed = true;
                        break;
                    }

                    if (!GitHelper.PushTag(repository.RepoRootFolder, releaseTarget.TagName, "origin"))
                    {
                        repoFailed = true;
                        break;
                    }

                    createdTags++;
                }

                if (repoFailed)
                {
                    failedRepos.Add(repository.RepoId ?? repository.RepoRootFolder);
                    continue;
                }

                succeeded++;
            }

            if (failedRepos.Count > 0)
            {
                MessageLogger.Warning($"⚠️ Tag release finished with errors. Repos OK: {succeeded}, Failed: {failedRepos.Count}, Tags created: {createdTags}");
                MessageLogger.Warning($"Failed repos: {string.Join(", ", failedRepos)}");
                return false;
            }

            MessageLogger.Highlight($"✅ Tag release finished. Repos tagged: {succeeded}, Tags created: {createdTags}");
            return true;
        }

        private static IEnumerable<ReleaseTagTarget> GetReleaseTagTargets(RepositoryModel repository, DateTime releaseDateUtc)
        {
            var repoRootFolder = repository.RepoRootFolder ?? string.Empty;
            if (repoRootFolder.IsNullOrEmpty())
                yield break;

            var repoTags = GitHelper.GetTags(repoRootFolder);
            var latestLegacyReleaseTag = repoTags.FirstOrDefault(IsLegacyReleaseTagName);

            foreach (var model in repository.Models ?? new List<ProfileEnvironmentModel>())
            {
                if (model == null || model.ModelType != ModelType.Source)
                    continue;

                if (model.MetadataFolder.IsNullOrEmpty() || !Directory.Exists(model.MetadataFolder))
                    continue;

                var descriptorFilePath = GetDescriptorFilePath(model);
                if (!File.Exists(descriptorFilePath))
                {
                    MessageLogger.Warning($"Descriptor file not found for model '{model.ModelName}': {descriptorFilePath}");
                    continue;
                }

                var metadataRelativePath = TryGetGitRelativePath(repoRootFolder, model.MetadataFolder);
                var descriptorRelativePath = TryGetGitRelativePath(repoRootFolder, descriptorFilePath);
                if (metadataRelativePath.IsNullOrEmpty() || descriptorRelativePath.IsNullOrEmpty())
                {
                    MessageLogger.Warning($"Model '{model.ModelName}' is outside repository root '{repository.DisplayName}'. Skipping release tag");
                    continue;
                }

                var latestModelReleaseTag = FindLatestModelReleaseTag(repoTags, model.ModelName);
                var comparisonTag = latestModelReleaseTag ?? latestLegacyReleaseTag;
                if (!GitHelper.HasChangesInPathSinceTag(repoRootFolder, metadataRelativePath, comparisonTag))
                    continue;

                if (!TryReadDescriptorVersion(descriptorFilePath, out var currentVersion))
                {
                    MessageLogger.Warning($"Unable to read version from descriptor for model '{model.ModelName}'. Skipping release tag");
                    continue;
                }

                var nextVersion = currentVersion.WithIncrementedRevision();

                yield return new ReleaseTagTarget
                {
                    Model = model,
                    DescriptorFilePath = descriptorFilePath,
                    DescriptorFileRelativePath = descriptorRelativePath,
                    NewVersion = nextVersion,
                    TagName = BuildReleaseTagName(releaseDateUtc, model.ModelName, nextVersion)
                };
            }
        }

        private static string GetDescriptorFilePath(ProfileEnvironmentModel model)
        {
            return Path.Combine(model.MetadataFolder ?? string.Empty, "Descriptor", $"{model.ModelName}.xml");
        }

        private static string? FindLatestModelReleaseTag(IReadOnlyCollection<string> tags, string modelName)
        {
            var modelSegment = Regex.Escape(NormalizeTagSegment(modelName));
            var pattern = new Regex(
                $"^Release-\\d{{4}}-\\d{{2}}-\\d{{2}}-{modelSegment}-\\d+\\.\\d+\\.\\d+$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            return tags.FirstOrDefault(tag => pattern.IsMatch(tag));
        }

        private static bool IsLegacyReleaseTagName(string tagName)
        {
            if (tagName.IsNullOrEmpty())
                return false;

            return Regex.IsMatch(tagName, "^Release-\\d{4}-\\d{2}-\\d{2}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string BuildReleaseTagName(DateTime releaseDateUtc, string modelName, ModelVersion version)
        {
            return $"Release-{releaseDateUtc:yyyy-MM-dd}-{NormalizeTagSegment(modelName)}-{version}";
        }

        private static string NormalizeTagSegment(string value)
        {
            var normalized = Regex.Replace((value ?? string.Empty).Trim(), "[^A-Za-z0-9._-]+", "-");
            normalized = normalized.Trim('-');
            return normalized.IsNullOrEmpty() ? "model" : normalized;
        }

        private static string? TryGetGitRelativePath(string repoRootFolder, string fullPath)
        {
            if (repoRootFolder.IsNullOrEmpty() || fullPath.IsNullOrEmpty())
                return null;

            var relativePath = Path.GetRelativePath(repoRootFolder, fullPath);
            if (relativePath.StartsWith(".", StringComparison.Ordinal))
                return null;

            return relativePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static bool TryReadDescriptorVersion(string descriptorFilePath, out ModelVersion version)
        {
            version = default;

            try
            {
                var document = XDocument.Load(descriptorFilePath);
                var majorValue = document.Descendants("VersionMajor").FirstOrDefault()?.Value;
                var minorValue = document.Descendants("VersionMinor").FirstOrDefault()?.Value;
                var buildValue = document.Descendants("VersionBuild").FirstOrDefault()?.Value;
                var revisionValue = document.Descendants("VersionRevision").FirstOrDefault()?.Value;

                if (!int.TryParse(majorValue, out var major)
                    || !int.TryParse(minorValue, out var minor)
                    || !TryResolveFoVersionComponent(buildValue, revisionValue, out var revision))
                {
                    return false;
                }

                version = new ModelVersion(major, minor, revision);
                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"Failed to read descriptor version from '{descriptorFilePath}': {exception.Message}");
                return false;
            }
        }

        private static bool TryIncrementDescriptorRevision(string descriptorFilePath, out ModelVersion newVersion)
        {
            newVersion = default;

            try
            {
                var document = XDocument.Load(descriptorFilePath);
                var majorElement = document.Descendants("VersionMajor").FirstOrDefault();
                var minorElement = document.Descendants("VersionMinor").FirstOrDefault();
                var buildElement = document.Descendants("VersionBuild").FirstOrDefault();
                var revisionElement = document.Descendants("VersionRevision").FirstOrDefault();

                if (majorElement == null || minorElement == null || revisionElement == null)
                    return false;

                if (!int.TryParse(majorElement.Value, out var major)
                    || !int.TryParse(minorElement.Value, out var minor)
                    || !TryResolveFoVersionComponent(buildElement?.Value, revisionElement.Value, out var revision))
                {
                    return false;
                }

                revision++;
                if (buildElement == null)
                {
                    revisionElement.AddBeforeSelf(new XElement("VersionBuild", revision.ToString()));
                }
                else
                {
                    buildElement.Value = revision.ToString();
                }

                revisionElement.Value = "0";
                document.Save(descriptorFilePath);

                newVersion = new ModelVersion(major, minor, revision);
                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"Failed to update descriptor version '{descriptorFilePath}': {exception.Message}");
                return false;
            }
        }

        private static void RestoreDescriptorFiles(IReadOnlyDictionary<string, string> originalDescriptorContents)
        {
            foreach (var descriptorFile in originalDescriptorContents)
            {
                try
                {
                    File.WriteAllText(descriptorFile.Key, descriptorFile.Value);
                }
                catch (Exception exception)
                {
                    MessageLogger.Warning($"Failed to restore descriptor file '{descriptorFile.Key}': {exception.Message}");
                }
            }
        }

        private static bool TryResolveFoVersionComponent(string? buildValue, string? revisionValue, out int versionComponent)
        {
            versionComponent = 0;

            if (int.TryParse(buildValue, out var build) && build > 0)
            {
                versionComponent = build;
                return true;
            }

            if (int.TryParse(revisionValue, out var revision))
            {
                versionComponent = revision;
                return true;
            }

            if (int.TryParse(buildValue, out build))
            {
                versionComponent = build;
                return true;
            }

            return false;
        }

        private sealed class ReleaseTagTarget
        {
            public required ProfileEnvironmentModel Model { get; init; }
            public required string DescriptorFilePath { get; init; }
            public required string DescriptorFileRelativePath { get; init; }
            public required string TagName { get; set; }
            public required ModelVersion NewVersion { get; set; }
        }

        public void UndeployAllModels()
        {
            _profilesContainer.Refresh();
            var models = _profilesContainer.GetDeployedModelsAcrossAllProfiles(out var dirtyProfiles);
            _modelDeploymentService.UnDeployModels(models);
            _profilesContainer.SaveProfiles(dirtyProfiles, updateExternal: false);
        }

        public void RemoveModelFromProfile(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
            var model = profile.FindModel(modelName);
            if (model == null)
            {
                MessageLogger.Warning($"Model '{modelName}' not found in profile '{profileName}'");
                return;
            }

            _solutionService.RemoveProjectFromSolution(profile, model.ModelName);

            if (model.ModelType == ModelType.CompiledNuget)
            {
                var repository = profile.FindRepositoryForModel(model);
                if (repository == null)
                {
                    MessageLogger.Warning($"NuGet-backed model '{modelName}' is not mapped to a repository");
                    return;
                }

                _deployablePackageService.RemovePackage(profile, repository, model.PackageId);
                _fileService.SaveProfile(profile, updateExternal: true);
                MessageLogger.Info($"Removed NuGet package '{model.PackageId}' from repository '{repository.DisplayName}'");
                return;
            }

            var removed = false;
            var repo = profile.FindRepositoryForModel(model);
            if (repo != null && repo.Models != null)
            {
                var toRemove = repo.Models.FirstOrDefault(m => m != null && m.ModelName.SameAs(model.ModelName));
                if (toRemove != null)
                {
                    repo.Models.Remove(toRemove);
                    removed = true;

                    if (repo.Models.Count == 0)
                    {
                        profile.Repositories.Remove(repo);
                    }
                }
            }

            if (!removed && profile.StandaloneModels != null)
            {
                var standalone = profile.StandaloneModels.FirstOrDefault(m => m != null && m.ModelName.SameAs(model.ModelName));
                if (standalone != null)
                {
                    profile.StandaloneModels.Remove(standalone);
                    removed = true;
                }
            }

            if (!removed)
            {
                MessageLogger.Warning($"Model '{modelName}' could not be removed from profile '{profileName}' (not found in repo/standalone collections)");
                return;
            }

            _fileService.SaveProfile(profile, updateExternal: true);
            MessageLogger.Info($"Model '{modelName}' removed from profile '{profileName}'");
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

            if (profile.AllModels.Count == 0)
            {
                MessageLogger.Warning($"No models found in profile '{profileName}'");
                return;
            }

            MessageLogger.Info($"Models in Profile '{profileName}':");
            foreach (var model in profile.AllModels)
            {
                string status = model.IsDeployed ? "? Deployed" : "? Not Deployed";
                string gitStatus = profile.FindRepositoryForModel(model) != null ? "? Git Repo" : string.Empty;
                MessageLogger.Info($"   - {model.ModelName}\t\t - {status} - {gitStatus}");
            }
        }

        public ProfileModel LoadProfile(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);

            var updated = EnsureRepositories(profile);
            if (updated)
                _fileService.SaveProfile(profile, updateExternal: true);

            UpdateGitBranchesInProfile(profile);

            return profile;
        }

        public bool PrepareCompiledNugetModels(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);
            if (profile == null)
                return false;

            if (!_deployablePackageService.EnsureCompiledNugetModels(profile))
                return false;

            _fileService.SaveProfile(profile, updateExternal: true);
            _modelDeploymentService.RedeployModelsWithChangedSource(profileName);
            return true;
        }

        public bool BuildDeployableNugetPackage(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
            if (profile == null)
            {
                MessageLogger.Error($"❌ Profile '{profileName}' was not found");
                return false;
            }

            var model = profile.FindModel(modelName);
            if (model == null)
            {
                MessageLogger.Error($"❌ Model '{modelName}' was not found in profile '{profileName}'");
                return false;
            }

            var solutionFilePath = _solutionService.CreateBuildSolution(profile, model);
            return _deployablePackageService.BuildDeployableNugetPackage(profile, model, solutionFilePath);
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

        public List<string> GetAvailablePackageVersions(string profileName, string modelName)
        {
            if (profileName.IsNullOrEmpty() || modelName.IsNullOrEmpty())
                return new List<string>();

            var profile = _fileService.LoadProfile(profileName);
            var model = profile.FindModel(modelName);
            if (model == null || model.ModelType != ModelType.CompiledNuget || model.PackageId.IsNullOrEmpty())
                return new List<string>();

            var repository = profile.FindRepositoryForModel(model);
            if (repository == null)
                return new List<string>();

            return _deployablePackageService.GetAvailablePackageVersions(repository, model.PackageId);
        }

        private bool EnsureRepositoryAvailable(RepositoryModel? repository)
        {
            if (repository == null)
                return false;

            if (repository.GitUrl.IsNullOrEmpty())
            {
                MessageLogger.Warning($"Repo '{repository.DisplayName}' has no GitUrl. Skipping clone");
                return false;
            }

            var repoRootFolder = ResolveRepositoryRootFolder(repository);
            if (repoRootFolder.IsNullOrEmpty())
            {
                MessageLogger.Warning($"Repo '{repository.DisplayName}' has no target folder. Skipping clone");
                return false;
            }

            var hasExistingGitRepo = GitHelper.IsGitRepository(repoRootFolder);
            var shouldClone = false;

            if (!Directory.Exists(repoRootFolder))
            {
                Directory.CreateDirectory(repoRootFolder);
                shouldClone = true;
            }
            else if (!hasExistingGitRepo)
            {
                if (!Directory.EnumerateFileSystemEntries(repoRootFolder).Any())
                {
                    shouldClone = true;
                }
                else
                {
                    MessageLogger.Warning($"Repo folder '{repoRootFolder}' exists but is not a git repository. Skipping clone");
                    repository.RepoRootFolder = repoRootFolder;
                    return false;
                }
            }

            if (shouldClone)
            {
                if (!GitHelper.CloneRepository(repository.GitUrl, repoRootFolder, allowCredentialPrompt: true))
                {
                    MessageLogger.Error($"Failed to clone repository '{repository.DisplayName}'");
                    repository.RepoRootFolder = repoRootFolder;
                    return false;
                }
            }
            else
            {
                MessageLogger.Info($"Repo already exists at {repoRootFolder}. Skipping clone");
            }

            var repoRootChanged = !string.Equals(repository.RepoRootFolder, repoRootFolder, StringComparison.OrdinalIgnoreCase);
            repository.RepoRootFolder = repoRootFolder;

            foreach (var model in repository.Models ?? new List<ProfileEnvironmentModel>())
            {
                model.ModelRootFolder = repoRootFolder;
                ConfigureModelPathsAndDeployment(repository, model, repoRootFolder);
            }

            var packageModelsChanged = _deployablePackageService.EnsureCompiledNugetModels(new ProfileModel 
                { 
                    Repositories = new List<RepositoryModel> { repository } 
                }, repository);

            UpdateLastKnownGitState(repository);
            return shouldClone || repoRootChanged || packageModelsChanged;
        }

        private string ResolveRepositoryRootFolder(RepositoryModel repository)
        {
            var repoRootFolder = (repository.RepoRootFolder ?? string.Empty).Trim();
            if (!repoRootFolder.IsNullOrEmpty() && Path.IsPathRooted(repoRootFolder))
                return repoRootFolder;

            var repoFolderName = GitHelper.ExtractAzureDevOpsRepo(repository.GitUrl);
            if (repoFolderName.IsNullOrEmpty() && !repoRootFolder.IsNullOrEmpty())
                repoFolderName = Path.GetFileName(repoRootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (repoFolderName.IsNullOrEmpty())
                repoFolderName = repository.DisplayName;

            if (repoFolderName.IsNullOrEmpty())
                repoFolderName = repository.RepoId;

            if (repoFolderName.IsNullOrEmpty())
                return string.Empty;

            return Path.Combine(_defaultSourceDirectory, repoFolderName);
        }

       
        public void UpdateDeploymentStatus(string profileName)
        {
            _deploymentLedgerService.SelfHeal();

            var profile = LoadProfile(profileName);

            var updated = false;

            foreach (var model in profile.AllModels)
            {
                var shouldBeMarkedAsDeployed = _deploymentLedgerService.IsModelDeployed(model);
                if (model.IsDeployed != shouldBeMarkedAsDeployed)
                {
                    model.IsDeployed = shouldBeMarkedAsDeployed;
                    updated = true;
                }
            }

            if (updated)
            {
                _fileService.SaveProfile(profile);
                MessageLogger.Info($"✅ Deployment status updated for profile '{profileName}'");
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

                var gitUrl = detectedGitUrl ?? string.Empty;
                var repoKey = !gitUrl.IsNullOrEmpty() ? gitUrl : repoRootFolder;

                var normalizedRepoKey = RepositoryModel.NormalizeKey(repoKey);


                if (!repoMap.TryGetValue(normalizedRepoKey, out var repository))
                {
                    repository = new RepositoryModel
                    {
                        RepoRootFolder = repoRootFolder,
                        GitUrl = gitUrl.IsNullOrEmpty() ? null : gitUrl
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

            MessageLogger.Info($"✅ Repositories built: {profile.Repositories.Count}. Standalone models: {profile.StandaloneModels.Count}");
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

                if (document.RootElement.TryGetProperty("ExportProfileVersion", out _))
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
                MessageLogger.Error("UpdateRepositoryProperties: profileName is empty");
                return;
            }

            if (updatedRepository == null)
            {
                MessageLogger.Error("UpdateRepositoryProperties: updatedRepository is null");
                return;
            }

            var profile = _fileService.LoadProfile(profileName);

            var existingRepository = profile.Repositories
                .FirstOrDefault(repository => repository.RepoId.SameAs(updatedRepository.RepoId));

            if (existingRepository == null)
            {
                MessageLogger.Error($"UpdateRepositoryProperties: repo '{updatedRepository.DisplayName}' not found in profile '{profileName}'");
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

        public void UpdateModelProperties(string profileName, ProfileEnvironmentModel updatedEnvironment, ModelVersion? sourceVersion = null)
        {
            if (profileName.IsNullOrEmpty())
            {
                MessageLogger.Error("UpdateModelProperties: profileName is empty");
                return;
            }

            if (updatedEnvironment == null || updatedEnvironment.ModelName.IsNullOrEmpty())
            {
                MessageLogger.Error("UpdateModelProperties: updatedEnvironment is null or ModelName is empty");
                return;
            }

            var profile = _fileService.LoadProfile(profileName);

            var targetEnvironment = profile.AllModels
                .FirstOrDefault(model => model.ModelName.SameAs(updatedEnvironment.ModelName));

            if (targetEnvironment == null)
            {
                MessageLogger.Error($"UpdateModelProperties: model '{updatedEnvironment.ModelName}' not found in profile '{profileName}'");
                return;
            }

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

            if (targetEnvironment.ModelType == ModelType.Source && sourceVersion.HasValue)
            {
                if (!_modelVersionService.TryUpdateSourceVersion(targetEnvironment, sourceVersion.Value))
                    return;
            }

            if (targetEnvironment.ModelType == ModelType.CompiledNuget)
            {
                var repository = profile.FindRepositoryForModel(targetEnvironment);
                if (repository == null)
                {
                    MessageLogger.Error($"UpdateModelProperties: model '{updatedEnvironment.ModelName}' is not mapped to a repository");
                    return;
                }

                var targetPackageId = targetEnvironment.PackageId?.Trim() ?? string.Empty;
                var targetPackageVersion = updatedEnvironment.PackageVersion?.Trim() ?? string.Empty;
                if (!targetPackageId.IsNullOrEmpty()
                    && !targetPackageVersion.IsNullOrEmpty()
                    && !targetPackageVersion.SameAs(targetEnvironment.PackageVersion))
                {
                    targetEnvironment.PackageUrl = UpdatePackageOverviewUrl(
                        targetEnvironment.PackageUrl,
                        targetPackageId,
                        targetPackageVersion);

                    if (!_deployablePackageService.UpdatePackageVersion(profile, repository, targetPackageId, targetPackageVersion))
                        return;

                    _fileService.SaveProfile(profile, updateExternal: true);
                    _modelDeploymentService.RedeployModelsWithChangedSource(profileName);
                    MessageLogger.Info($"Model properties saved: {targetEnvironment.ModelName}");
                    return;
                }
            }

            _fileService.SaveProfile(profile, updateExternal: true);
            MessageLogger.Info($"Model properties saved: {targetEnvironment.ModelName}");
        }
    }
}
