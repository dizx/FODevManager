using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Shared.Models;
using FODevManager.Shared.Utils;
using FODevManager.Utils;
using System;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FODevManager.Services
{
    public class ModelDeploymentService
    {
        private readonly FileService _fileService;
        private readonly string _deploymentBasePath;
        private readonly string _defaultSourceDirectory;
        private readonly DeployablePackageService _deployablePackageService;
        private readonly IDeploymentLedgerService _deploymentLedgerService;
        private readonly ModelVersionService _modelVersionService;
        private readonly IDirectoryLinkService _directoryLinkService;
        private readonly int _modelIdBegin;
        private readonly int _modelIdEnd;

        public ModelDeploymentService(
            AppConfig config,
            FileService fileService,
            DeployablePackageService deployablePackageService,
            IDeploymentLedgerService deploymentLedgerService,
            ModelVersionService modelVersionService,
            IDirectoryLinkService directoryLinkService)
        {
            _fileService = fileService;
            _deployablePackageService = deployablePackageService;
            _deploymentLedgerService = deploymentLedgerService;
            _modelVersionService = modelVersionService;
            _directoryLinkService = directoryLinkService;
            _deploymentBasePath = config.DeploymentBasePath;
            _defaultSourceDirectory = config.DefaultSourceDirectory;
            _modelIdBegin = config.ModelIdBegin;
            _modelIdEnd = config.ModelIdEnd;
            

            FileHelper.EnsureDirectoryExists(_deploymentBasePath);
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);
        }


        public bool DeployModel(string profileName, string modelName)
        {
            
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);

                if(DeploySingleModel(profile, modelName))
                {
                    profile.FindModel(modelName)!.IsDeployed = true;
                    _fileService.SaveProfile(profile);
                    return true;
                }

                return false;
            }
            finally
            {
                ServiceHelper.StartW3SVC();
            }
        }

        public bool DeployAllUndeployedModels(string profileName)
        {
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);

                bool anyUndeployed = false;
                bool anyDeployed = false;

                foreach (var model in profile.AllModels)
                {
                    if (!IsModelActuallyDeployed(model))
                    {
                        MessageLogger.Info($"🔄 Deploying model: {model.ModelName}..");
                        
                        if(DeploySingleModel(profile, model.ModelName))
                        {
                            model.IsDeployed = true;
                            anyDeployed = true;
                        }
                        anyUndeployed = true;
                    }
                }

                if (!anyUndeployed)
                {
                    MessageLogger.Info($"✅ All models in profile '{profileName}' are already deployed");
                    return false;
                }

                if (!anyDeployed)
                    return false;

                _fileService.SaveProfile(profile);

                MessageLogger.Info($"✅ Deployment complete. Updated profile '{profileName}'");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error deploying models: {ex.Message}");
            }
            finally
            {
                ServiceHelper.StartW3SVC();
                
            }
            return true;
        }

        public bool DeployAllModelsForProfileSwitch(string profileName)
        {
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);
                var anyDeployed = false;

                foreach (var model in profile.AllModels)
                {
                    MessageLogger.Info($"🔄 Deploying model for profile switch: {model.ModelName}..");

                    if (DeploySingleModel(profile, model.ModelName, forceReplaceExistingDeployment: true))
                    {
                        model.IsDeployed = true;
                        anyDeployed = true;
                    }
                }

                if (anyDeployed)
                {
                    _fileService.SaveProfile(profile);
                    MessageLogger.Info($"✅ Switch deployment complete. Updated profile '{profileName}'");
                }

                return anyDeployed;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error deploying models for profile switch: {ex.Message}");
                return false;
            }
            finally
            {
                ServiceHelper.StartW3SVC();
            }
        }

        public bool RedeployModelsWithChangedSource(string profileName)
        {
            var profile = _fileService.LoadProfile(profileName);
            var outdatedModels = profile.AllModels
                .Where(model => model.IsDeployed && !IsModelActuallyDeployed(model) && CanAutomaticallyReplaceDeploymentPath(model))
                .ToList();

            if (outdatedModels.Count == 0)
                return false;

            ServiceHelper.StopW3SVC();

            try
            {
                var anyDeployed = false;

                foreach (var model in outdatedModels)
                {
                    MessageLogger.Info($"🔄 Updating deployment for model: {model.ModelName}..");

                    if (DeploySingleModel(profile, model.ModelName, forceReplaceExistingDeployment: true))
                    {
                        model.IsDeployed = true;
                        anyDeployed = true;
                    }
                }

                if (anyDeployed)
                {
                    _fileService.SaveProfile(profile);
                    MessageLogger.Info($"✅ Outdated deployments updated for profile '{profileName}'");
                }

                return anyDeployed;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error updating deployed models: {ex.Message}");
                return false;
            }
            finally
            {
                ServiceHelper.StartW3SVC();
            }
        }

        public void UnDeployModel(string profileName, string modelName)
        {
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);
                var model = profile.FindModel(modelName); 
                if (model == null)
                {
                    MessageLogger.Error($"❌ Model '{modelName}' not found in profile '{profileName}'");
                    return;
                }
                string linkPath = Path.Combine(_deploymentBasePath, modelName);

                if (!_directoryLinkService.Exists(linkPath))
                {
                    MessageLogger.Error($"❌ Model '{modelName}' is NOT deployed");
                    return;
                }

                try
                {
                    MessageLogger.Info($"🔄 Removing deployment link for model '{modelName}'..");
                    _directoryLinkService.Delete(linkPath, true);
                    _deploymentLedgerService.RemoveDeployment(model.ModelName);
                    MessageLogger.Highlight($"✅ Model '{modelName}' successfully undeployed");

                    // Update profile status
                    model.IsDeployed = false;
                    _fileService.SaveProfile(profile);
                }
                catch (Exception ex)
                {
                    MessageLogger.Error($"❌ Error undeploying model '{modelName}': {ex.Message}");
                }
            }
            finally
            {
                ServiceHelper.StartW3SVC();
            }
        }

        public void UnDeployModels(List<ProfileEnvironmentModel> models)
        {
            ServiceHelper.StopW3SVC();

            try
            {
                bool anyDeployed = false;

                foreach (var model in models)
                {
                    string linkPath = Path.Combine(_deploymentBasePath, model.ModelName);

                    if (_directoryLinkService.Exists(linkPath))
                    {
                        MessageLogger.Info($"🔄 Removing deployment link for model '{model.ModelName}'..");
                        _directoryLinkService.Delete(linkPath, true);
                        _deploymentLedgerService.RemoveDeployment(model.ModelName);
                        model.IsDeployed = false;
                        anyDeployed = true;
                    }
                }

                if (!anyDeployed)
                {
                    MessageLogger.Info($"✅ All models are already undeployed");
                    return;
                }

                
                MessageLogger.Info($"✅ Undeployment complete");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error undeploying models: {ex.Message}");
            }
            finally
            {
                ServiceHelper.StartW3SVC();
            }
        }

        public void UnDeployAllModels(string profileName)
        {
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);
                bool anyDeployed = false;

                foreach (var model in profile.AllModels)
                {
                    string linkPath = Path.Combine(_deploymentBasePath, model.ModelName);

                    if (_directoryLinkService.Exists(linkPath))
                    {
                        MessageLogger.Info($"🔄 Removing deployment link for model '{model.ModelName}'..");
                        _directoryLinkService.Delete(linkPath, true);
                        _deploymentLedgerService.RemoveDeployment(model.ModelName);
                        model.IsDeployed = false;
                        anyDeployed = true;
                    }
                }

                if (!anyDeployed)
                {
                    MessageLogger.Info($"✅ All models in profile '{profileName}' are already undeployed");
                    return;
                }

                _fileService.SaveProfile(profile);
                MessageLogger.Info($"✅ Undeployment complete. Updated profile '{profileName}'");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error undeploying models: {ex.Message}");
            }
            finally
            {
                ServiceHelper.StartW3SVC();
            }
        }

        public void UnDeployManagedDeployments()
        {
            ServiceHelper.StopW3SVC();

            try
            {
                _deploymentLedgerService.SelfHeal();
                var records = _deploymentLedgerService.LoadRecords()
                    .Where(record => !record.IsUnmanaged)
                    .GroupBy(record => record.ModelName, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();

                if (records.Count == 0)
                {
                    MessageLogger.Info("✅ All managed models are already undeployed");
                    return;
                }

                foreach (var record in records)
                {
                    var linkPath = Path.Combine(_deploymentBasePath, record.ModelName);

                    if (_directoryLinkService.Exists(linkPath))
                    {
                        MessageLogger.Info($"🔄 Removing deployment link for model '{record.ModelName}'..");
                        _directoryLinkService.Delete(linkPath, true);
                    }

                    _deploymentLedgerService.RemoveDeployment(record.ModelName);
                }

                MessageLogger.Info("✅ Managed deployment cleanup complete");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error undeploying managed models: {ex.Message}");
            }
            finally
            {
                ServiceHelper.StartW3SVC();
            }
        }

        private bool DeploySingleModel(ProfileModel profile, string modelName, bool forceReplaceExistingDeployment = false)
        {
            try
            {
                var model = profile.FindModel(modelName);
                if (model == null)
                {
                    MessageLogger.Error($"❌ Model '{modelName}' not found in profile '{profile.ProfileName}'");
                    return false;
                }
                string targetDir = Path.Combine(_deploymentBasePath, modelName);

                if (model.ModelType == ModelType.CompiledNuget)
                    _deployablePackageService.EnsureCompiledNugetModel(profile, model);

                string linkPath = targetDir;
                string sourcePath = GetDeploymentSourcePath(model);

                if (!Directory.Exists(sourcePath))
                {
                    MessageLogger.Error($"❌ Error: Model not found at {sourcePath}");
                    return false;
                }

                if (!forceReplaceExistingDeployment && !CanDeployFromSource(profile.ProfileName, modelName, sourcePath))
                    return false;

                if (_directoryLinkService.Exists(linkPath))
                {
                    MessageLogger.Highlight($"Removing existing link: {linkPath}");
                    _directoryLinkService.Delete(linkPath, recursive: true);
                }

                _directoryLinkService.CreateSymbolicLink(linkPath, sourcePath);

                try
                {
                    _deploymentLedgerService.RecordDeployment(CreateDeploymentRecord(profile, model, sourcePath));
                }
                catch (Exception ledgerException)
                {
                    MessageLogger.Error($"❌ Deployment ledger could not be updated for model '{modelName}': {ledgerException.Message}");

                    if (_directoryLinkService.Exists(linkPath))
                        _directoryLinkService.Delete(linkPath, recursive: true);

                    return false;
                }

                MessageLogger.Info($"✅ Model '{modelName}' deployed successfully");

                return true;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error deploying model '{modelName}': {ex.Message}");
                return false;
            }
        }

      
        public void CheckModelDeployment(string profileName, string modelName, bool updateProfile = false)
        {
            var profile = _fileService.LoadProfile(profileName);
            CheckModelDeployment(profile, modelName, updateProfile);
        }

        public void CheckModelDeployment(ProfileModel profile, string modelName, bool updateProfile = false)
        {
            var model = profile.FindModel(modelName);
            if (model == null)
            {
                MessageLogger.Error($"❌ Model '{modelName}' not found in profile '{profile.ProfileName}'");
                return;
            }

            if (model.ModelRootFolder.IsNullOrEmpty())
            {
                string modelRootPath = FileHelper.GetModelRootFolder(model.ProjectFilePath);
                if (!Directory.Exists(modelRootPath))
                {
                    MessageLogger.Error($"❌ Error: Model root folder not found at {modelRootPath}");
                    return;
                }
                model.ModelRootFolder = modelRootPath;
            }

            MessageLogger.Info($"✅ Model '{model.ModelName}' source path exists: {File.Exists(model.ProjectFilePath)}");

            string linkPath = Path.Combine(_deploymentBasePath, modelName);

            if (IsModelActuallyDeployed(model))
            {
                MessageLogger.Info($"✅ Model '{modelName}' is deployed at {linkPath}");
                
                if (model.IsDeployed == false)
                {
                    model.IsDeployed = true;
                    
                    if (updateProfile) _fileService.SaveProfile(profile);                    
                }
            }
            else
            {
                MessageLogger.Warning($"❌ Model '{modelName}' is NOT deployed");
                if(model.IsDeployed == true)
                {
                    model.IsDeployed = false;
                    if (updateProfile) _fileService.SaveProfile(profile);
                }
            }

            if (updateProfile) _fileService.SaveProfile(profile);
        }

        public bool IsModelActuallyDeployed(ProfileEnvironmentModel environmentModel)
        {
            if (environmentModel == null)
                return false;

            if (environmentModel.ModelName.IsNullOrEmpty())
                return false;

            var deploymentLinkPath = Path.Combine(_deploymentBasePath, environmentModel.ModelName);
            if (!_directoryLinkService.Exists(deploymentLinkPath))
                return false;

            var sourcePath = GetDeploymentSourcePath(environmentModel);
            if (sourcePath.IsNullOrEmpty())
                return true;

            if (!Directory.Exists(sourcePath))
                return false;

            return DeploymentPathTargetsSource(deploymentLinkPath, sourcePath);
        }

        private static string GetDeploymentSourcePath(ProfileEnvironmentModel model)
        {
            return model.ModelType == ModelType.Source ? model.MetadataFolder : model.CompiledModelFolder;
        }

        private DeployedModelRecord CreateDeploymentRecord(ProfileModel profile, ProfileEnvironmentModel model, string sourcePath)
        {
            var version = string.Empty;
            if (_modelVersionService.TryGetVersionText(model, out var resolvedVersion))
                version = resolvedVersion;

            return new DeployedModelRecord
            {
                ModelName = model.ModelName,
                ProfileName = profile.ProfileName,
                ModelType = model.ModelType,
                SourcePath = sourcePath,
                Version = version,
                PackageId = model.PackageId ?? string.Empty,
                PackageVersion = model.PackageVersion ?? string.Empty,
                DeployedAtUtc = DateTime.UtcNow,
                IsUnmanaged = false
            };
        }

        private bool CanDeployFromSource(string profileName, string modelName, string sourcePath)
        {
            _deploymentLedgerService.SelfHeal();

            if (_deploymentLedgerService.CanDeploy(profileName, modelName, sourcePath, out var blocker))
                return true;

            if (blocker == null)
                return true;

            if (blocker.IsUnmanaged)
            {
                MessageLogger.Error($"❌ Model '{modelName}' is already deployed from unmanaged path '{blocker.SourcePath}'. Undeploy or switch profile before deploying a different source");
                return false;
            }

            MessageLogger.Error($"❌ Model '{modelName}' is already deployed from profile '{blocker.ProfileName}' at '{blocker.SourcePath}'. Deploy from '{sourcePath}' is blocked");
            return false;
        }

        private bool DeploymentPathTargetsSource(string deploymentLinkPath, string sourcePath)
        {
            if (AreSameDirectoryPath(deploymentLinkPath, sourcePath))
                return true;

            try
            {
                var targetPath = _directoryLinkService.ResolveLinkTarget(deploymentLinkPath);
                if (targetPath == null)
                    return false;

                return AreSameDirectoryPath(targetPath, sourcePath);
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"⚠️ Could not resolve deployment link '{deploymentLinkPath}': {exception.Message}");
                return false;
            }
        }

        private static bool AreSameDirectoryPath(string left, string right)
        {
            if (left.IsNullOrEmpty() || right.IsNullOrEmpty())
                return false;

            var normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalizedLeft.SameAs(normalizedRight);
        }

        private bool CanAutomaticallyReplaceDeploymentPath(ProfileEnvironmentModel model)
        {
            var deploymentLinkPath = Path.Combine(_deploymentBasePath, model.ModelName);
            if (!_directoryLinkService.Exists(deploymentLinkPath))
                return true;

            try
            {
                return _directoryLinkService.ResolveLinkTarget(deploymentLinkPath) != null;
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"⚠️ Could not inspect deployment path '{deploymentLinkPath}': {exception.Message}");
                return false;
            }
        }


        public bool CheckIfGitRepository(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName); 

            var model = profile.AllModelEntries.FirstOrDefault(model => model.ModelName.SameAs(modelName));
            if (model == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' not found in profile '{profileName}'");
                return false;
            }

            var repository = model.Repository;
            if (repository == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' is not mapped to a repository in profile '{profileName}'");
                return false;
            }

            try
            {
                var repoRootFolder = (repository.RepoRootFolder ?? string.Empty).Trim();
                if (repoRootFolder.IsNullOrEmpty())
                {
                    MessageLogger.Warning($"❌ Repository root folder is missing for model '{modelName}'");
                    return false;
                }

                if (!GitHelper.IsGitRepository(repoRootFolder, out var gitRemoteUrl))
                {
                    MessageLogger.Warning($"❌ Repo for model '{modelName}' is NOT a Git repository");
                    return false;
                }

                var activeBranch = GitHelper.GetActiveBranch(repoRootFolder) ?? string.Empty;

                MessageLogger.Info($"✅ Repo Git remote: {gitRemoteUrl}");
                MessageLogger.Info($"✅ Repo active branch: {activeBranch}");

                var gitUrlChanged = !repository.GitUrl.SameAs(gitRemoteUrl);
                var branchChanged = !repository.LastKnownBranch.SameAs(activeBranch);

                if (gitUrlChanged)
                    repository.GitUrl = gitRemoteUrl;

                if (branchChanged)
                    repository.LastKnownBranch = activeBranch;

                if (gitUrlChanged || branchChanged)
                    _fileService.SaveProfile(profile, updateExternal: true);

                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"❌ Error getting git status for '{modelName}' in profile '{profileName}': {exception.Message}");
                return false;
            }
        }
        
        public string? GetActiveGitBranch(string profileName, string modelName)
        {
            try
            {
                var profile = _fileService.LoadProfile(profileName);

                if (GitHelper.IsGitRepository(profile.TryGetRepoRootFolder(modelName), out string gitRemoteUrl))
                {
                    return GitHelper.GetActiveBranch(profile.TryGetRepoRootFolder(modelName));
                }
            }
            catch
            {
                return "";
            }
            return "";
        }

        public void OpenGitRepositoryUrl(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName);
            var model = profile.FindModelEntry(modelName);

            if (model == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' not found in profile '{profileName}'");
                return;
            }

            if (GitHelper.IsGitRepository(model.Repository?.RepoRootFolder))
            {
                GitHelper.OpenGitRemoteUrl(model.Repository?.RepoRootFolder);
            }
            else
            {
                MessageLogger.Info($"❌ The model '{modelName}' in profile '{profileName}' is not a Git repository");
            }
        }


        public bool CreateModel(string modelName, ProfileModel profile)
        {
            try
            {
                string modelRoot = Path.Combine(_defaultSourceDirectory, modelName);
                string metadataFolder = Path.Combine(modelRoot, "Metadata", modelName);
                string projectFolder = Path.Combine(modelRoot, "Project", modelName);
                string metadataSubfolder = Path.Combine(metadataFolder, modelName);
                string descriptorFolder = Path.Combine(metadataFolder, "Descriptor");
                string xppMetadataFolder = Path.Combine(metadataFolder, "XppMetadata", modelName);

                // Create required folders
                FileHelper.EnsureDirectoryExists(metadataFolder);
                FileHelper.EnsureDirectoryExists(projectFolder);
                FileHelper.EnsureDirectoryExists(metadataSubfolder);
                FileHelper.EnsureDirectoryExists(descriptorFolder);
                FileHelper.EnsureDirectoryExists(xppMetadataFolder);

                // Create .rnrproj
                string projectFilePath = CreateProjectFile(modelName, projectFolder);

                // Create model descriptor XML
                int modelId = new Random().Next(_modelIdBegin, _modelIdEnd);
                string modelXml = GenerateModelXml(modelName, modelId);
                string descriptorPath = Path.Combine(descriptorFolder, $"{modelName}.xml");
                File.WriteAllText(descriptorPath, modelXml, Encoding.UTF8);

                var newModel = new ProfileEnvironmentModel
                {
                    ModelName = modelName,
                    ModelRootFolder = modelRoot,
                    ProjectFilePath = projectFilePath,
                    MetadataFolder = metadataFolder,
                    IsDeployed = false
                };

                if (TryGetGitRepositoryContext(modelRoot, out var repositoryRootFolder, out var gitRemoteUrl))
                {
                    
                    var repository = FindOrCreateRepository(profile, repositoryRootFolder, gitRemoteUrl);
                    repository.Models.Add(newModel);

                }
                else
                {
                    // Non-git → standalone model
                    profile.StandaloneModels.Add(newModel);
                }

                _fileService.SaveProfile(profile, updateExternal: true);

                MessageLogger.Highlight($"✅ Model '{modelName}' created successfully at: {modelRoot}");
                return true;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Failed to create model '{modelName}': {ex.Message}");
                return false;
            }
        }

        public bool ConvertInstalledModelToProjectModel(string modelName, ProfileModel profile, string? projectFolderNameOverride = null)
        {
            var sourceModelPath = Path.Combine(_deploymentBasePath, modelName);

            if (!Directory.Exists(sourceModelPath))
            {
                MessageLogger.Error($"❌ Model not found in DeploymentBasePath: {sourceModelPath}");
                return false;
            }

            var projectFolderName = projectFolderNameOverride ?? modelName;
            var projectRootPath = Path.Combine(_defaultSourceDirectory, projectFolderName);
            var metadataTargetPath = Path.Combine(projectRootPath, "Metadata", modelName);
            var projectTargetPath = Path.Combine(projectRootPath, "Project", modelName);

            try
            {
                FileHelper.EnsureDirectoryExists(metadataTargetPath);
                FileHelper.CopyDirectory(sourceModelPath, metadataTargetPath);

                var projectFilePath = CreateProjectFile(modelName, projectTargetPath);

                MessageLogger.Info($"📁 Created project structure at: {projectRootPath}");

                var modelAlreadyExists = profile.AllModels.Any(existing => existing.ModelName.SameAs(modelName));

                if (modelAlreadyExists)
                {
                    MessageLogger.Warning($"⚠️ Model '{modelName}' already exists in profile: {profile.ProfileName}");
                }
                else
                {
                    var newEnvironment = new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelRootFolder = projectRootPath,
                        MetadataFolder = metadataTargetPath,
                        ProjectFilePath = projectFilePath,
                        IsDeployed = false
                    };

                    // If the projectRootPath is a git repo, store it in RepositoryModel
                    if (TryGetGitRepositoryContext(projectRootPath, out var repositoryRootFolder, out var gitRemoteUrl))
                    {
                        var repository = FindOrCreateRepository(profile, repositoryRootFolder, gitRemoteUrl);
                        repository.Models.Add(newEnvironment);
                        
                    }
                    else
                    {
                        // Non-git → standalone model
                        profile.StandaloneModels.Add(newEnvironment);
                    }

                    _fileService.SaveProfile(profile, updateExternal: true);
                    MessageLogger.Highlight($"✅ Model '{modelName}' added to profile: {profile.ProfileName}");
                }

                if (Directory.Exists(sourceModelPath))
                {
                    ServiceHelper.StopW3SVC();

                    MessageLogger.Info($"🗑️ Deleting installed model folder: {sourceModelPath}");
                    Directory.Delete(sourceModelPath, recursive: true);

                    ServiceHelper.StartW3SVC();

                    MessageLogger.Highlight($"✅ Successfully deleted '{modelName}' from deployment path");
                }

                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"❌ Failed to convert model: {exception.Message}");
                return false;
            }
        }

        public void AddModelToProfileIfNotExists(string profileName, string modelName, string environmentPath, ModelType modelType)
        {
            var projectFilePath = string.Empty;
            var metaDataFolder = string.Empty;
            var compiledModelFolder = string.Empty;
            var profile = _fileService.LoadProfile(profileName);

            if (!modelName.IsNullOrEmpty() && TryBlockDuplicateModel(profile, modelName))
                return;

            var modelRootPath = FileHelper.GetModelRootFolder(environmentPath);
            if (!Directory.Exists(modelRootPath))
            {
                MessageLogger.Error($"❌ Error: Model root folder not found at {modelRootPath}");
                return;
            }

            if (modelType == ModelType.Source)
            {
                if (modelName.IsNullOrEmpty())
                {
                    modelName = DetectModelNameFromMetadata(modelRootPath);
                    if (modelName.IsNullOrEmpty())
                    {
                        throw new Exception("❌ Unable to find model name from Metadata folder");
                    }
                }

                if (TryBlockDuplicateModel(profile, modelName))
                    return;

                projectFilePath = GetProjectFilePath(modelName, modelRootPath);
                if (!File.Exists(projectFilePath))
                {
                    MessageLogger.Info($"{projectFilePath} does not exist");
                    if (Singleton<Engine>.Instance.EnvironmentType == EnvironmentType.Console)
                        MessageLogger.Info("Usage: fodev.exe -profile \"ProfileName\" -model \"ModelName\" add \"ProjectFilePath\"");
                    return;
                }

                metaDataFolder = FileHelper.GetMetadataFolder(modelName, modelRootPath);
                if (!Directory.Exists(metaDataFolder))
                {
                    MessageLogger.Error($"❌ Error: Metadata folder not found at {metaDataFolder}");
                    return;
                }
            }
            else
            {
                compiledModelFolder = environmentPath;
                projectFilePath = string.Empty;
                metaDataFolder = string.Empty;
            }

            string deploymentLinkPath = Path.Combine(_deploymentBasePath, modelName);
            bool isAlreadyDeployed = Directory.Exists(deploymentLinkPath);

            var newModel = new ProfileEnvironmentModel
            {
                ModelName = modelName,
                ModelRootFolder = modelRootPath,
                ProjectFilePath = projectFilePath,
                MetadataFolder = metaDataFolder,
                CompiledModelFolder = compiledModelFolder,
                IsDeployed = isAlreadyDeployed,
                ModelType = modelType
            };

            AddEnvironmentToProfile(profile, newModel);

            _fileService.SaveProfile(profile, updateExternal: true);
            
            if (modelType == ModelType.Source)
            {
                MessageLogger.Info($"✅ Model '{modelName}' added to profile");
            }
            else
            {
                MessageLogger.Info($"✅ Compiled Model '{modelName}' added to profile");
            }
        }

        public static bool HasModelName(ProfileModel profile, string modelName)
        {
            if (profile == null || modelName.IsNullOrEmpty())
                return false;

            return profile.AllModels.Any(model => model.ModelName.SameAs(modelName));
        }

        public static bool TryBlockDuplicateModel(ProfileModel profile, string modelName)
        {
            if (!HasModelName(profile, modelName))
                return false;

            MessageLogger.Error($"❌ Model '{modelName}' already exists in profile '{profile.ProfileName}'. Skipping add");
            return true;
        }

        private string GetProjectFilePath(string modelName, string projectFilePath)
        {
            if (projectFilePath.IsNullOrEmpty())
            {
                if (FileHelper.TryFilePath(Path.Combine(_defaultSourceDirectory, modelName, "Project", $"{modelName}.rnrproj"), out string returnPath))
                {
                    return returnPath;
                }
            }
            return FileHelper.GetProjectFilePath(modelName, projectFilePath);
        }

        private void AddEnvironmentToProfile(ProfileModel profile, ProfileEnvironmentModel model)
        {
            if (TryGetGitRepositoryContext(model.ModelRootFolder, out var repositoryRootFolder, out var gitRemoteUrl))
            {
                var repository = FindOrCreateRepository(profile, repositoryRootFolder, gitRemoteUrl);
                repository.Models.Add(model);
                return;
            }

            profile.StandaloneModels.Add(model);
        }

        private static bool TryGetGitRepositoryContext(string path, out string repositoryRootFolder, out string gitRemoteUrl)
        {
            repositoryRootFolder = string.Empty;
            gitRemoteUrl = string.Empty;

            if (path.IsNullOrEmpty())
                return false;

            string? currentPath = Directory.Exists(path) || !Path.HasExtension(path)
                ? path
                : Path.GetDirectoryName(path);

            while (!currentPath.IsNullOrEmpty())
            {
                if (GitHelper.IsGitRepository(currentPath, out var detectedRemoteUrl))
                {
                    repositoryRootFolder = currentPath;
                    gitRemoteUrl = detectedRemoteUrl ?? string.Empty;
                    return true;
                }

                var gitMarkerPath = Path.Combine(currentPath, ".git");
                if (Directory.Exists(gitMarkerPath) || File.Exists(gitMarkerPath))
                {
                    repositoryRootFolder = currentPath;
                    return true;
                }

                currentPath = Directory.GetParent(currentPath)?.FullName;
            }

            return false;
        }

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


        private RepositoryModel FindOrCreateRepository(ProfileModel profile, string repoRootFolder, string gitRemoteUrl)
        {
            var repository = !gitRemoteUrl.IsNullOrEmpty()
                ? profile.FindRepositoryByGitUrl(gitRemoteUrl)
                : null;

            repository ??= profile.FindRepositoryByRoot(repoRootFolder);

            if (repository == null)
            {
                repository = new RepositoryModel
                {
                    RepoRootFolder = repoRootFolder,
                    GitUrl = gitRemoteUrl
                };

                repository.EnsureRepoId();
                repository.EnsureDisplayName();

                profile.Repositories.Add(repository);
            }

            if (repository.RepoRootFolder.IsNullOrEmpty())
                repository.RepoRootFolder = repoRootFolder;

            if (!gitRemoteUrl.IsNullOrEmpty() && repository.GitUrl.IsNullOrEmpty())
                repository.GitUrl = gitRemoteUrl;

            repository.LastKnownBranch = GitHelper.GetActiveBranch(repoRootFolder) ?? string.Empty;

            return repository;
        }
        private string CreateProjectFile(string modelName, string projectFolder)
        {
            FileHelper.EnsureDirectoryExists(projectFolder);

            string projectGuid = Guid.NewGuid().ToString("D");
            string projectFilePath = Path.Combine(projectFolder, $"{modelName}.rnrproj");

            string content = GenerateProjectFromTemplate(modelName, projectGuid);
            File.WriteAllText(projectFilePath, content, Encoding.UTF8);

            MessageLogger.Info($"📄 Created .rnrproj file for model '{modelName}'");

            return projectFilePath;
        }

        private string GenerateProjectFromTemplate(string modelName, string guid)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Templates", "ProjectTemplate.rnrproj");
            if (!File.Exists(path)) throw new FileNotFoundException("Project template file not found");

            string template = File.ReadAllText(path);
            return template
                .Replace("{{ModelName}}", modelName)
                .Replace("{{Guid}}", guid);
        }

        private string GenerateModelXml(string modelName, int modelId)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Templates", "ModelTemplate.xml");
            if (!File.Exists(path)) throw new FileNotFoundException("Missing ModelTemplate.xml");

            string template = File.ReadAllText(path);
            return template.Replace("{modelName}", modelName).Replace("{modelId}", modelId.ToString());
        }
        public bool AssignTask(string profileName, string modelName, string periTask, string comment, bool switchBranch = true)
        {
            var profile = _fileService.LoadProfile(profileName);
            var model = profile.FindModel(modelName);
            if (model == null)
            {
                MessageLogger.Error($"❌ Model '{modelName}' not found in profile '{profile.ProfileName}'");
                return false;
            }

            if (periTask.IsNullOrEmpty())
            {
                MessageLogger.Warning("⚠️ Task cannot be empty");
                return false;
            }

            var repository = profile.FindRepositoryForModel(model);
            if (repository == null)
            {
                MessageLogger.Warning($"⚠️ Model '{model.ModelName}' is not mapped to a repository");
                return false;
            }

            repository.Task = periTask;
            repository.TaskComment = comment;

            _fileService.SaveProfile(profile);

            if (!switchBranch)
                return true;

            var branchPrefix = $"feature/task-{periTask}";
            var slug = Slugify(comment, 255, branchPrefix + "-");
            var fullBranch = slug.IsNullOrEmpty()
                ? branchPrefix
                : $"{branchPrefix}-{slug}";

            var repoPath = profile.TryGetRepoRootFolder(model);

            if (repoPath.IsNullOrEmpty() || !Directory.Exists(repoPath))
            {
                MessageLogger.Warning($"⚠️ Repo root folder not found for '{model.ModelName}'. Skipping branch switch");
                return true;
            }

            if (!GitHelper.IsGitRepository(repoPath))
            {
                MessageLogger.Warning($"⚠️ '{repoPath}' is not a Git repository. Skipping branch switch");
                return true;
            }

            var autoStashIfDirty = true;
            var stashMessage = $"FO Dev Manager: Task {periTask} ({model.ModelName})";

            if (GitHelper.ChangeBranch(repoPath, fullBranch, autoStashIfDirty, stashMessage))
            {
                repository.LastKnownBranch = GitHelper.GetActiveBranch(repoPath) ?? repository.LastKnownBranch;
                _fileService.SaveProfile(profile);
                MessageLogger.Highlight($"✅ Switched to branch '{fullBranch}'");
            }
            else
            {
                MessageLogger.Warning($"⚠️ Failed to switch to branch '{fullBranch}'");
            }

            return true;
        }

        public bool AssignTaskToRepository(string profileName, string repoId, string task, string comment, bool switchBranch = true)
        {
            var profile = _fileService.LoadProfile(profileName);

            var repository = profile.Repositories
                .FirstOrDefault(repo => repo.RepoId.SameAs(repoId));

            if (repository == null)
            {
                MessageLogger.Warning($"⚠️ Repository '{repoId}' not found in profile '{profileName}'");
                return false;
            }

            repository.Task = task;
            repository.TaskComment = comment;

            _fileService.SaveProfile(profile);

            if (!switchBranch)
                return true;

            var branchPrefix = task.IsNullOrEmpty() ? $"feature/task" : $"feature/task-{task}";
            var slug = Slugify(comment, 255, branchPrefix + "-");
            var fullBranch = slug.IsNullOrEmpty()
                ? branchPrefix
                : $"{branchPrefix}-{slug}";

            var repoPath = (repository.RepoRootFolder ?? string.Empty).Trim();
            if (repoPath.IsNullOrEmpty() || !Directory.Exists(repoPath))
            {
                MessageLogger.Warning($"⚠️ Repo root folder not found for repo '{repository.DisplayName}'. Skipping branch switch");
                return true;
            }

            if (!GitHelper.IsGitRepository(repoPath))
            {
                MessageLogger.Warning($"⚠️ '{repoPath}' is not a Git repository. Skipping branch switch");
                return true;
            }

            var stashMessage = $"FO Dev Manager: Task {task} ({repository.DisplayName})";
            var autoStashIfDirty = true;

            if (GitHelper.ChangeBranch(repoPath, fullBranch, autoStashIfDirty, stashMessage, true))
            {
                repository.LastKnownBranch = GitHelper.GetActiveBranch(repoPath) ?? repository.LastKnownBranch;
                _fileService.SaveProfile(profile);
                MessageLogger.Highlight($"✅ Switched to branch '{fullBranch}'");
            }
            else
            {
                MessageLogger.Warning($"⚠️ Failed to switch to branch '{fullBranch}'");
            }

            return true;
        }

        private static string Slugify(string input, int maxTotalLength, string branchPrefix)
        {
            if (input.IsNullOrEmpty())
                return string.Empty;

            var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();

            // Convert to URL-safe/branch-safe slug
            var slug = new string(input
                .ToLowerInvariant()
                .Replace(" ", "-")
                .Where(c => !invalidChars.Contains(c))
                .ToArray());

            int remainingLength = maxTotalLength - branchPrefix.Length;

            if (remainingLength <= 0)
                return string.Empty;

            return slug.Length > remainingLength
                ? slug.Substring(0, remainingLength)
                : slug;
        }
    }
}
