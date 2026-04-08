using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Utils;
using System.Diagnostics;
using System.Xml.Linq;

namespace FODevManager.Services
{
    public sealed class DeployablePackageService
    {
        private readonly string _deployablePackagesRoot;
        private readonly string _deploymentBasePath;
        private readonly string _azureArtifactsUsername;
        private readonly string _azureArtifactsPat;
        private readonly string _azureArtifactsApiKey;

        public DeployablePackageService(AppConfig config)
        {
            _deployablePackagesRoot = config.DeployablePackages;
            _deploymentBasePath = config.DeploymentBasePath;
            _azureArtifactsUsername = config.AzureArtifactsUsername;
            _azureArtifactsPat = config.AzureArtifactsPat;
            _azureArtifactsApiKey = config.AzureArtifactsApiKey;

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

            if (_deployablePackagesRoot.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ DeployablePackages is not configured. Cannot prepare compiled NuGet models.");
                return false;
            }

            if (!TryGetPackageContext(repository, out var packageContext))
                return false;

            var extractedPackages = new List<PackageReference>();
            foreach (var packageReference in packageContext.Packages)
            {
                if (EnsurePackageExtracted(packageReference, packageContext))
                    extractedPackages.Add(packageReference);
            }

            if (extractedPackages.Count == 0)
            {
                MessageLogger.Warning($"⚠️ No deployable packages could be prepared for repository '{repository.DisplayName}'.");
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
                MessageLogger.Warning($"⚠️ Compiled NuGet model '{model.ModelName}' is not mapped to a repository. Skipping package preparation.");
                return false;
            }

            return EnsureCompiledNugetModels(profile, repository);
        }

        public bool AddOrUpdatePackageFromUrl(ProfileModel profile, RepositoryModel repository, string packageUrl)
        {
            if (profile == null || repository == null || packageUrl.IsNullOrEmpty())
                return false;

            if (!TryParseNugetPackageUrl(packageUrl, out var packageId, out var packageVersion))
            {
                MessageLogger.Error($"❌ Unsupported package URL '{packageUrl}'.");
                return false;
            }

            if (!TryGetIsvConfigPath(repository, createIfMissing: true, out var isvConfigPath))
            {
                MessageLogger.Error($"❌ Could not locate Build\\isv.config for repository '{repository.DisplayName}'.");
                return false;
            }

            UpsertPackageReference(isvConfigPath, packageId, packageVersion);
            return EnsureCompiledNugetModels(profile, repository);
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
                MessageLogger.Error($"❌ Unsupported package URL '{packageUrl}'.");
                return false;
            }

            if (!TryGetIsvConfigPath(repository, createIfMissing: true, out var isvConfigPath))
            {
                MessageLogger.Error($"❌ Could not locate Build\\isv.config for repository '{repository.DisplayName}'.");
                return false;
            }

            if (!existingPackageId.IsNullOrEmpty() && !existingPackageId.SameAs(newPackageId))
            {
                RemovePackageReference(isvConfigPath, existingPackageId);
            }

            UpsertPackageReference(isvConfigPath, newPackageId, newPackageVersion);
            return EnsureCompiledNugetModels(profile, repository);
        }

        private bool TryGetPackageContext(RepositoryModel repository, out PackageContext context)
        {
            context = null!;

            var repositoryRoot = (repository.RepoRootFolder ?? string.Empty).Trim();
            if (repositoryRoot.IsNullOrEmpty() || !Directory.Exists(repositoryRoot))
            {
                MessageLogger.LogOnly($"Repository root folder is missing for '{repository.DisplayName}'. Cannot prepare compiled NuGet models.");
                return false;
            }

            var nugetConfigPath = FindRepositoryConfigFile(repositoryRoot, "nuget.config");
            if (nugetConfigPath.IsNullOrEmpty())
            {
                MessageLogger.LogOnly($"No nuget.config found under repository '{repository.DisplayName}'.");
                return false;
            }

            var isvConfigPath = FindRepositoryConfigFile(repositoryRoot, "isv.config");
            if (isvConfigPath.IsNullOrEmpty())
            {
                MessageLogger.LogOnly($"No isv.config found under repository '{repository.DisplayName}'.");
                return false;
            }

            var packages = LoadIsvPackageReferences(isvConfigPath);
            if (packages.Count == 0)
            {
                MessageLogger.LogOnly($"No package entries found in Build\\isv.config for repository '{repository.DisplayName}'.");
                return false;
            }

            context = new PackageContext(repositoryRoot, isvConfigPath, nugetConfigPath, packages);
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
                    descriptors.Add(new ResolvedModelDescriptor(packageReference, Path.GetFileName(modelFolder), modelFolder));
                }
            }

            var existingNugetModels = (repository.Models ?? new List<ProfileEnvironmentModel>())
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
                repository.Models.Remove(staleModel);
                updated = true;
            }

            return updated;
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

        private bool EnsurePackageExtracted(PackageReference packageReference, PackageContext context)
        {
            var extractedRoot = GetExtractedPackageRoot(packageReference);
            FileHelper.EnsureDirectoryExists(extractedRoot);

            if (!HasExtractedPackageContent(extractedRoot))
            {
                var downloadedRoot = Path.Combine(extractedRoot, ".download");
                FileHelper.EnsureDirectoryExists(downloadedRoot);

                if (!EnsurePackageDownloaded(packageReference, context, downloadedRoot, out var installedPackageFolder))
                    return false;

                if (!HasCompiledPackageContent(installedPackageFolder))
                {
                    MessageLogger.Warning($"⚠️ NuGet package '{packageReference.Id} {packageReference.Version}' did not contain compiled model files.");
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

        private bool EnsurePackageDownloaded(PackageReference packageReference, PackageContext context, string downloadedRoot, out string installedPackageFolder)
        {
            installedPackageFolder = Path.Combine(downloadedRoot, $"{packageReference.Id}.{packageReference.Version}");
            if (Directory.Exists(installedPackageFolder) && Directory.EnumerateFileSystemEntries(installedPackageFolder).Any())
                return true;

            var nugetExecutable = ResolveNuGetExecutable();
            if (nugetExecutable.IsNullOrEmpty())
            {
                MessageLogger.Error("❌ Could not locate nuget.exe on PATH. Cannot download deployable packages.");
                return false;
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = nugetExecutable,
                Arguments = $"install \"{packageReference.Id}\" -Version \"{packageReference.Version}\" -OutputDirectory \"{downloadedRoot}\" -ConfigFile \"{context.NugetConfigPath}\" -NonInteractive",
                WorkingDirectory = context.RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            ApplyAzureArtifactsCredentials(processStartInfo, context.NugetConfigPath);

            try
            {
                using var process = new Process { StartInfo = processStartInfo };
                if (!process.Start())
                {
                    MessageLogger.Error($"❌ Failed to start nuget for package '{packageReference.Id}'.");
                    return false;
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    var output = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(text => !text.IsNullOrEmpty()));
                    MessageLogger.Error($"❌ NuGet install failed for '{packageReference.Id} {packageReference.Version}'. {output}".Trim());
                    return false;
                }

                MessageLogger.Info($"📦 Downloaded deployable package '{packageReference.Id} {packageReference.Version}'.");
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

        private void ApplyAzureArtifactsCredentials(ProcessStartInfo processStartInfo, string nugetConfigPath)
        {
            var secret = ResolveAzureArtifactsSecret();
            if (secret.IsNullOrEmpty())
                return;

            var feedEndpoints = LoadAzureArtifactsFeedEndpoints(nugetConfigPath);
            if (feedEndpoints.Count == 0)
                return;

            var username = _azureArtifactsUsername.IsNullOrEmpty() ? "FODevManager" : _azureArtifactsUsername;
            var endpointCredentials = string.Join(",",
                feedEndpoints.Select(endpoint =>
                    $"{{\"endpoint\":\"{EscapeJson(endpoint)}\",\"username\":\"{EscapeJson(username)}\",\"password\":\"{EscapeJson(secret)}\"}}"));

            processStartInfo.Environment["VSS_NUGET_EXTERNAL_FEED_ENDPOINTS"] =
                $"{{\"endpointCredentials\":[{endpointCredentials}]}}";
        }

        private string ResolveAzureArtifactsSecret()
        {
            if (!_azureArtifactsPat.IsNullOrEmpty())
                return _azureArtifactsPat;

            return _azureArtifactsApiKey ?? string.Empty;
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
        private static string? ResolveNuGetExecutable()
        {
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var pathDirectories = pathValue
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var pathDirectory in pathDirectories)
            {
                var candidate = Path.Combine(pathDirectory, "nuget.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            foreach (var pathDirectory in pathDirectories)
            {
                var candidate = Path.Combine(pathDirectory, "nuget");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private sealed record PackageContext(string RepositoryRoot, string IsvConfigPath, string NugetConfigPath, IReadOnlyList<PackageReference> Packages);
        private sealed record PackageReference(string Id, string Version)
        {
            public string GetKey() => $"{Id}|{Version}";
        }

        private sealed record ResolvedModelDescriptor(PackageReference PackageReference, string ModelName, string ModelFolder);
    }
}





