using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Shared.Utils;
using FODevManager.Utils;
using Microsoft.Win32;
using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;

namespace FODevManager.Services
{
    public sealed class DeployablePackageService
    {
        private const string CompilerPackageId = "Microsoft.Dynamics.AX.Platform.CompilerPackage";
        private const string PlatformBuildPackageId = "Microsoft.Dynamics.AX.Platform.DevALM.BuildXpp";
        private const string Application1BuildPackageId = "Microsoft.Dynamics.AX.Application1.DevALM.BuildXpp";
        private const string Application2BuildPackageId = "Microsoft.Dynamics.AX.Application2.DevALM.BuildXpp";
        private const string ApplicationSuiteBuildPackageId = "Microsoft.Dynamics.AX.ApplicationSuite.DevALM.BuildXpp";
        private const string NugetUtilDefaultPath = @"C:\nuget\nugetutil.exe";
        private const int NugetDownloadRetryCount = 5;
        private static readonly string[] FoBuildPackageIds =
        [
            CompilerPackageId,
            PlatformBuildPackageId,
            Application1BuildPackageId,
            Application2BuildPackageId,
            ApplicationSuiteBuildPackageId
        ];
        private readonly AppConfig _config;
        private readonly string _deployablePackagesRoot;
        private readonly string _deploymentBasePath;

        public DeployablePackageService(AppConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _deployablePackagesRoot = _config.DeployablePackages;
            _deploymentBasePath = _config.DeploymentBasePath;

            if (!_deployablePackagesRoot.IsNullOrEmpty())
            {
                FileHelper.EnsureDirectoryExists(_deployablePackagesRoot);
            }
        }

        public bool EnsureCompiledNugetModels(ProfileModel profile)
        {
            if (profile?.Repositories == null || profile.Repositories.Count == 0)
                return false;

            var anyUpdated = false;

            foreach (var repository in profile.Repositories)
            {
                if (EnsureCompiledNugetModels(profile, repository))
                    anyUpdated = true;
            }

            return anyUpdated;
        }

        public bool EnsureCompiledNugetModels(ProfileModel profile, RepositoryModel repository)
        {
            if (profile == null || repository == null)
                return false;

            var hasNugetModels = repository.Models != null && repository.Models.Any(model => model.ModelType == ModelType.CompiledNuget);
            if (!hasNugetModels)
            {
                if (!TryGetIsvConfigPath(repository, createIfMissing: false, out var isvConfigPath)
                    || isvConfigPath.IsNullOrEmpty()
                    || !File.Exists(isvConfigPath))
                {
                    return false;
                }

                var configuredPackages = LoadIsvPackageReferences(isvConfigPath);
                if (configuredPackages.Count == 0)
                    return false;
            }

            if (_deployablePackagesRoot.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ DeployablePackages is not configured. Cannot prepare Compiled Nuget models");
                return false;
            }

            if (!TryGetPackageContext(repository, out var packageContext))
            {
                MessageLogger.Error($"❌ Nuget is not configured for repository '{repository.DisplayName}'. Cannot prepare compiled NuGet models");
                return false;
            }

            var packageReferences = packageContext.Packages
                .Where(packageReference => !HasNonNugetModelName(profile, packageReference.Id))
                .ToList();

            if (packageReferences.Count == 0)
                return SyncRepositoryNugetModels(profile, repository, []);

            var extractedPackages = new List<PackageReference>();
            foreach (var packageReference in packageReferences)
            {
                if (EnsurePackageExtracted(packageReference, packageContext))
                    extractedPackages.Add(packageReference);
            }

            if (extractedPackages.Count == 0)
            {
                MessageLogger.Warning($"⚠️ No Compiled Nuget packages could be prepared for repository '{repository.DisplayName}'");
                return false;
            }

            return SyncRepositoryNugetModels(profile, repository, extractedPackages);
        }

        public bool EnsureCompiledNugetModel(ProfileModel profile, ProfileEnvironmentModel model)
        {
            if (profile == null || model == null || model.ModelType != ModelType.CompiledNuget)
                return false;

            var repository = profile.FindRepositoryForModel(model);
            if (repository == null)
            {
                MessageLogger.Warning($"⚠️ Compiled NuGet model '{model.ModelName}' is not mapped to a repository. Skipping package preparation");
                return false;
            }

            return EnsureCompiledNugetModels(profile, repository);
        }

        public bool BuildDeployableNugetPackage(ProfileModel profile, ProfileEnvironmentModel model, string solutionFilePath)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (solutionFilePath.IsNullOrEmpty() || !File.Exists(solutionFilePath))
            {
                MessageLogger.Error($"❌ Solution file not found: {solutionFilePath}");
                return false;
            }

            if (model.ModelType != ModelType.Source)
            {
                MessageLogger.Error($"❌ Only source models can be packaged. '{model.ModelName}' is {model.ModelType}");
                return false;
            }

            if (model.ModelName.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Model name is required to build a Compiled Nuget");
                return false;
            }

            if (model.ProjectFilePath.IsNullOrEmpty() || !File.Exists(model.ProjectFilePath))
            {
                MessageLogger.Error($"❌ Project file not found for model '{model.ModelName}': {model.ProjectFilePath}");
                return false;
            }

            if (_deployablePackagesRoot.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ DeployablePackages is not configured. Cannot build Compiled Nuget artifacts");
                return false;
            }

            var appNugetConfigPath = ResolveApplicationNugetConfigPath();
            if (appNugetConfigPath.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Could not locate FO Dev Manager nuget.config in the app directory");
                return false;
            }

            if (!ValidateBuildPackageSettings(appNugetConfigPath))
                return false;

            var buildPackagesRoot = ResolvePreferredBuildPackagesRoot();
            FileHelper.EnsureDirectoryExists(buildPackagesRoot);

            MessageLogger.LogOnly($"Build package cache root: {buildPackagesRoot}");

            if (!TryEnsureFoBuildPackages(buildPackagesRoot, appNugetConfigPath, out var packageRoots))
                return false;

            var artifactsRoot = GetModelArtifactsRoot(profile, model);
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var runRoot = Path.Combine(artifactsRoot, "BuildPackages", model.ModelName, timestamp);
            var buildOutputRoot = Path.Combine(runRoot, "BuildOutput");
            var nugetOutputRoot = Path.Combine(runRoot, "NuGet");

            EnsureCleanDirectory(runRoot);
            Directory.CreateDirectory(buildOutputRoot);
            Directory.CreateDirectory(nugetOutputRoot);

            if (!TryResolveReferencedCompiledNugetModels(profile, model, out var compiledNugetModels))
                return false;

            var compiledNugetReferenceFolders = new List<string>();
            if (compiledNugetModels.Count > 0)
                compiledNugetReferenceFolders.Add(PrepareCompiledNugetReferenceRoot(runRoot, compiledNugetModels));

            compiledNugetReferenceFolders.AddRange(ResolveProjectReferenceOutputFolders(model.ProjectFilePath));

            var buildMetadataDirectory = ResolveBuildMetadataDirectory(model, _deploymentBasePath);
            if (!TryBuildMsBuildContext(packageRoots, compiledNugetReferenceFolders, buildMetadataDirectory, out var buildContext))
                return false;

            MessageLogger.Highlight($"📦 Building package for '{model.ModelName}'.");

            if (!RunMsBuild(solutionFilePath, model.ProjectFilePath, model.ModelName, buildContext, buildOutputRoot, runRoot))
                return false;

            if (!TryResolveBuiltPayloadRoot(buildOutputRoot, model.ModelName, out var payloadRoot))
            {
                MessageLogger.Error($"❌ Could not find compiled payload for model '{model.ModelName}' under '{buildOutputRoot}'");
                return false;
            }

            var sourceFileCount = Directory.GetFiles(payloadRoot, "*", SearchOption.AllDirectories).Length;
            if (sourceFileCount == 0)
            {
                MessageLogger.Error($"❌ Compiled payload root '{payloadRoot}' contains no files");
                return false;
            }

            var capturedPayloadRoot = IsPathWithinDirectory(payloadRoot, buildOutputRoot)
                ? payloadRoot
                : Path.Combine(buildOutputRoot, model.ModelName);

            if (!AreSameDirectoryPath(payloadRoot, capturedPayloadRoot))
            {
                EnsureCleanDirectory(capturedPayloadRoot);
                FileHelper.CopyDirectory(payloadRoot, capturedPayloadRoot);
            }

            var capturedFileCount = Directory.GetFiles(capturedPayloadRoot, "*", SearchOption.AllDirectories).Length;
            if (capturedFileCount == 0)
            {
                MessageLogger.Error($"❌ Build output copy created '{capturedPayloadRoot}' but no files were copied from '{payloadRoot}'");
                return false;
            }

            MessageLogger.Highlight($"✅ Build model '{model.ModelName}' is completed");

            if (!RunNugetUtilFopack(capturedPayloadRoot, nugetOutputRoot, out var packagePath, out var nuspecPath))
                return false;

            if (!CopyNuspecToModelRoot(model, nuspecPath))
                return false;

            return true;
        }

        public bool AddOrUpdatePackageFromUrl(ProfileModel profile, RepositoryModel repository, string packageUrl)
        {
            if (profile == null || repository == null || packageUrl.IsNullOrEmpty())
                return false;

            if (!TryParseNugetPackageUrl(packageUrl, out var packageId, out var packageVersion))
            {
                MessageLogger.Error($"❌ Unsupported package URL '{packageUrl}'");
                return false;
            }

            if (!TryGetIsvConfigPath(repository, createIfMissing: true, out var isvConfigPath))
            {
                MessageLogger.Error($"❌ Could not locate Build\\isv.config for repository '{repository.DisplayName}'");
                return false;
            }

            if (!ValidatePackageVersionExists(repository, packageId, packageVersion))
                return false;

            UpsertPackageReference(isvConfigPath, packageId, packageVersion);
            var updated = EnsureCompiledNugetModels(profile, repository);
            ApplyPackageUrl(repository, packageId, packageUrl);
            return updated;
        }

        public bool RemovePackage(ProfileModel profile, RepositoryModel repository, string packageId)
        {
            if (profile == null || repository == null || packageId.IsNullOrEmpty())
                return false;

            if (!TryGetIsvConfigPath(repository, createIfMissing: false, out var isvConfigPath))
                return false;

            var removed = RemovePackageReference(isvConfigPath, packageId.Trim());
            var synced = EnsureCompiledNugetModels(profile, repository);

            var staleModels = (repository.Models ?? new List<ProfileEnvironmentModel>())
                .Where(model => model.ModelType == ModelType.CompiledNuget && model.PackageId.SameAs(packageId))
                .ToList();

            foreach (var staleModel in staleModels)
            {
                repository.Models.Remove(staleModel);
            }

            return removed || synced || staleModels.Count > 0;
        }

        public bool UpdatePackageFromUrl(ProfileModel profile, RepositoryModel repository, string existingPackageId, string packageUrl)
        {
            if (profile == null || repository == null)
                return false;

            if (packageUrl.IsNullOrEmpty())
                return RemovePackage(profile, repository, existingPackageId);

            if (!TryParseNugetPackageUrl(packageUrl, out var newPackageId, out var newPackageVersion))
            {
                MessageLogger.Error($"❌ Unsupported package URL '{packageUrl}'");
                return false;
            }

            if (!TryGetIsvConfigPath(repository, createIfMissing: true, out var isvConfigPath))
            {
                MessageLogger.Error($"❌ Could not locate Build\\isv.config for repository '{repository.DisplayName}'");
                return false;
            }

            if (!ValidatePackageVersionExists(repository, newPackageId, newPackageVersion))
                return false;

            if (!existingPackageId.IsNullOrEmpty() && !existingPackageId.SameAs(newPackageId))
            {
                RemovePackageReference(isvConfigPath, existingPackageId);
            }

            UpsertPackageReference(isvConfigPath, newPackageId, newPackageVersion);
            var updated = EnsureCompiledNugetModels(profile, repository);
            ApplyPackageUrl(repository, newPackageId, packageUrl);
            return updated;
        }

        public bool UpdatePackageVersion(ProfileModel profile, RepositoryModel repository, string packageId, string packageVersion)
        {
            if (profile == null || repository == null || packageId.IsNullOrEmpty() || packageVersion.IsNullOrEmpty())
                return false;

            if (!TryGetIsvConfigPath(repository, createIfMissing: true, out var isvConfigPath))
            {
                MessageLogger.Error($"❌ Could not locate Build\\isv.config for repository '{repository.DisplayName}'");
                return false;
            }

            if (!ValidatePackageVersionExists(repository, packageId, packageVersion))
                return false;

            UpsertPackageReference(isvConfigPath, packageId, packageVersion);
            return EnsureCompiledNugetModels(profile, repository);
        }

        public List<string> GetAvailablePackageVersions(RepositoryModel repository, string packageId)
        {
            if (repository == null || packageId.IsNullOrEmpty())
                return new List<string>();

            if (!TryResolvePackageNugetConfigPath(repository, out var nugetConfigPath))
                return new List<string>();

            if (!TryGetAvailablePackageVersions(
                    repository.RepoRootFolder ?? Directory.GetCurrentDirectory(),
                    nugetConfigPath,
                    packageId,
                    out var versions,
                    out var errorMessage))
            {
                MessageLogger.Warning($"⚠️ Could not load available versions for package '{packageId}'. {errorMessage}");
                return new List<string>();
            }

            return versions
                .OrderByDescending(version => version, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool TryGetPackageContext(RepositoryModel repository, out PackageContext context)
        {
            context = null!;

            var repositoryRoot = (repository.RepoRootFolder ?? string.Empty).Trim();
            if (repositoryRoot.IsNullOrEmpty() || !Directory.Exists(repositoryRoot))
            {
                MessageLogger.LogOnly($"Repository root folder is missing for '{repository.DisplayName}'. Cannot prepare compiled NuGet models");
                return false;
            }

            if (!TryResolvePackageNugetConfigPath(repository, out var nugetConfigPath))
                return false;

            var isvConfigPath = FindRepositoryConfigFile(repositoryRoot, "isv.config");
            if (isvConfigPath.IsNullOrEmpty())
            {
                MessageLogger.LogOnly($"No isv.config found under repository '{repository.DisplayName}'");
                return false;
            }

            var packages = LoadIsvPackageReferences(isvConfigPath);
            if (packages.Count == 0)
            {
                MessageLogger.LogOnly($"No package entries found in Build\\isv.config for repository '{repository.DisplayName}'");
                return false;
            }

            context = new PackageContext(repositoryRoot, isvConfigPath, nugetConfigPath, packages);

            if (!ValidatePackageSettings(repository, context))
                return false;

            return true;
        }

        private bool SyncRepositoryNugetModels(ProfileModel profile, RepositoryModel repository, IReadOnlyCollection<PackageReference> extractedPackages)
        {
            var descriptors = new List<ResolvedModelDescriptor>();

            foreach (var packageReference in extractedPackages)
            {
                var extractedRoot = GetExtractedPackageRoot(packageReference);
                foreach (var modelFolder in GetExtractedModelFolders(extractedRoot))
                {
                    var modelName = ResolveCompiledModelName(modelFolder, packageReference);
                    descriptors.Add(new ResolvedModelDescriptor(packageReference, modelName, modelFolder));
                }
            }

            repository.Models ??= new List<ProfileEnvironmentModel>();

            var blockedDescriptors = descriptors
                .Where(descriptor => HasNonNugetModelName(profile, descriptor.ModelName))
                .ToList();

            foreach (var blockedDescriptor in blockedDescriptors)
            {
                ModelDeploymentService.TryBlockDuplicateModel(profile, blockedDescriptor.ModelName);
            }

            if (blockedDescriptors.Count > 0)
            {
                descriptors = descriptors
                    .Where(descriptor => !blockedDescriptors.Contains(descriptor))
                    .ToList();
            }

            var existingNugetModels = repository.Models
                .Where(model => model.ModelType == ModelType.CompiledNuget)
                .ToList();

            var updated = false;
            var deploymentBasePath = _deploymentBasePath;

            foreach (var descriptor in descriptors)
            {
                var existingModel = existingNugetModels.FirstOrDefault(model => model.ModelName.SameAs(descriptor.ModelName));
                if (existingModel == null)
                {
                    existingModel = new ProfileEnvironmentModel
                    {
                        ModelName = descriptor.ModelName,
                        ModelType = ModelType.CompiledNuget,
                        IsDeployed = Directory.Exists(Path.Combine(deploymentBasePath, descriptor.ModelName))
                    };
                    repository.Models.Add(existingModel);
                    existingNugetModels.Add(existingModel);
                    updated = true;
                }

                updated |= ApplyResolvedModel(existingModel, repository, descriptor);
            }

            var descriptorNames = descriptors
                .Select(descriptor => descriptor.ModelName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var staleModel in existingNugetModels.Where(model => !descriptorNames.Contains(model.ModelName)).ToList())
            {
                updated |= TryRemoveLegacyVersionedDeploymentLink(staleModel, descriptors);
                repository.Models.Remove(staleModel);
                updated = true;
            }

            return updated;
        }

        private bool TryRemoveLegacyVersionedDeploymentLink(ProfileEnvironmentModel staleModel, IReadOnlyCollection<ResolvedModelDescriptor> descriptors)
        {
            if (!IsLegacyVersionedNugetModel(staleModel, descriptors))
                return false;

            var deploymentLinkPath = Path.Combine(_deploymentBasePath, staleModel.ModelName);
            if (!Directory.Exists(deploymentLinkPath))
                return false;

            try
            {
                var attributes = File.GetAttributes(deploymentLinkPath);
                if (!attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    MessageLogger.Warning($"⚠️ Legacy deployment path '{deploymentLinkPath}' is a real directory. Leaving it in place");
                    return false;
                }

                Directory.Delete(deploymentLinkPath);
                MessageLogger.Info($"🧹 Removed legacy versioned NuGet deployment link '{deploymentLinkPath}'");
                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"⚠️ Could not remove legacy deployment link '{deploymentLinkPath}': {exception.Message}");
                return false;
            }
        }

        private static bool IsLegacyVersionedNugetModel(ProfileEnvironmentModel staleModel, IReadOnlyCollection<ResolvedModelDescriptor> descriptors)
        {
            return descriptors.Any(descriptor =>
                descriptor.PackageReference.Id.SameAs(staleModel.PackageId)
                && descriptor.PackageReference.Version.SameAs(staleModel.PackageVersion)
                && staleModel.ModelName.SameAs($"{descriptor.PackageReference.Id}-{descriptor.PackageReference.Version}"));
        }

        private static bool ApplyResolvedModel(ProfileEnvironmentModel model, RepositoryModel repository, ResolvedModelDescriptor descriptor)
        {
            var updated = false;

            updated |= SetIfDifferent(model, nameof(model.ModelRootFolder), repository.RepoRootFolder, value => model.ModelRootFolder = value);
            updated |= SetIfDifferent(model, nameof(model.CompiledModelFolder), descriptor.ModelFolder, value => model.CompiledModelFolder = value);
            updated |= SetIfDifferent(model, nameof(model.PackageId), descriptor.PackageReference.Id, value => model.PackageId = value);
            updated |= SetIfDifferent(model, nameof(model.PackageVersion), descriptor.PackageReference.Version, value => model.PackageVersion = value);
            updated |= SetIfDifferent(model, nameof(model.ProjectFilePath), string.Empty, value => model.ProjectFilePath = value);
            updated |= SetIfDifferent(model, nameof(model.MetadataFolder), string.Empty, value => model.MetadataFolder = value);

            if (model.ModelType != ModelType.CompiledNuget)
            {
                model.ModelType = ModelType.CompiledNuget;
                updated = true;
            }

            return updated;
        }

        private static void ApplyPackageUrl(RepositoryModel repository, string packageId, string packageUrl)
        {
            if (repository?.Models == null || packageId.IsNullOrEmpty() || packageUrl.IsNullOrEmpty())
                return;

            foreach (var model in repository.Models.Where(model =>
                         model.ModelType == ModelType.CompiledNuget &&
                         model.PackageId.SameAs(packageId)))
            {
                model.PackageUrl = packageUrl.Trim();
            }
        }

        private static bool SetIfDifferent(ProfileEnvironmentModel model, string _, string newValue, Action<string> setter)
        {
            var currentValue = _ switch
            {
                nameof(model.ModelRootFolder) => model.ModelRootFolder ?? string.Empty,
                nameof(model.CompiledModelFolder) => model.CompiledModelFolder ?? string.Empty,
                nameof(model.PackageId) => model.PackageId ?? string.Empty,
                nameof(model.PackageVersion) => model.PackageVersion ?? string.Empty,
                nameof(model.ProjectFilePath) => model.ProjectFilePath ?? string.Empty,
                nameof(model.MetadataFolder) => model.MetadataFolder ?? string.Empty,
                _ => string.Empty
            };

            if (currentValue.SameAs(newValue ?? string.Empty))
                return false;

            setter(newValue ?? string.Empty);
            return true;
        }

        private static IEnumerable<string> GetExtractedModelFolders(string extractedRoot)
        {
            if (!Directory.Exists(extractedRoot))
                return Enumerable.Empty<string>();

            return Directory.GetFiles(extractedRoot, "*.xref", SearchOption.AllDirectories)
                .Where(path => !IsUnderDownloadFolder(extractedRoot, path))
                .Select(Path.GetDirectoryName)
                .Where(path => !path.IsNullOrEmpty())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)!;
        }

        private static string ResolveCompiledModelName(string modelFolder, PackageReference packageReference)
        {
            if (Directory.Exists(modelFolder))
            {
                var xrefModelName = Directory
                    .GetFiles(modelFolder, "*.xref", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !name.IsNullOrEmpty())
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (!xrefModelName.IsNullOrEmpty())
                    return xrefModelName!;

                var binFolder = Path.Combine(modelFolder, "bin");
                if (Directory.Exists(binFolder))
                {
                    var compiledAssemblyName = Directory
                        .GetFiles(binFolder, "Dynamics.AX.*.dll", SearchOption.TopDirectoryOnly)
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(name => !name.IsNullOrEmpty() && name.StartsWith("Dynamics.AX.", StringComparison.OrdinalIgnoreCase))
                        .Select(name => name!.Substring("Dynamics.AX.".Length))
                        .Where(name => !name.IsNullOrEmpty())
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();

                    if (!compiledAssemblyName.IsNullOrEmpty())
                        return compiledAssemblyName!;
                }
            }

            var folderName = Path.GetFileName(modelFolder) ?? string.Empty;
            var packageVersionSuffix = $"-{packageReference.Version}";
            if (!folderName.IsNullOrEmpty()
                && folderName.EndsWith(packageVersionSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return folderName[..^packageVersionSuffix.Length];
            }

            return folderName;
        }

        private bool EnsurePackageExtracted(PackageReference packageReference, PackageContext context)
        {
            var extractedRoot = GetExtractedPackageRoot(packageReference);
            FileHelper.EnsureDirectoryExists(extractedRoot);

            if (!HasExtractedPackageContent(extractedRoot))
            {
                var downloadedRoot = Path.Combine(extractedRoot, ".download");
                FileHelper.EnsureDirectoryExists(downloadedRoot);

                if (!DownloadPackage(packageReference, context, downloadedRoot, out var installedPackageFolder))
                    return false;

                if (!HasCompiledPackageContent(installedPackageFolder))
                {
                    MessageLogger.Warning($"⚠️ NuGet package '{packageReference.Id} {packageReference.Version}' did not contain compiled model files");
                    return false;
                }

                FileHelper.CopyDirectory(installedPackageFolder, extractedRoot);
                NormalizeExtractedPackageLayout(extractedRoot);

                try
                {
                    Directory.Delete(downloadedRoot, recursive: true);
                }
                catch (Exception exception)
                {
                    MessageLogger.Warning($"⚠️ Could not delete package staging folder '{downloadedRoot}': {exception.Message}");
                }
            }

            return HasExtractedPackageContent(extractedRoot);
        }

        private static bool HasCompiledPackageContent(string packageFolder)
        {
            if (!Directory.Exists(packageFolder))
                return false;

            return Directory.GetFiles(packageFolder, "*.xref", SearchOption.AllDirectories).Any();
        }

        private bool DownloadPackage(PackageReference packageReference, PackageContext context, string downloadedRoot, out string installedPackageFolder)
        {
            installedPackageFolder = Path.Combine(downloadedRoot, $"{packageReference.Id}.{packageReference.Version}");
            if (Directory.Exists(installedPackageFolder) && Directory.EnumerateFileSystemEntries(installedPackageFolder).Any())
                return true;

            if (!ValidatePackageVersionExists(context, packageReference.Id, packageReference.Version))
                return false;

            var targetInstalledPackageFolder = installedPackageFolder;

            var nugetExecutable = ResolveNuGetExecutable(out var nugetSearchLocations);
            if (nugetExecutable.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Could not locate nuget.exe. Cannot download Compiled Nuget packages");
                MessageLogger.Info("Set a NuGet executable path in Settings or make sure nuget.exe is available on PATH");
                MessageLogger.LogOnly($"NuGet search locations: {string.Join(" | ", nugetSearchLocations)}");
                return false;
            }

            try
            {
                using var nugetConfig = CreateNugetConfigScope(context.NugetConfigPath);
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = nugetExecutable,
                    Arguments = $"install \"{packageReference.Id}\" -Version \"{packageReference.Version}\" -OutputDirectory \"{downloadedRoot}\" -ConfigFile \"{nugetConfig.ConfigPath}\" -NonInteractive",
                    WorkingDirectory = context.RepositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ApplyAzureArtifactsCredentials(processStartInfo, context.NugetConfigPath);

                RetryHelper.RetryOnException(
                    operation: () =>
                    {
                        if (!RunProcess(processStartInfo, out var output))
                            throw new InvalidOperationException($"NuGet install failed for '{packageReference.Id} {packageReference.Version}'. {output}".Trim());

                        if (!Directory.Exists(targetInstalledPackageFolder))
                            throw new InvalidOperationException($"NuGet install completed for '{packageReference.Id} {packageReference.Version}', but '{targetInstalledPackageFolder}' was not created");
                    },
                    times: NugetDownloadRetryCount,
                    onRetry: (attempt, exception, retryDelay) =>
                    {
                        MessageLogger.Warning(
                            $"⚠️ NuGet download attempt {attempt} failed for '{packageReference.Id} {packageReference.Version}'. Retrying in {retryDelay.TotalSeconds:0}s. {exception.Message}");
                    });

                MessageLogger.Info($"📦 Downloaded Compiled Nuget package '{packageReference.Id} {packageReference.Version}'");
                return Directory.Exists(installedPackageFolder);
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"❌ Failed to download package '{packageReference.Id} {packageReference.Version}': {exception.Message}");
                return false;
            }
        }

        private static void NormalizeExtractedPackageLayout(string extractedRoot)
        {
            var compiledModelDirectories = Directory.GetFiles(extractedRoot, "*.xref", SearchOption.AllDirectories)
                .Where(path => !IsUnderDownloadFolder(extractedRoot, path))
                .Select(Path.GetDirectoryName)
                .Where(directory => !directory.IsNullOrEmpty())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var modelDirectory in compiledModelDirectories)
            {
                var sourceDirectory = modelDirectory!;
                if (sourceDirectory.SameAs(extractedRoot))
                    continue;

                var targetDirectory = Path.Combine(extractedRoot, Path.GetFileName(sourceDirectory));
                if (sourceDirectory.SameAs(targetDirectory))
                    continue;

                if (Directory.Exists(targetDirectory))
                    continue;

                Directory.Move(sourceDirectory, targetDirectory);
            }
        }

        private static bool HasExtractedPackageContent(string extractedRoot)
        {
            if (!Directory.Exists(extractedRoot))
                return false;

            return Directory.GetFiles(extractedRoot, "*.xref", SearchOption.AllDirectories)
                .Any(path => !IsUnderDownloadFolder(extractedRoot, path));
        }

        private static bool IsUnderDownloadFolder(string extractedRoot, string path)
        {
            var downloadRoot = Path.Combine(extractedRoot, ".download") + Path.DirectorySeparatorChar;
            return path.StartsWith(downloadRoot, StringComparison.OrdinalIgnoreCase);
        }

        private string GetExtractedPackageRoot(PackageReference packageReference)
            => Path.Combine(_deployablePackagesRoot, $"{packageReference.Id}-{packageReference.Version}");

        private static string? FindRepositoryConfigFile(string repositoryRoot, string fileName)
        {
            var buildFolderCandidate = Path.Combine(repositoryRoot, "Build", fileName);
            if (File.Exists(buildFolderCandidate))
                return buildFolderCandidate;

            return Directory
                .GetFiles(repositoryRoot, fileName, SearchOption.AllDirectories)
                .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar))
                .FirstOrDefault();
        }

        private static List<PackageReference> LoadIsvPackageReferences(string isvConfigPath)
        {
            var document = XDocument.Load(isvConfigPath);

            return document.Descendants()
                .Where(element => element.Name.LocalName.Contains("package", StringComparison.OrdinalIgnoreCase))
                .Select(element => new PackageReference(
                    GetPackageId(element),
                    GetPackageVersion(element)))
                .Where(packageReference => !packageReference.Id.IsNullOrEmpty() && !packageReference.Version.IsNullOrEmpty())
                .DistinctBy(packageReference => packageReference.GetKey(), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetPackageId(XElement element)
        {
            return (element.Attribute("id")?.Value
                ?? element.Attribute("packageId")?.Value
                ?? element.Attribute("name")?.Value
                ?? element.Attribute("package")?.Value
                ?? string.Empty).Trim();
        }

        private static string GetPackageVersion(XElement element)
        {
            return (element.Attribute("version")?.Value
                ?? element.Attribute("packageVersion")?.Value
                ?? string.Empty).Trim();
        }

        private static void UpsertPackageReference(string isvConfigPath, string packageId, string packageVersion)
        {
            var document = File.Exists(isvConfigPath)
                ? XDocument.Load(isvConfigPath)
                : new XDocument(new XElement("packages"));

            var root = document.Root ?? new XElement("packages");
            if (document.Root == null)
                document.Add(root);

            var existingElement = root.DescendantsAndSelf()
                .FirstOrDefault(element => element.Name.LocalName.Contains("package", StringComparison.OrdinalIgnoreCase)
                    && GetPackageId(element).SameAs(packageId));

            if (existingElement != null)
            {
                existingElement.SetAttributeValue(existingElement.Attribute("id") != null ? "id" : existingElement.Attribute("packageId") != null ? "packageId" : existingElement.Attribute("name") != null ? "name" : "id", packageId);
                existingElement.SetAttributeValue(existingElement.Attribute("version") != null ? "version" : existingElement.Attribute("packageVersion") != null ? "packageVersion" : "version", packageVersion);
            }
            else
            {
                root.Add(new XElement("package",
                    new XAttribute("id", packageId),
                    new XAttribute("version", packageVersion)));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(isvConfigPath)!);
            document.Save(isvConfigPath);
        }

        private static bool RemovePackageReference(string isvConfigPath, string packageId)
        {
            if (!File.Exists(isvConfigPath))
                return false;

            var document = XDocument.Load(isvConfigPath);
            var matches = document.Descendants()
                .Where(element => element.Name.LocalName.Contains("package", StringComparison.OrdinalIgnoreCase)
                    && GetPackageId(element).SameAs(packageId))
                .ToList();

            if (matches.Count == 0)
                return false;

            foreach (var match in matches)
            {
                match.Remove();
            }

            document.Save(isvConfigPath);
            return true;
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

        private static bool TryGetIsvConfigPath(RepositoryModel repository, bool createIfMissing, out string isvConfigPath)
        {
            isvConfigPath = string.Empty;

            var repositoryRoot = (repository.RepoRootFolder ?? string.Empty).Trim();
            if (repositoryRoot.IsNullOrEmpty())
                return false;

            isvConfigPath = FindRepositoryConfigFile(repositoryRoot, "isv.config") ?? string.Empty;
            if (!isvConfigPath.IsNullOrEmpty())
                return true;

            if (!createIfMissing)
                return false;

            isvConfigPath = Path.Combine(repositoryRoot, "Build", "isv.config");
            return true;
        }

        private bool TryResolvePackageNugetConfigPath(RepositoryModel repository, out string nugetConfigPath)
        {
            nugetConfigPath = string.Empty;

            var repositoryRoot = (repository.RepoRootFolder ?? string.Empty).Trim();
            if (repositoryRoot.IsNullOrEmpty())
                return false;

            nugetConfigPath = FindRepositoryConfigFile(repositoryRoot, "nuget.config") ?? string.Empty;
            if (!nugetConfigPath.IsNullOrEmpty())
                return true;

            nugetConfigPath = ResolveDefaultNugetConfigPath() ?? string.Empty;
            if (!nugetConfigPath.IsNullOrEmpty())
            {
                MessageLogger.LogOnly($"No nuget.config found under repository '{repository.DisplayName}'. Falling back to '{nugetConfigPath}'");
                return true;
            }

            MessageLogger.LogOnly($"No nuget.config found under repository '{repository.DisplayName}', and no system NuGet.Config could be resolved");
            return false;
        }

        private bool ValidatePackageVersionExists(RepositoryModel repository, string packageId, string packageVersion)
        {
            if (!TryResolvePackageNugetConfigPath(repository, out var nugetConfigPath))
                return false;

            return ValidatePackageVersionExists(
                repository.RepoRootFolder ?? Directory.GetCurrentDirectory(),
                nugetConfigPath,
                packageId,
                packageVersion);
        }

        private bool ValidatePackageVersionExists(PackageContext context, string packageId, string packageVersion)
            => ValidatePackageVersionExists(context.RepositoryRoot, context.NugetConfigPath, packageId, packageVersion);

        private bool ValidatePackageVersionExists(
            string workingDirectory,
            string nugetConfigPath,
            string packageId,
            string packageVersion)
        {
            if (TryGetAvailablePackageVersions(workingDirectory, nugetConfigPath, packageId, out var versions, out var errorMessage))
            {
                if (versions.Contains(packageVersion, StringComparer.OrdinalIgnoreCase))
                    return true;

                var availableVersions = versions.Count == 0
                    ? "none"
                    : string.Join(", ", versions.OrderByDescending(version => version, StringComparer.OrdinalIgnoreCase));

                MessageLogger.Error($"❌ Package '{packageId}' version '{packageVersion}' was not found. Available versions: {availableVersions}");
                return false;
            }

            MessageLogger.Error($"❌ Could not verify package '{packageId}' version '{packageVersion}'. {errorMessage}");
            return false;
        }

        private bool ValidateBuildPackageSettings(string nugetConfigPath)
        {
            var azureFeedEndpoints = LoadAzureArtifactsFeedEndpoints(nugetConfigPath);
            var nugetExecutable = ResolveNuGetExecutable(out var nugetSearchLocations);
            var longPathsEnabled = IsWindowsLongPathsEnabled();

            MessageLogger.LogOnly($"Build package nuget.config: {nugetConfigPath}");
            MessageLogger.LogOnly(
                nugetExecutable.IsNullOrEmpty()
                    ? $"NuGet executable could not be resolved. Searched: {string.Join(" | ", nugetSearchLocations)}"
                    : $"NuGet executable: {nugetExecutable}");
            MessageLogger.LogOnly($"Windows long paths enabled: {longPathsEnabled}");

            if (azureFeedEndpoints.Count > 0)
                MessageLogger.LogOnly($"Azure Artifacts feeds: {string.Join(", ", azureFeedEndpoints)}");

            if (nugetExecutable.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Could not locate nuget.exe. Add it to PATH or set a NuGet executable path in Settings");
                return false;
            }

            if (!longPathsEnabled)
            {
                MessageLogger.Warning("⚠️ Windows long paths are disabled on this machine. Large FO build packages may fail to extract");
                MessageLogger.Info(@"Enable 'LongPathsEnabled' under HKLM\SYSTEM\CurrentControlSet\Control\FileSystem to reduce path-length install failures");
            }

            if (azureFeedEndpoints.Count == 0)
                return true;

            var secret = ResolveAzureArtifactsSecret();
            if (!secret.IsNullOrEmpty())
                return true;

            MessageLogger.Error("❌ Cannot download FO build packages because private Azure Artifacts feeds are configured but no credentials are saved in Settings");
            MessageLogger.Info("Open Settings and provide an Azure Artifacts PAT or API key before retrying package download");
            MessageLogger.LogOnly($"Azure Artifacts feeds: {string.Join(", ", azureFeedEndpoints)}");

            return false;
        }

        private void ApplyAzureArtifactsCredentials(ProcessStartInfo processStartInfo, string nugetConfigPath)
        {
            var secret = ResolveAzureArtifactsSecret();
            if (secret.IsNullOrEmpty())
                return;

            var feedEndpoints = LoadAzureArtifactsFeedEndpoints(nugetConfigPath);
            if (feedEndpoints.Count == 0)
                return;

            var username = _config.AzureArtifactsUsername.IsNullOrEmpty() ? "FODevManager" : _config.AzureArtifactsUsername;
            var endpointCredentials = string.Join(",",
                feedEndpoints.Select(endpoint =>
                    $"{{\"endpoint\":\"{EscapeJson(endpoint)}\",\"username\":\"{EscapeJson(username)}\",\"password\":\"{EscapeJson(secret)}\"}}"));

            processStartInfo.Environment["VSS_NUGET_EXTERNAL_FEED_ENDPOINTS"] =
                $"{{\"endpointCredentials\":[{endpointCredentials}]}}";
        }

        private NugetConfigScope CreateNugetConfigScope(string nugetConfigPath)
        {
            return CreateCredentialedNugetConfig(nugetConfigPath, out var credentialedNugetConfigPath)
                ? new NugetConfigScope(credentialedNugetConfigPath, deleteOnDispose: true)
                : new NugetConfigScope(nugetConfigPath, deleteOnDispose: false);
        }

        private bool CreateCredentialedNugetConfig(string nugetConfigPath, out string credentialedNugetConfigPath)
        {
            credentialedNugetConfigPath = nugetConfigPath;

            var secret = ResolveAzureArtifactsSecret();
            if (secret.IsNullOrEmpty() || nugetConfigPath.IsNullOrEmpty() || !File.Exists(nugetConfigPath))
                return false;

            try
            {
                var document = XDocument.Load(nugetConfigPath);
                var sourceCredentials = ResolveAzureArtifactsSourceCredentials(document);
                if (sourceCredentials.Count == 0)
                    return false;

                var configuration = document.Element("configuration");
                if (configuration == null)
                    return false;

                var credentialsElement = configuration.Element("packageSourceCredentials");
                if (credentialsElement == null)
                {
                    credentialsElement = new XElement("packageSourceCredentials");
                    configuration.Add(credentialsElement);
                }

                var username = _config.AzureArtifactsUsername.IsNullOrEmpty() ? "FODevManager" : _config.AzureArtifactsUsername;
                foreach (var source in sourceCredentials)
                {
                    var sourceElementName = XmlConvert.EncodeName(source.Key);
                    credentialsElement.Elements(sourceElementName).Remove();
                    credentialsElement.Add(new XElement(sourceElementName,
                        new XElement("add",
                            new XAttribute("key", "Username"),
                            new XAttribute("value", username)),
                        new XElement("add",
                            new XAttribute("key", "ClearTextPassword"),
                            new XAttribute("value", secret))));
                }

                var tempDirectory = Path.Combine(Path.GetTempPath(), "FODevManager", "NuGet", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDirectory);
                credentialedNugetConfigPath = Path.Combine(tempDirectory, "nuget.config");
                document.Save(credentialedNugetConfigPath);
                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"⚠️ Could not create credentialed NuGet config for '{nugetConfigPath}': {exception.Message}");
                credentialedNugetConfigPath = nugetConfigPath;
                return false;
            }
        }

        private static Dictionary<string, string> ResolveAzureArtifactsSourceCredentials(XDocument document)
        {
            return document
                .Descendants("packageSources")
                .Elements("add")
                .Select(element => new
                {
                    Key = element.Attribute("key")?.Value?.Trim() ?? string.Empty,
                    Value = element.Attribute("value")?.Value?.Trim() ?? string.Empty
                })
                .Where(source => !source.Key.IsNullOrEmpty() && IsAzureArtifactsEndpoint(source.Value))
                .GroupBy(source => source.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsAzureArtifactsEndpoint(string value)
        {
            return !value.IsNullOrEmpty()
                && (value.Contains("pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase));
        }

        private sealed class NugetConfigScope : IDisposable
        {
            private readonly bool _deleteOnDispose;

            public NugetConfigScope(string configPath, bool deleteOnDispose)
            {
                ConfigPath = configPath;
                _deleteOnDispose = deleteOnDispose;
            }

            public string ConfigPath { get; }

            public void Dispose()
            {
                if (!_deleteOnDispose || ConfigPath.IsNullOrEmpty())
                    return;

                try
                {
                    if (File.Exists(ConfigPath))
                        File.Delete(ConfigPath);

                    var directory = Path.GetDirectoryName(ConfigPath);
                    if (!directory.IsNullOrEmpty() && Directory.Exists(directory))
                        Directory.Delete(directory, recursive: true);
                }
                catch (Exception exception)
                {
                    MessageLogger.LogOnly($"Could not delete temporary NuGet config '{ConfigPath}': {exception.Message}");
                }
            }
        }

        private bool ValidatePackageSettings(RepositoryModel repository, PackageContext context)
        {
            var azureFeedEndpoints = LoadAzureArtifactsFeedEndpoints(context.NugetConfigPath);
            if (azureFeedEndpoints.Count == 0)
                return true;

            var secret = ResolveAzureArtifactsSecret();
            if (!secret.IsNullOrEmpty())
                return true;

            var displayName = repository.DisplayName.IsNullOrEmpty()
                ? repository.RepoId
                : repository.DisplayName;

            MessageLogger.Error(
                $"❌ Cannot prepare NuGet packages for repository '{displayName}' because private Azure Artifacts feeds are configured but no credentials are saved in Settings");
            MessageLogger.Info("Open Settings and provide an Azure Artifacts PAT or API key before retrying package download");
            MessageLogger.LogOnly($"Azure Artifacts feeds: {string.Join(", ", azureFeedEndpoints)}");

            return false;
        }

        private string ResolveAzureArtifactsSecret()
        {
            if (!_config.AzureArtifactsPat.IsNullOrEmpty())
                return _config.AzureArtifactsPat;

            return _config.AzureArtifactsApiKey ?? string.Empty;
        }

        private static List<string> LoadAzureArtifactsFeedEndpoints(string nugetConfigPath)
        {
            if (nugetConfigPath.IsNullOrEmpty() || !File.Exists(nugetConfigPath))
                return new List<string>();

            try
            {
                var document = XDocument.Load(nugetConfigPath);
                return document
                    .Descendants("packageSources")
                    .Elements("add")
                    .Select(element => element.Attribute("value")?.Value?.Trim())
                    .Where(value => !value.IsNullOrEmpty())
                    .Where(value =>
                        value!.Contains("pkgs.dev.azure.com", StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Cast<string>()
                    .ToList();
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"⚠️ Could not read Azure Artifacts feeds from '{nugetConfigPath}': {exception.Message}");
                return new List<string>();
            }
        }

        private static string EscapeJson(string value)
            => value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal);

        private static string? ResolveApplicationNugetConfigPath()
        {
            var baseDirectory = AppContext.BaseDirectory;
            var baseDirectoryCandidate = Path.Combine(baseDirectory, "nuget.config");
            if (File.Exists(baseDirectoryCandidate))
                return baseDirectoryCandidate;

            var currentDirectoryCandidate = Path.Combine(Directory.GetCurrentDirectory(), "FODevManager.WinUI", "nuget.config");
            if (File.Exists(currentDirectoryCandidate))
                return currentDirectoryCandidate;

            return null;
        }

        private static string? ResolveDefaultNugetConfigPath()
        {
            var userNugetConfig = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NuGet",
                "NuGet.Config");

            if (File.Exists(userNugetConfig))
                return userNugetConfig;

            return null;
        }

        private bool TryGetAvailablePackageVersions(
            string workingDirectory,
            string nugetConfigPath,
            string packageId,
            out List<string> versions,
            out string errorMessage)
        {
            versions = new List<string>();
            errorMessage = string.Empty;

            var nugetExecutable = ResolveNuGetExecutable(out var nugetSearchLocations);
            if (nugetExecutable.IsNullOrEmpty())
            {
                errorMessage = $"Could not locate nuget.exe. Searched: {string.Join(" | ", nugetSearchLocations)}";
                return false;
            }

            using var nugetConfig = CreateNugetConfigScope(nugetConfigPath);
            var processStartInfo = new ProcessStartInfo
            {
                FileName = nugetExecutable,
                Arguments = $"list \"{packageId}\" -AllVersions -Prerelease -ConfigFile \"{nugetConfig.ConfigPath}\" -NonInteractive",
                WorkingDirectory = workingDirectory.IsNullOrEmpty() ? Directory.GetCurrentDirectory() : workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            ApplyAzureArtifactsCredentials(processStartInfo, nugetConfigPath);

            if (!RunProcess(processStartInfo, out var output))
            {
                if (IsNuGetNoPackagesFoundOutput(output))
                    return true;

                errorMessage = output;
                return false;
            }

            versions = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Trim())
                .Where(line => !line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
                .Where(line => !line.StartsWith("Feeds used", StringComparison.OrdinalIgnoreCase))
                .Select(line =>
                {
                    var separatorIndex = line.LastIndexOf(' ');
                    if (separatorIndex <= 0)
                        return string.Empty;

                    var listedPackageId = line[..separatorIndex].Trim();
                    var listedVersion = line[(separatorIndex + 1)..].Trim();
                    return listedPackageId.SameAs(packageId) ? listedVersion : string.Empty;
                })
                .Where(version => !version.IsNullOrEmpty())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return true;
        }

        private static bool IsNuGetNoPackagesFoundOutput(string output)
        {
            return output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Trim())
                .Where(line => !line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
                .Any(line => line.Equals("No packages found.", StringComparison.OrdinalIgnoreCase));
        }

        private bool TryEnsureFoBuildPackages(string buildPackagesRoot, string nugetConfigPath, out Dictionary<string, string> packageRoots)
        {
            packageRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var packageId in FoBuildPackageIds)
            {
                if (!TryEnsureFoBuildPackage(buildPackagesRoot, nugetConfigPath, packageId, out var packageRoot))
                    return false;

                packageRoots[packageId] = packageRoot;
            }

            return true;
        }

        private bool TryEnsureFoBuildPackage(string buildPackagesRoot, string nugetConfigPath, string packageId, out string packageRoot)
        {
            packageRoot = ResolveInstalledPackageRoot(GetBuildPackageSearchRoots(buildPackagesRoot), packageId);
            if (!packageRoot.IsNullOrEmpty())
                return true;

            string resolvedPackageRoot = string.Empty;

            var nugetExecutable = ResolveNuGetExecutable(out var nugetSearchLocations);
            if (nugetExecutable.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Could not locate nuget.exe. Cannot download FO build packages");
                MessageLogger.Info("Set a NuGet executable path in Settings or make sure nuget.exe is available on PATH");
                MessageLogger.LogOnly($"NuGet search locations: {string.Join(" | ", nugetSearchLocations)}");
                return false;
            }

            MessageLogger.Info($"📥 Downloading build package '{packageId}'..");
            MessageLogger.LogOnly($"Using nuget executable: {nugetExecutable}");
            MessageLogger.LogOnly($"Using nuget.config: {nugetConfigPath}");

            try
            {
                using var nugetConfig = CreateNugetConfigScope(nugetConfigPath);
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = nugetExecutable,
                    Arguments = $"install \"{packageId}\" -OutputDirectory \"{buildPackagesRoot}\" -ConfigFile \"{nugetConfig.ConfigPath}\" -NonInteractive",
                    WorkingDirectory = buildPackagesRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ApplyAzureArtifactsCredentials(processStartInfo, nugetConfigPath);

                RetryHelper.RetryOnException(
                    operation: () =>
                    {
                        if (!RunProcess(processStartInfo, out var output))
                            throw new InvalidOperationException($"NuGet install failed for '{packageId}'. {output}".Trim());

                        resolvedPackageRoot = ResolveInstalledPackageRoot(GetBuildPackageSearchRoots(buildPackagesRoot), packageId);
                        if (resolvedPackageRoot.IsNullOrEmpty())
                            throw new InvalidOperationException($"Package '{packageId}' was downloaded, but the installed folder could not be resolved");
                    },
                    times: NugetDownloadRetryCount,
                    onRetry: (attempt, exception, retryDelay) =>
                    {
                        MessageLogger.LogOnly($"Build package download raw failure for '{packageId}': {exception.Message}");
                        MessageLogger.Warning(
                            $"⚠️ Build package download attempt {attempt} failed for '{packageId}'. Retrying in {retryDelay.TotalSeconds:0}s. {SummarizeNugetInstallFailure(packageId, buildPackagesRoot, exception.Message)}");
                    });
            }
            catch (Exception exception)
            {
                MessageLogger.LogOnly($"Build package download raw failure for '{packageId}': {exception.Message}");
                MessageLogger.Error($"❌ Failed to download build package '{packageId}': {SummarizeNugetInstallFailure(packageId, buildPackagesRoot, exception.Message)}");
                if (IsLikelyLongPathFailure(exception.Message))
                {
                    MessageLogger.Warning("⚠️ Package extraction appears to be hitting a Windows long-path limit on this machine");
                    MessageLogger.Info("Enable Win32 long paths in Local Group Policy or set LongPathsEnabled=1, then restart the machine");
                }
                return false;
            }

            packageRoot = resolvedPackageRoot;
            MessageLogger.Info($"📦 Build package ready: {packageRoot}");
            return true;
        }

        private string ResolveInstalledPackageRoot(IEnumerable<string> buildPackagesRoots, string packageId)
        {
            foreach (var buildPackagesRoot in buildPackagesRoots)
            {
                if (!Directory.Exists(buildPackagesRoot))
                    continue;

                var packageRoot = Directory.GetDirectories(buildPackagesRoot, $"{packageId}.*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (!packageRoot.IsNullOrEmpty())
                    return packageRoot;
            }

            return string.Empty;
        }

        private bool TryBuildMsBuildContext(
            IReadOnlyDictionary<string, string> packageRoots,
            IReadOnlyCollection<string> compiledNugetReferenceFolders,
            string metadataDirectory,
            out MsBuildContext context)
        {
            context = null!;

            if (!packageRoots.TryGetValue(CompilerPackageId, out var compilerPackageRoot) || compilerPackageRoot.IsNullOrEmpty())
                return false;

            var buildTasksDirectory = Path.Combine(compilerPackageRoot, "DevAlm");
            if (!Directory.Exists(buildTasksDirectory))
                return false;

            var referenceFolders = new List<string>();

            if (Directory.Exists(_deploymentBasePath))
                referenceFolders.Add(_deploymentBasePath);

            referenceFolders.AddRange([
                Path.Combine(packageRoots[PlatformBuildPackageId], "ref", "net40"),
                Path.Combine(packageRoots[Application1BuildPackageId], "ref", "net40"),
                Path.Combine(packageRoots[Application2BuildPackageId], "ref", "net40"),
                Path.Combine(packageRoots[ApplicationSuiteBuildPackageId], "ref", "net40")
            ]);

            var missingReferenceFolder = referenceFolders.FirstOrDefault(path => !Directory.Exists(path));
            if (!missingReferenceFolder.IsNullOrEmpty())
                return false;

            foreach (var referenceFolder in compiledNugetReferenceFolders)
            {
                if (!referenceFolder.IsNullOrEmpty() && !referenceFolders.Any(existing => AreSameDirectoryPath(existing, referenceFolder)))
                    referenceFolders.Add(referenceFolder);
            }

            context = new MsBuildContext(
                compilerPackageRoot,
                buildTasksDirectory,
                metadataDirectory,
                string.Join(";", referenceFolders));

            return true;
        }

        private static string ResolveBuildMetadataDirectory(ProfileEnvironmentModel model, string fallbackMetadataDirectory)
        {
            if (!model.MetadataFolder.IsNullOrEmpty() && Directory.Exists(model.MetadataFolder))
            {
                var metadataDirectory = Directory.GetParent(Path.GetFullPath(model.MetadataFolder))?.FullName ?? string.Empty;
                if (!metadataDirectory.IsNullOrEmpty())
                    return metadataDirectory;
            }

            return fallbackMetadataDirectory;
        }

        private bool TryResolveReferencedCompiledNugetReferenceFolders(
            ProfileModel profile,
            ProfileEnvironmentModel model,
            out IReadOnlyList<string> referenceFolders)
        {
            referenceFolders = [];

            if (!TryResolveReferencedCompiledNugetModels(profile, model, out var compiledModels))
                return false;

            var resolvedFolders = new List<string>();
            foreach (var compiledModel in compiledModels)
            {
                AddReferenceFolder(resolvedFolders, compiledModel.CompiledModelFolder);

                var binFolder = Path.Combine(compiledModel.CompiledModelFolder, "bin");
                if (Directory.Exists(binFolder))
                    AddReferenceFolder(resolvedFolders, binFolder);
            }

            referenceFolders = resolvedFolders;
            return true;
        }

        private bool TryResolveReferencedCompiledNugetModels(
            ProfileModel profile,
            ProfileEnvironmentModel model,
            out IReadOnlyList<ProfileEnvironmentModel> compiledModels)
        {
            compiledModels = [];

            var descriptorPath = ResolveModelDescriptorPath(model);
            if (descriptorPath.IsNullOrEmpty() || !File.Exists(descriptorPath))
            {
                MessageLogger.LogOnly($"No model descriptor found for '{model.ModelName}'. No compiled NuGet references were added to the build");
                return true;
            }

            IReadOnlyCollection<string> moduleReferences;
            try
            {
                moduleReferences = LoadModuleReferences(descriptorPath);
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"❌ Could not read module references from '{descriptorPath}'. {exception.Message}");
                return false;
            }

            if (moduleReferences.Count == 0)
            {
                MessageLogger.LogOnly($"Model descriptor '{descriptorPath}' contains no module references");
                return true;
            }

            var repository = profile.FindRepositoryForModel(model);
            var repositoryRoot = repository?.RepoRootFolder ?? profile.TryGetRepoRootFolder(model) ?? string.Empty;
            var isvConfigPath = repositoryRoot.IsNullOrEmpty()
                ? string.Empty
                : FindRepositoryConfigFile(repositoryRoot, "isv.config") ?? string.Empty;

            if (isvConfigPath.IsNullOrEmpty() || !File.Exists(isvConfigPath))
            {
                MessageLogger.LogOnly($"No Build\\isv.config found for '{model.ModelName}'. No compiled NuGet references were added to the build");
                return true;
            }

            List<PackageReference> isvPackageReferences;
            try
            {
                isvPackageReferences = LoadIsvPackageReferences(isvConfigPath);
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"❌ Could not read package references from '{isvConfigPath}'. {exception.Message}");
                return false;
            }

            var referencedPackages = isvPackageReferences
                .Where(packageReference => moduleReferences.Any(moduleReference => moduleReference.SameAs(packageReference.Id)))
                .Where(packageReference => !HasNonNugetModelName(profile, packageReference.Id))
                .ToList();

            if (referencedPackages.Count == 0)
                return true;

            var resolvedModels = new List<ProfileEnvironmentModel>();
            foreach (var packageReference in referencedPackages)
            {
                var compiledModel = profile.AllModels.FirstOrDefault(candidate => IsMatchingCompiledNugetModel(candidate, packageReference));
                if (compiledModel == null)
                {
                    MessageLogger.Error($"❌ Model '{model.ModelName}' references NuGet package '{packageReference.Id}' version '{packageReference.Version}', but the profile does not contain that compiled NuGet model");
                    return false;
                }

                var compiledModelFolder = compiledModel.CompiledModelFolder ?? string.Empty;
                if (compiledModelFolder.IsNullOrEmpty() || !Directory.Exists(compiledModelFolder))
                {
                    MessageLogger.Error($"❌ Compiled NuGet model '{compiledModel.ModelName}' folder was not found: {compiledModelFolder}");
                    return false;
                }

                resolvedModels.Add(compiledModel);
                MessageLogger.Info($"Added compiled NuGet build reference '{packageReference.Id}' version '{packageReference.Version}' from '{compiledModelFolder}'");
            }

            compiledModels = resolvedModels;
            return true;
        }

        private static string PrepareCompiledNugetReferenceRoot(string runRoot, IReadOnlyCollection<ProfileEnvironmentModel> compiledModels)
        {
            var referenceRoot = Path.Combine(runRoot, "CompiledNugetReferences");
            EnsureCleanDirectory(referenceRoot);

            foreach (var compiledModel in compiledModels)
            {
                var moduleName = GetUnversionedModelName(compiledModel);
                var targetFolder = Path.Combine(referenceRoot, moduleName);
                EnsureCleanDirectory(targetFolder);
                FileHelper.CopyDirectory(compiledModel.CompiledModelFolder, targetFolder);
            }

            return referenceRoot;
        }

        private static IReadOnlyList<string> ResolveProjectReferenceOutputFolders(string projectFilePath)
        {
            var outputFolders = new List<string>();

            foreach (var referencedProjectPath in ResolveProjectReferencePaths(projectFilePath))
            {
                foreach (var outputFolder in ResolveProjectOutputFolders(referencedProjectPath))
                    AddReferenceFolder(outputFolders, outputFolder);
            }

            return outputFolders;
        }

        private static IReadOnlyList<string> ResolveProjectReferencePaths(string projectFilePath)
        {
            if (projectFilePath.IsNullOrEmpty() || !File.Exists(projectFilePath))
                return [];

            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFilePath)) ?? string.Empty;
            if (projectDirectory.IsNullOrEmpty())
                return [];

            var document = XDocument.Load(projectFilePath);

            return document.Descendants()
                .Where(element => element.Name.LocalName.SameAs("ProjectReference"))
                .Select(element => element.Attribute("Include")?.Value?.Trim() ?? string.Empty)
                .Where(include => !include.IsNullOrEmpty())
                .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include)))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IReadOnlyList<string> ResolveProjectOutputFolders(string referencedProjectPath)
        {
            return ResolveProjectTargetFrameworks(referencedProjectPath)
                .Select(targetFramework => Path.Combine(
                    Path.GetDirectoryName(referencedProjectPath) ?? string.Empty,
                    "bin",
                    "Debug",
                    targetFramework))
                .ToList();
        }

        private static IReadOnlyList<string> ResolveProjectTargetFrameworks(string projectFilePath)
        {
            var document = XDocument.Load(projectFilePath);
            var targetFramework = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName.SameAs("TargetFramework"))
                ?.Value
                ?.Trim() ?? string.Empty;

            if (!targetFramework.IsNullOrEmpty())
                return [targetFramework];

            var targetFrameworks = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName.SameAs("TargetFrameworks"))
                ?.Value ?? string.Empty;

            return targetFrameworks
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(framework => !framework.IsNullOrEmpty())
                .DefaultIfEmpty("net48")
                .ToList();
        }

        private static string ResolveModelDescriptorPath(ProfileEnvironmentModel model)
        {
            if (model.MetadataFolder.IsNullOrEmpty() || model.ModelName.IsNullOrEmpty())
                return string.Empty;

            return Path.Combine(model.MetadataFolder, "Descriptor", $"{model.ModelName}.xml");
        }

        private static IReadOnlyCollection<string> LoadModuleReferences(string descriptorPath)
        {
            var document = XDocument.Load(descriptorPath);
            var moduleReferencesElement = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName.SameAs("ModuleReferences"));

            if (moduleReferencesElement == null)
                return [];

            var isNil = moduleReferencesElement.Attributes()
                .Any(attribute => attribute.Name.LocalName.SameAs("nil") && attribute.Value.SameAs("true"));
            if (isNil)
                return [];

            return moduleReferencesElement.Descendants()
                .Where(element => element.Name.LocalName.SameAs("string"))
                .Select(element => element.Value.Trim())
                .Where(reference => !reference.IsNullOrEmpty())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsMatchingCompiledNugetModel(ProfileEnvironmentModel candidate, PackageReference packageReference)
        {
            if (candidate == null || candidate.ModelType != ModelType.CompiledNuget)
                return false;

            if (!candidate.PackageVersion.SameAs(packageReference.Version))
                return false;

            return candidate.PackageId.SameAs(packageReference.Id)
                || candidate.ModelName.SameAs(packageReference.Id)
                || GetUnversionedModelName(candidate).SameAs(packageReference.Id);
        }

        private static bool HasNonNugetModelName(ProfileModel profile, string modelName)
        {
            if (profile == null || modelName.IsNullOrEmpty())
                return false;

            return profile.AllModels.Any(model =>
                model.ModelType != ModelType.CompiledNuget
                && model.ModelName.SameAs(modelName));
        }

        private static string GetUnversionedModelName(ProfileEnvironmentModel model)
        {
            var modelName = model.ModelName ?? string.Empty;
            var packageVersion = model.PackageVersion ?? string.Empty;
            var versionSuffix = $"-{packageVersion}";

            if (!modelName.IsNullOrEmpty()
                && !packageVersion.IsNullOrEmpty()
                && modelName.EndsWith(versionSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return modelName[..^versionSuffix.Length];
            }

            return modelName;
        }

        private static void AddReferenceFolder(List<string> referenceFolders, string referenceFolder)
        {
            if (referenceFolder.IsNullOrEmpty())
                return;

            if (!referenceFolders.Any(existing => AreSameDirectoryPath(existing, referenceFolder)))
                referenceFolders.Add(referenceFolder);
        }

        private bool RunMsBuild(string solutionFilePath, string sourceProjectFilePath, string modelName, MsBuildContext buildContext, string buildOutputRoot, string runRoot)
        {
            var msbuildExecutable = ResolveMsBuildExecutable();
            if (msbuildExecutable.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Could not locate msbuild.exe. Ensure Visual Studio Build Tools are installed and msbuild is on PATH");
                return false;
            }

            var buildBinPath = Path.Combine(buildOutputRoot, "Bin");
            Directory.CreateDirectory(buildBinPath);

            if (!PrepareProjectReferenceAssemblies(msbuildExecutable, sourceProjectFilePath, buildOutputRoot, modelName))
                return false;

            var processStartInfo = new ProcessStartInfo
            {
                FileName = msbuildExecutable,
                Arguments = BuildMsBuildArguments(solutionFilePath, buildContext, buildOutputRoot),
                WorkingDirectory = Path.GetDirectoryName(solutionFilePath) ?? Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            ApplyDotNetSdkEnvironmentIfNeeded(processStartInfo, msbuildExecutable);

            MessageLogger.Info($"🛠️ Running MSBuild for solution '{solutionFilePath}'.");
            MessageLogger.LogOnly($"Using MSBuild executable: {msbuildExecutable}");

            var compilerLogsRoot = Path.Combine(runRoot, "CompilerLogs");
            Directory.CreateDirectory(compilerLogsRoot);

            var succeeded = RunProcess(processStartInfo, out var output);
            PersistBuildDiagnostics(solutionFilePath, compilerLogsRoot, output);

            if (!succeeded)
            {
                MessageLogger.Error($"❌ MSBuild failed for '{solutionFilePath}'. {output}".Trim());
                return false;
            }

            MessageLogger.Info($"✅ MSBuild completed for '{Path.GetFileName(solutionFilePath)}'");
            return true;
        }

        private static bool PrepareProjectReferenceAssemblies(string msbuildExecutable, string sourceProjectFilePath, string buildOutputRoot, string modelName)
        {
            var projectReferences = ResolveProjectReferencePaths(sourceProjectFilePath);
            if (projectReferences.Count == 0)
                return true;

            foreach (var projectReference in projectReferences)
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = msbuildExecutable,
                    Arguments = $"\"{projectReference}\" /restore /p:Configuration=Debug",
                    WorkingDirectory = Path.GetDirectoryName(projectReference) ?? Directory.GetCurrentDirectory(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ApplyDotNetSdkEnvironmentIfNeeded(processStartInfo, msbuildExecutable);

                if (!RunProcess(processStartInfo, out var output))
                {
                    MessageLogger.Error($"❌ MSBuild failed for project reference '{projectReference}'. {output}".Trim());
                    return false;
                }
            }

            var targetBinFolder = Path.Combine(buildOutputRoot, "Bin", modelName, "bin");
            Directory.CreateDirectory(targetBinFolder);

            foreach (var outputFolder in projectReferences.SelectMany(ResolveProjectOutputFolders))
            {
                if (!Directory.Exists(outputFolder))
                    continue;

                foreach (var filePath in Directory.GetFiles(outputFolder, "*.*", SearchOption.TopDirectoryOnly)
                             .Where(path => IsReferenceAssemblyOutput(Path.GetExtension(path))))
                {
                    File.Copy(filePath, Path.Combine(targetBinFolder, Path.GetFileName(filePath)), overwrite: true);
                }
            }

            return true;
        }

        private static bool IsReferenceAssemblyOutput(string extension)
        {
            return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildMsBuildArguments(string solutionFilePath, MsBuildContext buildContext, string buildOutputRoot)
        {
            var buildBinPath = Path.Combine(buildOutputRoot, "Bin");

            return
                $"\"{solutionFilePath}\" " +
                "/restore " +
                $"/p:BuildTasksDirectory=\"{buildContext.BuildTasksDirectory}\" " +
                $"/p:MetadataDirectory=\"{buildContext.MetadataDirectory}\" " +
                $"/p:FrameworkDirectory=\"{buildContext.CompilerPackageRoot}\" " +
                $"/p:ReferenceFolder=\"{buildContext.ReferenceFolder};{buildBinPath}\" " +
                $"/p:ReferencePath=\"{buildContext.CompilerPackageRoot}\" " +
                $"/p:OutputDirectory=\"{buildBinPath}\"";
        }

        private void PersistBuildDiagnostics(string solutionFilePath, string compilerLogsRoot, string buildOutput)
        {
            try
            {
                Directory.CreateDirectory(compilerLogsRoot);

                var msbuildLogPath = Path.Combine(compilerLogsRoot, "msbuild.log");
                File.WriteAllText(msbuildLogPath, buildOutput ?? string.Empty);

                foreach (var logFilePath in EnumerateCompilerLogFiles(solutionFilePath))
                {
                    var destinationPath = Path.Combine(compilerLogsRoot, Path.GetFileName(logFilePath));
                    if (!AreSameFilePath(logFilePath, destinationPath))
                        File.Copy(logFilePath, destinationPath, overwrite: true);
                }
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"âš ï¸ Could not persist compiler diagnostics. {exception.Message}");
            }
        }

        private static IEnumerable<string> EnumerateCompilerLogFiles(string solutionFilePath)
        {
            var solutionDirectory = Path.GetDirectoryName(solutionFilePath) ?? string.Empty;
            if (solutionDirectory.IsNullOrEmpty() || !Directory.Exists(solutionDirectory))
                yield break;

            var patterns = new[]
            {
                "*.xppc.log",
                "*.xppc.xml",
                "*.labelc.log",
                "*.labelc.err",
                "*.reportsc.log",
                "*.reportsc.xml",
                "*.xppbp.log",
                "*.xppbp.xml"
            };

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in patterns)
            {
                foreach (var candidatePath in Directory.GetFiles(solutionDirectory, pattern, SearchOption.TopDirectoryOnly))
                {
                    if (seenPaths.Add(candidatePath))
                        yield return candidatePath;
                }
            }
        }

        private bool RunNugetUtilFopack(string payloadRoot, string nugetOutputRoot, out string packagePath, out string nuspecPath)
        {
            packagePath = string.Empty;
            nuspecPath = string.Empty;

            var nugetUtilPath = ResolveNugetUtilExecutable();
            if (nugetUtilPath.IsNullOrEmpty())
            {
                MessageLogger.Error($"❌ Could not locate NugetUtil. Expected '{NugetUtilDefaultPath}' or an executable on PATH");
                return false;
            }

            var arguments = $"fopack \"{payloadRoot}\" -output \"{nugetOutputRoot}\" -save-nuspec";
            if (_config.PushDeployablePackageOnBuild)
            {
                arguments += " -push -yes";

                if (!_config.PushDeployablePackageSource.IsNullOrEmpty())
                    arguments += $" -source \"{_config.PushDeployablePackageSource}\"";
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = nugetUtilPath,
                Arguments = arguments,
                WorkingDirectory = nugetOutputRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var appNugetConfigPath = ResolveApplicationNugetConfigPath();
            
            //if (_config.PushDeployablePackageOnBuild && !appNugetConfigPath.IsNullOrEmpty())
            //    ApplyAzureArtifactsCredentials(processStartInfo, appNugetConfigPath);

            if (!RunProcess(processStartInfo, out var output))
            {
                MessageLogger.Error($"❌ NugetUtil failed for '{payloadRoot}'. {output}".Trim());
                return false;
            }

            var packageFiles = Directory.Exists(nugetOutputRoot)
                ? Directory.GetFiles(nugetOutputRoot, "*.nupkg", SearchOption.TopDirectoryOnly)
                : [];
            var nuspecFiles = Directory.Exists(nugetOutputRoot)
                ? Directory.GetFiles(nugetOutputRoot, "*.nuspec", SearchOption.TopDirectoryOnly)
                : [];

            if (packageFiles.Length == 0)
            {
                MessageLogger.Error($"❌ NugetUtil completed, but no .nupkg was created under '{nugetOutputRoot}'");
                return false;
            }

            if (nuspecFiles.Length == 0)
            {
                MessageLogger.Error($"❌ NugetUtil completed, but no .nuspec was created under '{nugetOutputRoot}'");
                return false;
            }

            packagePath = packageFiles[0];
            nuspecPath = nuspecFiles[0];

            MessageLogger.Info($"📦 NuGet package: {packagePath}");
            MessageLogger.Info($"📄 Nuspec: {nuspecPath}");
            MessageLogger.Info("✅ NugetUtil packaging completed");
            return true;
        }

        private static bool CopyNuspecToModelRoot(ProfileEnvironmentModel model, string nuspecPath)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (nuspecPath.IsNullOrEmpty() || !File.Exists(nuspecPath))
            {
                MessageLogger.Error($"❌ Generated nuspec '{nuspecPath}' could not be found");
                return false;
            }

            if (!TryGetModelPackageRoot(model, out var modelPackageRoot))
            {
                MessageLogger.Error($"❌ Could not resolve a model root folder for '{model.ModelName}' to place the nuspec");
                return false;
            }

            Directory.CreateDirectory(modelPackageRoot);

            var destinationNuspecPath = Path.Combine(modelPackageRoot, Path.GetFileName(nuspecPath));
            if (!AreSameFilePath(nuspecPath, destinationNuspecPath))
                File.Copy(nuspecPath, destinationNuspecPath, overwrite: true);

            MessageLogger.Info($"📄 Nuspec copied to model root: {destinationNuspecPath}");
            return true;
        }

        private static bool RunProcess(ProcessStartInfo processStartInfo, out string combinedOutput)
        {
            combinedOutput = string.Empty;

            try
            {
                using var process = new Process { StartInfo = processStartInfo };
                if (!process.Start())
                    return false;

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                combinedOutput = string.Join(Environment.NewLine, new[] { stdout, stderr }
                    .Where(text => !string.IsNullOrWhiteSpace(text)));

                return process.ExitCode == 0;
            }
            catch (Exception exception)
            {
                combinedOutput = exception.Message;
                return false;
            }
        }

        private static string ResolveMsBuildExecutable()
        {
            var visualStudioCandidate = ResolveVisualStudioMsBuildExecutable();
            if (!visualStudioCandidate.IsNullOrEmpty())
                return visualStudioCandidate;

            foreach (var candidate in EnumerateExecutablesFromPath("msbuild.exe"))
            {
                if (IsSupportedMsBuildExecutable(candidate))
                    return candidate;
            }

            return string.Empty;
        }

        private static string ResolveVisualStudioMsBuildExecutable()
        {
            var vsWhereCandidate = ResolveVsWhereMsBuildExecutable();
            if (!vsWhereCandidate.IsNullOrEmpty())
                return vsWhereCandidate;

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            foreach (var visualStudioVersion in new[] { "18", "2022", "2019" })
            {
                var visualStudioRoot = Path.Combine(programFilesX86, "Microsoft Visual Studio", visualStudioVersion);
                if (!Directory.Exists(visualStudioRoot))
                    continue;

                foreach (var editionPath in Directory.GetDirectories(visualStudioRoot)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var candidate = Path.Combine(editionPath, "MSBuild", "Current", "Bin", "MSBuild.exe");
                    if (IsSupportedMsBuildExecutable(candidate))
                        return candidate;
                }
            }

            return string.Empty;
        }

        private static string ResolveVsWhereMsBuildExecutable()
        {
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var vsWherePath = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (!File.Exists(vsWherePath))
                return string.Empty;

            var processStartInfo = new ProcessStartInfo
            {
                FileName = vsWherePath,
                Arguments = "-products * -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!RunProcess(processStartInfo, out var output))
                return string.Empty;

            return output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(IsSupportedMsBuildExecutable) ?? string.Empty;
        }

        private static bool IsSupportedMsBuildExecutable(string? candidate)
        {
            if (candidate.IsNullOrEmpty() || !File.Exists(candidate))
                return false;

            var normalizedCandidate = candidate.Replace('/', '\\');

            if (normalizedCandidate.Contains(@"\Microsoft Visual Studio\", StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalizedCandidate.Contains(@"\dotnet\sdk\", StringComparison.OrdinalIgnoreCase))
                return true;

            if (normalizedCandidate.Contains(@"\Windows\Microsoft.NET\Framework\", StringComparison.OrdinalIgnoreCase)
                || normalizedCandidate.Contains(@"\Windows\Microsoft.NET\Framework64\", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static void ApplyDotNetSdkEnvironmentIfNeeded(ProcessStartInfo processStartInfo, string msbuildExecutable)
        {
            if (CanResolveMicrosoftNetSdk(msbuildExecutable))
                return;

            ApplyDotNetSdkEnvironment(processStartInfo);
        }

        private static bool CanResolveMicrosoftNetSdk(string msbuildExecutable)
        {
            if (msbuildExecutable.IsNullOrEmpty() || !File.Exists(msbuildExecutable))
                return false;

            var msbuildDirectory = Path.GetDirectoryName(msbuildExecutable);
            if (msbuildDirectory.IsNullOrEmpty())
                return false;

            var sdkPath = Path.GetFullPath(Path.Combine(msbuildDirectory, "..", "..", "Sdks", "Microsoft.NET.Sdk", "Sdk"));
            return Directory.Exists(sdkPath);
        }

        private static void ApplyDotNetSdkEnvironment(ProcessStartInfo processStartInfo)
        {
            var dotNetRoot = ResolveDotNetRoot();
            var sdkRoot = ResolveDotNetSdkRoot(dotNetRoot);

            if (dotNetRoot.IsNullOrEmpty() || sdkRoot.IsNullOrEmpty())
                return;

            var sdkResolverPath = Path.Combine(sdkRoot, "Sdks");
            if (!Directory.Exists(sdkResolverPath))
                return;

            processStartInfo.Environment["DOTNET_ROOT"] = dotNetRoot;
            processStartInfo.Environment["MSBuildSDKsPath"] = sdkResolverPath;
            processStartInfo.Environment["MSBuildEnableWorkloadResolver"] = "false";

            var existingPath = processStartInfo.Environment.TryGetValue("PATH", out var path)
                ? path ?? string.Empty
                : Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            if (!existingPath
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(entry => AreSameDirectoryPath(entry, dotNetRoot)))
            {
                processStartInfo.Environment["PATH"] = $"{dotNetRoot}{Path.PathSeparator}{existingPath}";
            }

            MessageLogger.LogOnly($"Using .NET SDK resolver path: {sdkResolverPath}");
        }

        private static string ResolveDotNetRoot()
        {
            var configuredRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!configuredRoot.IsNullOrEmpty() && Directory.Exists(configuredRoot))
                return configuredRoot;

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesCandidate = Path.Combine(programFiles, "dotnet");
            if (Directory.Exists(programFilesCandidate))
                return programFilesCandidate;

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var programFilesX86Candidate = Path.Combine(programFilesX86, "dotnet");
            if (Directory.Exists(programFilesX86Candidate))
                return programFilesX86Candidate;

            return EnumerateExecutablesFromPath("dotnet.exe")
                .Select(Path.GetDirectoryName)
                .FirstOrDefault(path => !path.IsNullOrEmpty() && Directory.Exists(path)) ?? string.Empty;
        }

        private static string ResolveDotNetSdkRoot(string dotNetRoot)
        {
            if (dotNetRoot.IsNullOrEmpty())
                return string.Empty;

            var sdkContainer = Path.Combine(dotNetRoot, "sdk");
            if (!Directory.Exists(sdkContainer))
                return string.Empty;

            return Directory.GetDirectories(sdkContainer)
                .Where(path => Directory.Exists(Path.Combine(path, "Sdks", "Microsoft.NET.Sdk", "Sdk")))
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault() ?? string.Empty;
        }

        private static string SummarizeNugetInstallFailure(string packageId, string installRoot, string rawMessage)
        {
            if (IsLikelyLongPathFailure(rawMessage))
                return $"Package extraction hit a Windows path-length limit under '{installRoot}'";

            if (rawMessage.Contains("Unknown option", StringComparison.OrdinalIgnoreCase))
                return "NuGet rejected one of the command-line arguments";

            if (rawMessage.Contains("Unable to load the service index", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("No such host is known", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                return "NuGet could not reach one of the configured feeds";
            }

            if (rawMessage.Contains("401", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("403", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("forbidden", StringComparison.OrdinalIgnoreCase))
            {
                return "NuGet could not authenticate to one of the configured feeds";
            }

            return $"NuGet install failed for '{packageId}'";
        }

        private static bool IsLikelyLongPathFailure(string rawMessage)
        {
            if (rawMessage.IsNullOrEmpty())
                return false;

            return rawMessage.Contains("Could not find a part of the path", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("The specified path, file name, or both are too long", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("fully qualified file name must be less than", StringComparison.OrdinalIgnoreCase)
                || rawMessage.Contains("path too long", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsLongPathsEnabled()
        {
            try
            {
                var rawValue = Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem",
                    "LongPathsEnabled",
                    0);

                return rawValue is int intValue && intValue != 0;
            }
            catch
            {
                return false;
            }
        }

        private static string ResolveNugetUtilExecutable()
        {
            if (File.Exists(NugetUtilDefaultPath))
                return NugetUtilDefaultPath;

            return EnumerateExecutablesFromPath("nugetutil.exe").FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private static IEnumerable<string> EnumerateExecutablesFromPath(string fileName)
        {
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var pathDirectories = pathValue
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var pathDirectory in pathDirectories)
            {
                yield return Path.Combine(pathDirectory, fileName);
            }
        }

        private static string GetModelArtifactsRoot(ProfileModel profile, ProfileEnvironmentModel model)
        {
            var repoRoot = profile.TryGetRepoRootFolder(model);
            var baseRoot = repoRoot.IsNullOrEmpty() ? model.ModelRootFolder : repoRoot;

            if (baseRoot.IsNullOrEmpty())
                baseRoot = Directory.GetCurrentDirectory();

            return Path.Combine(baseRoot, "Artifacts");
        }

        private string ResolvePreferredBuildPackagesRoot()
        {
            return Path.Combine(_deployablePackagesRoot, "BuildPackages");
        }

        private IEnumerable<string> GetBuildPackageSearchRoots(string preferredBuildPackagesRoot)
        {
            if (!preferredBuildPackagesRoot.IsNullOrEmpty())
                yield return preferredBuildPackagesRoot;

            var legacyBuildPackagesRoot = Path.Combine(_deployablePackagesRoot, "BuildPackages");
            if (!legacyBuildPackagesRoot.IsNullOrEmpty()
                && !AreSameDirectoryPath(preferredBuildPackagesRoot, legacyBuildPackagesRoot))
            {
                yield return legacyBuildPackagesRoot;
            }
        }

        private static void EnsureCleanDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);

            Directory.CreateDirectory(path);
        }

        private bool TryResolveBuiltPayloadRoot(string buildOutputRoot, string modelName, out string payloadRoot)
        {
            payloadRoot = string.Empty;

            var directPayloadRoot = Path.Combine(buildOutputRoot, "Bin", modelName);
            if (IsValidPayloadRoot(directPayloadRoot, modelName))
            {
                payloadRoot = directPayloadRoot;
                return true;
            }

            var directBinRoot = Path.Combine(buildOutputRoot, "Bin");
            if (IsValidPayloadRoot(directBinRoot, modelName))
            {
                payloadRoot = directBinRoot;
                return true;
            }

            var xrefPath = Directory.Exists(buildOutputRoot)
                ? Directory.GetFiles(buildOutputRoot, $"{modelName}.xref", SearchOption.AllDirectories)
                    .OrderBy(path => path.Count(character => character == Path.DirectorySeparatorChar))
                    .FirstOrDefault()
                : null;

            if (xrefPath.IsNullOrEmpty())
            {
                var packagesLocalPayloadRoot = Path.Combine(_deploymentBasePath, modelName);
                if (IsValidPayloadRoot(packagesLocalPayloadRoot, modelName))
                {
                    payloadRoot = packagesLocalPayloadRoot;
                    return true;
                }

                return false;
            }

            var candidateRoot = Path.GetDirectoryName(xrefPath!) ?? string.Empty;
            if (!IsValidPayloadRoot(candidateRoot, modelName))
                return false;

            payloadRoot = candidateRoot;
            return true;
        }

        private static bool AreSameDirectoryPath(string leftPath, string rightPath)
        {
            if (leftPath.IsNullOrEmpty() || rightPath.IsNullOrEmpty())
                return false;

            var normalizedLeftPath = Path.GetFullPath(leftPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRightPath = Path.GetFullPath(rightPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return normalizedLeftPath.Equals(normalizedRightPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPathWithinDirectory(string candidatePath, string directoryPath)
        {
            if (candidatePath.IsNullOrEmpty() || directoryPath.IsNullOrEmpty())
                return false;

            var normalizedCandidatePath = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedDirectoryPath = Path.GetFullPath(directoryPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (normalizedCandidatePath.Equals(normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase))
                return true;

            var directoryPrefix = normalizedDirectoryPath + Path.DirectorySeparatorChar;
            return normalizedCandidatePath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool AreSameFilePath(string leftPath, string rightPath)
        {
            if (leftPath.IsNullOrEmpty() || rightPath.IsNullOrEmpty())
                return false;

            var normalizedLeftPath = Path.GetFullPath(leftPath);
            var normalizedRightPath = Path.GetFullPath(rightPath);
            return normalizedLeftPath.Equals(normalizedRightPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetModelPackageRoot(ProfileEnvironmentModel model, out string modelPackageRoot)
        {
            modelPackageRoot = string.Empty;

            var projectRoot = model.ProjectFilePath.IsNullOrEmpty()
                ? string.Empty
                : Path.GetDirectoryName(Path.GetFullPath(model.ProjectFilePath)) ?? string.Empty;

            if (!projectRoot.IsNullOrEmpty())
            {
                modelPackageRoot = projectRoot;
                return true;
            }

            if (!model.ModelRootFolder.IsNullOrEmpty())
            {
                modelPackageRoot = Path.GetFullPath(model.ModelRootFolder);
                return true;
            }

            if (!model.MetadataFolder.IsNullOrEmpty())
            {
                modelPackageRoot = Path.GetFullPath(model.MetadataFolder);
                return true;
            }

            return false;
        }

        private static bool IsValidPayloadRoot(string payloadRoot, string modelName)
        {
            if (payloadRoot.IsNullOrEmpty() || !Directory.Exists(payloadRoot))
                return false;

            return File.Exists(Path.Combine(payloadRoot, $"{modelName}.xref"))
                || File.Exists(Path.Combine(payloadRoot, "bin", $"Dynamics.AX.{modelName}.dll"));
        }


        private string ResolveNuGetExecutable(out IReadOnlyList<string> searchLocations)
        {
            var candidates = GetNuGetExecutableCandidates().ToList();
            searchLocations = candidates;

            return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private IEnumerable<string> GetNuGetExecutableCandidates()
        {
            if (!_config.NuGetExecutablePath.IsNullOrEmpty())
                yield return Environment.ExpandEnvironmentVariables(_config.NuGetExecutablePath);

            var appBaseDirectory = AppContext.BaseDirectory;
            yield return Path.Combine(appBaseDirectory, "nuget.exe");
            yield return Path.Combine(appBaseDirectory, "Tools", "nuget.exe");

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!localAppData.IsNullOrEmpty())
                yield return Path.Combine(localAppData, "FODevManager", "Tools", "nuget.exe");

            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!programFilesX86.IsNullOrEmpty())
                yield return Path.Combine(programFilesX86, "NuGet", "nuget.exe");

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!programFiles.IsNullOrEmpty())
                yield return Path.Combine(programFiles, "NuGet", "nuget.exe");

            foreach (var candidate in EnumerateExecutablesFromPath("nuget.exe"))
                yield return candidate;

            foreach (var candidate in EnumerateExecutablesFromPath("nuget"))
                yield return candidate;
        }

        private sealed record PackageContext(string RepositoryRoot, string IsvConfigPath, string NugetConfigPath, IReadOnlyList<PackageReference> Packages);
        private sealed record PackageReference(string Id, string Version)
        {
            public string GetKey() => $"{Id}|{Version}";
        }

        private sealed record MsBuildContext(string CompilerPackageRoot, string BuildTasksDirectory, string MetadataDirectory, string ReferenceFolder);
        private sealed record ResolvedModelDescriptor(PackageReference PackageReference, string ModelName, string ModelFolder);
    }
}





