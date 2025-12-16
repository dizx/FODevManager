using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using FODevManager.Models;
using FODevManager.Utils;
using FODevManager.Messages;
using System.Reflection;
using System.Dynamic;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Text;

namespace FODevManager.Services
{
    public class ModelDeploymentService
    {
        private readonly FileService _fileService;
        private readonly string _deploymentBasePath;
        private readonly string _defaultSourceDirectory;
        private readonly int _modelIdBegin;
        private readonly int _modelIdEnd;

        public ModelDeploymentService(AppConfig config, FileService fileService)
        {
            _fileService = fileService;
            _deploymentBasePath = config.DeploymentBasePath;
            _defaultSourceDirectory = config.DefaultSourceDirectory;
            _modelIdBegin = config.ModelIdBegin;
            _modelIdEnd = config.ModelIdEnd;
            

            // Ensure directories exist
            FileHelper.EnsureDirectoryExists(_deploymentBasePath);
            FileHelper.EnsureDirectoryExists(_defaultSourceDirectory);
        }


        public void DeployModel(string profileName, string modelName)
        {
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);

                if(DeploySingleModel(profile, modelName))
                {
                    UpdateProfileFile(profileName, new ProfileEnvironmentModel { ModelName = modelName, IsDeployed = true });
                }
                
            }
            finally
            {
                MessageLogger.Info("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                ServiceHelper.StartW3SVC();
            }
        }

        public bool DeployAllUndeployedModels(string profileName)
        {
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);

                bool anyUndeployed = false;

                foreach (var model in profile.Models)
                {
                    string linkPath = Path.Combine(_deploymentBasePath, model.ModelName);

                    if (!model.IsDeployed && !Directory.Exists(linkPath))
                    {
                        MessageLogger.Info($"🔄 Deploying model: {model.ModelName}...");
                        
                        if(DeploySingleModel(profile, model.ModelName))
                            model.IsDeployed = true;
                        anyUndeployed = true;
                    }
                }

                if (!anyUndeployed)
                {
                    MessageLogger.Info($"✅ All models in profile '{profileName}' are already deployed.");
                    return false;
                }

                _fileService.SaveProfile(profile);

                MessageLogger.Info($"✅ Deployment complete. Updated profile '{profileName}'.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error deploying models: {ex.Message}");
            }
            finally
            {
                MessageLogger.Info("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                ServiceHelper.StartW3SVC();
                
            }
            return true;
        }

        public void UnDeployModel(string profileName, string modelName)
        {
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);
                var environment = GetProfileModel(profile, modelName);
                string linkPath = Path.Combine(_deploymentBasePath, modelName);

                if (!Directory.Exists(linkPath))
                {
                    MessageLogger.Error($"❌ Model '{modelName}' is NOT deployed.");
                    return;
                }

                try
                {
                    MessageLogger.Info($"🔄 Removing deployment link for model '{modelName}'...");
                    Directory.Delete(linkPath, true);
                    MessageLogger.Highlight($"✅ Model '{modelName}' successfully undeployed.");

                    // Update profile status
                    environment.IsDeployed = false;
                    _fileService.SaveProfile(profile);
                }
                catch (Exception ex)
                {
                    MessageLogger.Error($"❌ Error undeploying model '{modelName}': {ex.Message}");
                }
            }
            finally
            {
                MessageLogger.Info("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                ServiceHelper.StartW3SVC();
            }
        }

        public void UnDeployAllModels(string profileName)
        {
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);
                bool anyDeployed = false;

                foreach (var model in profile.Models)
                {
                    string linkPath = Path.Combine(_deploymentBasePath, model.ModelName);

                    if (Directory.Exists(linkPath))
                    {
                        MessageLogger.Info($"🔄 Removing deployment link for model '{model.ModelName}'...");
                        Directory.Delete(linkPath, true);
                        model.IsDeployed = false;
                        anyDeployed = true;
                    }
                }

                if (!anyDeployed)
                {
                    MessageLogger.Info($"✅ All models in profile '{profileName}' are already undeployed.");
                    return;
                }

                _fileService.SaveProfile(profile);
                MessageLogger.Info($"✅ Undeployment complete. Updated profile '{profileName}'.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error undeploying models: {ex.Message}");
            }
            finally
            {
                MessageLogger.Info("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                ServiceHelper.StartW3SVC();
            }
        }

        private bool DeploySingleModel(ProfileModel profile, string modelName)
        {
            try
            {
                var environment = GetProfileModel(profile, modelName);
                string targetDir = Path.Combine(_deploymentBasePath, modelName);

                string linkPath = targetDir;
                string sourcePath = environment.ModelType == ModelType.Compiled ? environment.CompiledModelFolder : environment.MetadataFolder;

                if (!Directory.Exists(sourcePath))
                {
                    MessageLogger.Error($"❌ Error: Model not found at {sourcePath}.");
                    return false;
                }

                if (Directory.Exists(linkPath))
                {
                    MessageLogger.Highlight($"Removing existing link: {linkPath}");
                    Directory.Delete(linkPath);
                }

                Directory.CreateSymbolicLink(linkPath, sourcePath);
                MessageLogger.Info($"✅ Model '{modelName}' deployed successfully.");

                return true;
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error deploying model '{modelName}': {ex.Message}");
                return false;
            }
        }

        private ProfileEnvironmentModel GetProfileModel(ProfileModel profile, string modelName)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (string.IsNullOrWhiteSpace(modelName))
                throw new ArgumentException("Model name is required.", nameof(modelName));

            // 1) Repository models
            var repoModel = profile.Repositories?
                .SelectMany(r => r.Models ?? new List<ProfileEnvironmentModel>())
                .FirstOrDefault(m => m.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase));

            if (repoModel != null)
                return repoModel;

            // 2) Standalone models (disk-only)
            var standalone = profile.Models?
                .FirstOrDefault(m => m.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase));

            if (standalone != null)
                return standalone;

            throw new Exception($"Model '{modelName}' not found in profile '{profile.ProfileName}'.");
        }

        private void UpdateProfileFile(string profileName, ProfileEnvironmentModel updatedEnvironment)
        {
            var profile = _fileService.LoadProfile(profileName);

            // Find the model in the profile
            var existingEnvironment = GetProfileModel(profile, updatedEnvironment.ModelName);

            if (!updatedEnvironment.ModelRootFolder.IsNullOrEmpty())
                existingEnvironment.ModelRootFolder = updatedEnvironment.ModelRootFolder;

            // Update the existing model entry
            if (!updatedEnvironment.ProjectFilePath.IsNullOrEmpty())
                existingEnvironment.ProjectFilePath = updatedEnvironment.ProjectFilePath;
            existingEnvironment.IsDeployed = updatedEnvironment.IsDeployed;

            // Save the updated profile
            _fileService.SaveProfile(profile, updateExternal: true);
            MessageLogger.Info($"✅ Updated model '{updatedEnvironment.ModelName}' in profile '{profileName}'.");
        }

        public void CheckModelDeployment(string profileName, string modelName)
        {
            var env = GetProfileModel(_fileService.LoadProfile(profileName), modelName);

            if(env.ModelRootFolder.IsNullOrEmpty())
            {
                string modelRootPath = FileHelper.GetModelRootFolder(env.ProjectFilePath);
                if (!Directory.Exists(modelRootPath))
                {
                    MessageLogger.Error($"❌ Error: Model root folder not found at {modelRootPath}.");
                    return;
                }
                env.ModelRootFolder = modelRootPath;
            }

            MessageLogger.Info($"✅ Model '{env.ModelName}' source path exists: {File.Exists(env.ProjectFilePath)}");

            string linkPath = Path.Combine(_deploymentBasePath, modelName);

            if (Directory.Exists(linkPath))
            {
                MessageLogger.Info($"✅ Model '{modelName}' is deployed at {linkPath}.");
                
                if (env.IsDeployed == false)
                {
                    env.IsDeployed = true;
                    UpdateProfileFile(profileName, env);
                }
            }
            else
            {
                MessageLogger.Warning($"❌ Model '{modelName}' is NOT deployed.");
                if(env.IsDeployed == true)
                {
                    env.IsDeployed = false;
                    UpdateProfileFile(profileName, env);
                }
            }

            UpdateProfileFile(profileName, env);
        }

        public bool IsModelActuallyDeployed(ProfileEnvironmentModel env)
        {
            if (env.ModelRootFolder.IsNullOrEmpty())
            {
                return Directory.Exists(env.ModelRootFolder);
            }
            return false;
        }

        public bool CheckIfGitRepository(string profileName, string modelName)
        {
            var profile = _fileService.LoadProfile(profileName); 

            var model = profile
                .GetAllModels()
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.ModelName, modelName, StringComparison.OrdinalIgnoreCase));

            if (model == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' not found in profile '{profileName}'.");
                return false;
            }

            var repository = profile.FindRepositoryForModel(model);
            if (repository == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' is not mapped to a repository in profile '{profileName}'.");
                return false;
            }

            try
            {
                var repoRootFolder = (repository.RepoRootFolder ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(repoRootFolder))
                {
                    MessageLogger.Warning($"❌ Repository root folder is missing for model '{modelName}'.");
                    return false;
                }

                if (!GitHelper.IsGitRepository(repoRootFolder, out var gitRemoteUrl))
                {
                    MessageLogger.Warning($"❌ Repo for model '{modelName}' is NOT a Git repository.");
                    return false;
                }

                var activeBranch = GitHelper.GetActiveBranch(repoRootFolder) ?? string.Empty;

                MessageLogger.Info($"✅ Repo Git remote: {gitRemoteUrl}");
                MessageLogger.Info($"✅ Repo active branch: {activeBranch}");

                var gitUrlChanged = !string.Equals(repository.GitUrl, gitRemoteUrl, StringComparison.OrdinalIgnoreCase);
                var branchChanged = !string.Equals(repository.LastKnownBranch, activeBranch, StringComparison.OrdinalIgnoreCase);

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
            var model = GetProfileModel(profile, modelName);

            if (model == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' not found in profile '{profileName}'.");
                return;
            }

            if (GitHelper.IsGitRepository(profile.TryGetRepoRootFolder(model)))
            {
                GitHelper.OpenGitRemoteUrl(profile.TryGetRepoRootFolder(model));
            }
            else
            {
                MessageLogger.Info($"❌ The model '{modelName}' in profile '{profileName}' is not a Git repository.");
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

                // Register in profile
                if (!profile.Models.Any(e => e.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase)))
                {
                    profile.Models.Add(new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelRootFolder = modelRoot,
                        ProjectFilePath = projectFilePath,
                        MetadataFolder = metadataFolder,
                        IsDeployed = false
                    });

                    _fileService.SaveProfile(profile, updateExternal: true);
                }

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

                var modelAlreadyExists = profile
                    .GetAllModels()
                    .Any(existing => string.Equals(existing.ModelName, modelName, StringComparison.OrdinalIgnoreCase));

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
                    if (GitHelper.IsGitRepository(projectRootPath, out var gitRemoteUrl))
                    {
                        var repositoryRootFolder = projectRootPath;

                        profile.Repositories ??= new List<RepositoryModel>();

                        // RepoKey = GitUrl if present, otherwise RepoRootFolder (handled internally)
                        var repository = profile.Repositories
                            .FirstOrDefault(r =>
                                string.Equals(r.GetRepoKey(), RepositoryModel.NormalizeKey(gitRemoteUrl), StringComparison.OrdinalIgnoreCase));

                        if (repository == null)
                        {
                            repository = new RepositoryModel
                            {
                                RepoRootFolder = repositoryRootFolder,
                                GitUrl = gitRemoteUrl
                            };

                            // Centralized, sexy, deterministic
                            repository.EnsureRepoId();
                            repository.EnsureDisplayName();

                            profile.Repositories.Add(repository);
                        }

                        repository.Models ??= new List<ProfileEnvironmentModel>();
                        repository.Models.Add(newEnvironment);

                        repository.LastKnownBranch = GitHelper.GetActiveBranch(repositoryRootFolder) ?? string.Empty;
                    }
                    else
                    {
                        // Non-git → standalone model
                        profile.Models ??= new List<ProfileEnvironmentModel>();
                        profile.Models.Add(newEnvironment);
                    }

                    _fileService.SaveProfile(profile, updateExternal: true);
                    MessageLogger.Highlight($"✅ Model '{modelName}' added to profile: {profile.ProfileName}");
                }

                if (Directory.Exists(sourceModelPath))
                {
                    MessageLogger.Info("🛑 Stopping W3SVC to release locks on AOS folder...");
                    ServiceHelper.StopW3SVC();

                    MessageLogger.Info($"🗑️ Deleting installed model folder: {sourceModelPath}");
                    Directory.Delete(sourceModelPath, recursive: true);

                    ServiceHelper.StartW3SVC();

                    MessageLogger.Highlight($"✅ Successfully deleted '{modelName}' from deployment path.");
                }

                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"❌ Failed to convert model: {exception.Message}");
                return false;
            }
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
            if (!File.Exists(path)) throw new FileNotFoundException("Project template file not found.");

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
        public bool AssignPeriTask(string profileName, string modelName, string periTask, string comment, bool switchBranch = true)
        {
            var profile = _fileService.LoadProfile(profileName);
            var model = GetProfileModel(profile, modelName);

            if (string.IsNullOrWhiteSpace(periTask))
            {
                MessageLogger.Warning("⚠️ PeriTask cannot be empty.");
                return false;
            }

            var repository = profile.FindRepositoryForModel(model);
            if (repository == null)
            {
                MessageLogger.Warning($"⚠️ Model '{model.ModelName}' is not mapped to a repository.");
                return false;
            }

            repository.PeriTask = periTask;
            repository.PeriTaskComment = comment;

            _fileService.SaveProfile(profile);

            if (!switchBranch)
                return true;

            var branchPrefix = $"feature/task-{periTask}";
            var slug = Slugify(comment, 255, branchPrefix + "-");
            var fullBranch = string.IsNullOrWhiteSpace(slug)
                ? branchPrefix
                : $"{branchPrefix}-{slug}";

            var repoPath = profile.TryGetRepoRootFolder(model);

            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            {
                MessageLogger.Warning($"⚠️ Repo root folder not found for '{model.ModelName}'. Skipping branch switch.");
                return true;
            }

            if (!GitHelper.IsGitRepository(repoPath))
            {
                MessageLogger.Warning($"⚠️ '{repoPath}' is not a Git repository. Skipping branch switch.");
                return true;
            }

            var autoStashIfDirty = true;
            var stashMessage = $"FO Dev Manager: PeriTask {periTask} ({model.ModelName})";

            if (GitHelper.ChangeBranch(repoPath, fullBranch, autoStashIfDirty, stashMessage))
            {
                repository.LastKnownBranch = GitHelper.GetActiveBranch(repoPath) ?? repository.LastKnownBranch;
                _fileService.SaveProfile(profile);
                MessageLogger.Highlight($"✅ Switched to branch '{fullBranch}'.");
            }
            else
            {
                MessageLogger.Warning($"⚠️ Failed to switch to branch '{fullBranch}'.");
            }

            return true;
        }

        public bool AssignPeriTaskToRepository(string profileName, string repoId, string periTask, string comment, bool switchBranch = true)
        {
            var profile = _fileService.LoadProfile(profileName);

            if (string.IsNullOrWhiteSpace(periTask))
            {
                MessageLogger.Warning("⚠️ PeriTask cannot be empty.");
                return false;
            }

            var repository = profile.Repositories
                .FirstOrDefault(repo => string.Equals(repo.RepoId, repoId, StringComparison.OrdinalIgnoreCase));

            if (repository == null)
            {
                MessageLogger.Warning($"⚠️ Repository '{repoId}' not found in profile '{profileName}'.");
                return false;
            }

            repository.PeriTask = periTask;
            repository.PeriTaskComment = comment;

            _fileService.SaveProfile(profile);

            if (!switchBranch)
                return true;

            var branchPrefix = $"feature/task-{periTask}";
            var slug = Slugify(comment, 255, branchPrefix + "-");
            var fullBranch = string.IsNullOrWhiteSpace(slug)
                ? branchPrefix
                : $"{branchPrefix}-{slug}";

            var repoPath = (repository.RepoRootFolder ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
            {
                MessageLogger.Warning($"⚠️ Repo root folder not found for repo '{repository.DisplayName}'. Skipping branch switch.");
                return true;
            }

            if (!GitHelper.IsGitRepository(repoPath))
            {
                MessageLogger.Warning($"⚠️ '{repoPath}' is not a Git repository. Skipping branch switch.");
                return true;
            }

            var stashMessage = $"FO Dev Manager: PeriTask {periTask} ({repository.DisplayName})";
            var autoStashIfDirty = true;

            if (GitHelper.ChangeBranch(repoPath, fullBranch, autoStashIfDirty, stashMessage, true))
            {
                repository.LastKnownBranch = GitHelper.GetActiveBranch(repoPath) ?? repository.LastKnownBranch;
                _fileService.SaveProfile(profile);
                MessageLogger.Highlight($"✅ Switched to branch '{fullBranch}'.");
            }
            else
            {
                MessageLogger.Warning($"⚠️ Failed to switch to branch '{fullBranch}'.");
            }

            return true;
        }



        private static string Slugify(string input, int maxTotalLength, string branchPrefix)
        {
            if (string.IsNullOrWhiteSpace(input))
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

