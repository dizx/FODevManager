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

        public ModelDeploymentService(AppConfig config, FileService fileService)
        {
            _fileService = fileService;
            _deploymentBasePath = config.DeploymentBasePath;
            _defaultSourceDirectory = config.DefaultSourceDirectory;
            

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

                DeploySingleModel(profile, modelName);
                UpdateProfileFile(profileName, new ProfileEnvironmentModel { ModelName = modelName, IsDeployed = true });
            }
            finally
            {
                MessageLogger.Info("🔄 Restarting World Wide Web Publishing Service (W3SVC)...");
                ServiceHelper.StartW3SVC();
            }
        }

        public void DeployAllUndeployedModels(string profileName)
        {
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);

                bool anyUndeployed = false;

                foreach (var model in profile.Environments)
                {
                    string linkPath = Path.Combine(_deploymentBasePath, profileName);

                    if (!model.IsDeployed && !Directory.Exists(linkPath))
                    {
                        MessageLogger.Info($"🔄 Deploying model: {model.ModelName}...");
                        DeploySingleModel(profile, model.ModelName);
                        model.IsDeployed = true;
                        anyUndeployed = true;
                    }
                }

                if (!anyUndeployed)
                {
                    MessageLogger.Info($"✅ All models in profile '{profileName}' are already deployed.");
                    return;
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
        }

        public void UnDeployModel(string profileName, string modelName)
        {
            MessageLogger.Info("⏳ Stopping World Wide Web Publishing Service (W3SVC)...");
            ServiceHelper.StopW3SVC();

            try
            {
                var profile = _fileService.LoadProfile(profileName);
                var environment = GetProfileEnvironment(profile, modelName);
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

                foreach (var model in profile.Environments)
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

        private void DeploySingleModel(ProfileModel profile, string modelName)
        {
            try
            {
                var environment = GetProfileEnvironment(profile, modelName);
                string targetDir = Path.Combine(_deploymentBasePath, modelName);

                string linkPath = targetDir;
                //string sourcePath = Path.Combine(Path.GetDirectoryName(environment.ProjectFilePath), "Metadata");
                string sourcePath = environment.MetadataFolder;

                if (!Directory.Exists(sourcePath))
                {
                    MessageLogger.Error($"❌ Error: Metadata folder not found at {sourcePath}.");
                    return;
                }

                if (Directory.Exists(linkPath))
                {
                    MessageLogger.Error($"Removing existing link: {linkPath}");
                    Directory.Delete(linkPath);
                }

                Directory.CreateSymbolicLink(linkPath, sourcePath);
                MessageLogger.Info($"✅ Model '{modelName}' deployed successfully.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Error deploying model '{modelName}': {ex.Message}");
            }
        }

        private ProfileEnvironmentModel GetProfileEnvironment(ProfileModel profile, string modelName)
        {
            var environment = profile.Environments.FirstOrDefault(e => e.ModelName == modelName);

            if (environment == null)
            {
                throw new Exception($"Model '{modelName}' not found in profile '{profile.ProfileName}'.");
            }

            return environment;
        }

        private void UpdateProfileFile(string profileName, ProfileEnvironmentModel updatedEnvironment)
        {
            var profile = _fileService.LoadProfile(profileName);

            // Find the model in the profile
            var existingEnvironment = GetProfileEnvironment(profile, updatedEnvironment.ModelName);

            if (!updatedEnvironment.ModelRootFolder.IsNullOrEmpty())
                existingEnvironment.ModelRootFolder = updatedEnvironment.ModelRootFolder;

            // Update the existing model entry
            if (!updatedEnvironment.ProjectFilePath.IsNullOrEmpty())
                existingEnvironment.ProjectFilePath = updatedEnvironment.ProjectFilePath;
            existingEnvironment.IsDeployed = updatedEnvironment.IsDeployed;

            // Save the updated profile
            _fileService.SaveProfile(profile);
            MessageLogger.Info($"✅ Updated model '{updatedEnvironment.ModelName}' in profile '{profileName}'.");
        }

        public void CheckModelDeployment(string profileName, string modelName)
        {
            var env = GetProfileEnvironment(_fileService.LoadProfile(profileName), modelName);

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
            var model = GetProfileEnvironment(profile, modelName);

            if (model == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' not found in profile '{profileName}'.");
                return false;
            }

            try
            {
                string projectRootPath = FileHelper.GetModelRootFolder(model.ProjectFilePath);

                if (GitHelper.IsGitRepository(projectRootPath, out string gitRemoteUrl))
                {
                    MessageLogger.Info($"✅ Model '{modelName}' Git repository: {gitRemoteUrl}");

                    MessageLogger.Info($"✅ Model '{modelName}' Git active branch: {GitHelper.GetActiveBranch(projectRootPath)}");

                    model.GitUrl = gitRemoteUrl;
                    _fileService.SaveProfile(profile); 

                    return true;
                }
                else
                {
                    MessageLogger.Warning($"❌ Model '{modelName}' is NOT a Git repository.");
                }
            }
            catch
            {
                MessageLogger.Error($"❌ Eror getting git status for '{modelName}' in profile '{profileName}'");
            }

            return false;

        }

        public string? GetActiveGitBranch(string profileName, string modelName)
        {
            try
            {
                var model = GetProfileEnvironment(_fileService.LoadProfile(profileName), modelName);
                if (model == null)
                {
                    return "";
                }

                //string projectRootPath = FileHelper.GetModelRootFolder(model.ModelRootFolder);

                if (GitHelper.IsGitRepository(model.ModelRootFolder, out string gitRemoteUrl))
                {
                    return GitHelper.GetActiveBranch(model.ModelRootFolder);
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
            var model = GetProfileEnvironment(profile, modelName);

            if (model == null)
            {
                MessageLogger.Warning($"❌ Model '{modelName}' not found in profile '{profileName}'.");
                return;
            }

            if (GitHelper.IsGitRepository(model.ModelRootFolder))
            {
                GitHelper.OpenGitRemoteUrl(model.ModelRootFolder);
            }
            else
            {
                MessageLogger.Info($"❌ The model '{modelName}' in profile '{profileName}' is not a Git repository.");
            }
        }

        public bool ConvertInstalledModelToProjectModel(string modelName, ProfileModel profile, string? projectFolderNameOverride = null)
        {
            string sourceModelPath = Path.Combine(_deploymentBasePath, modelName);

            if (!Directory.Exists(sourceModelPath))
            {
                MessageLogger.Error($"❌ Model not found in DeploymentBasePath: {sourceModelPath}");
                return false;
            }

            string projectFolderName = projectFolderNameOverride ?? modelName;
            string projectRootPath = Path.Combine(_defaultSourceDirectory, projectFolderName);
            string metadataTargetPath = Path.Combine(projectRootPath, "Metadata", modelName);
            string projectTargetPath = Path.Combine(projectRootPath, "Project", modelName);

            try
            {
                FileHelper.EnsureDirectoryExists(metadataTargetPath);
                FileHelper.EnsureDirectoryExists(projectTargetPath);

                FileHelper.CopyDirectory(sourceModelPath, metadataTargetPath);

                string guid = Guid.NewGuid().ToString("D");
                string projectFilePath = Path.Combine(projectTargetPath, $"{modelName}.rnrproj");
                File.WriteAllText(projectFilePath, GenerateRnrprojTemplate(modelName, guid), Encoding.UTF8);

                MessageLogger.Info($"📁 Created project structure at: {projectRootPath}");

                // Check if model already exists in profile environments
                bool alreadyExists = profile.Environments.Any(env =>
                    env.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase));

                if (!alreadyExists)
                {
                    profile.Environments.Add(new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelRootFolder = projectRootPath,
                        MetadataFolder = metadataTargetPath,
                        ProjectFilePath = projectFilePath,
                        IsDeployed = false
                    });

                    _fileService.SaveProfile(profile);
                    MessageLogger.Highlight($"✅ Model '{modelName}' added to profile: {profile.ProfileName}");
                }
                else
                {
                    MessageLogger.Warning($"⚠️ Model '{modelName}' already exists in profile: {profile.ProfileName}");
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
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ Failed to convert model: {ex.Message}");
                return false;
            }
        }



        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            foreach (string dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(sourceDir, targetDir));
            }

            foreach (string file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                string destFile = file.Replace(sourceDir, targetDir);
                File.Copy(file, destFile, true);
            }
        }

        private static string GenerateRnrprojTemplate(string modelName, string guid) =>
        $@"<?xml version=""1.0"" encoding=""utf-8""?>
        <Project ToolsVersion=""14.0"" DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
          <PropertyGroup>
            <Configuration Condition="" '$(Configuration)' == '' "">Debug</Configuration>
            <Platform Condition="" '$(Platform)' == '' "">AnyCPU</Platform>
            <BuildTasksDirectory Condition="" '$(BuildTasksDirectory)' == ''"">$(MSBuildProgramFiles32)\MSBuild\Microsoft\Dynamics\AX</BuildTasksDirectory>
            <Model>{modelName}</Model>
            <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>
            <OutputPath>bin</OutputPath>
            <SchemaVersion>2.0</SchemaVersion>
            <GenerateCrossReferences>True</GenerateCrossReferences>
            <RunAppCheckerRules>False</RunAppCheckerRules>
            <LogAppcheckerDiagsAsErrors>False</LogAppcheckerDiagsAsErrors>
            <DeployOnline>False</DeployOnline>
            <ProjectGuid>{{{guid}}}</ProjectGuid>
            <Name>{modelName}</Name>
            <RootNamespace>{modelName}</RootNamespace>
          </PropertyGroup>
          <PropertyGroup Condition=""'$(Configuration)|$(Platform)' == 'Debug|AnyCPU'"">
            <Configuration>Debug</Configuration>
            <DBSyncInBuild>False</DBSyncInBuild>
            <GenerateFormAdaptors>False</GenerateFormAdaptors>
            <Company></Company>
            <Partition>initial</Partition>
            <PlatformTarget>AnyCPU</PlatformTarget>
            <DataEntityExpandParentChildRelations>False</DataEntityExpandParentChildRelations>
            <DataEntityUseLabelTextAsFieldName>False</DataEntityUseLabelTextAsFieldName>
          </PropertyGroup>
          <PropertyGroup Condition="" '$(Configuration)' == 'Debug' "">
            <DebugSymbols>true</DebugSymbols>
            <EnableUnmanagedDebugging>false</EnableUnmanagedDebugging>
          </PropertyGroup>
          <Import Project=""$(MSBuildBinPath)\Microsoft.Common.targets"" />
          <Import Project=""$(BuildTasksDirectory)\Microsoft.Dynamics.Framework.Tools.BuildTasks.17.0.targets"" />
        </Project>";


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

