using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Utils;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FODevManager.Services
{
    public sealed class ModelVersionService
    {
        private static readonly Regex VersionPattern = new(@"(?<major>\d+)\.(?<minor>\d+)\.(?<revision>\d+)(?:\.\d+)?", RegexOptions.Compiled);

        public bool TryGetVersion(ProfileEnvironmentModel model, out ModelVersion version)
        {
            version = default;

            if (model == null)
                return false;

            return model.ModelType switch
            {
                ModelType.Source => TryReadSourceVersion(model, out version),
                ModelType.Compiled or ModelType.CompiledNuget => TryReadCompiledVersion(model, out version),
                _ => false
            };
        }

        public bool TryGetVersionText(ProfileEnvironmentModel model, out string versionText)
        {
            versionText = string.Empty;

            if (!TryGetVersion(model, out var version))
                return false;

            versionText = version.ToString();
            return true;
        }

        public bool TryUpdateSourceVersion(ProfileEnvironmentModel model, ModelVersion version)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (model.ModelType != ModelType.Source)
            {
                MessageLogger.Warning($"Version editing is only supported for source models. '{model.ModelName}' is {model.ModelType}");
                return false;
            }

            var descriptorFilePath = GetDescriptorFilePath(model);
            if (descriptorFilePath.IsNullOrEmpty())
            {
                MessageLogger.Error($"Could not resolve descriptor file for model '{model.ModelName}'");
                return false;
            }

            try
            {
                XDocument document;
                XElement root;

                if (File.Exists(descriptorFilePath))
                {
                    document = XDocument.Load(descriptorFilePath);
                    root = document.Root ?? new XElement("AxModelInfo");

                    if (document.Root == null)
                        document.Add(root);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(descriptorFilePath)!);
                    root = new XElement("AxModelInfo");
                    document = new XDocument(root);
                }

                SetOrAddElement(root, "VersionMajor", version.Major.ToString());
                SetOrAddElement(root, "VersionMinor", version.Minor.ToString());
                SetOrAddElement(root, "VersionRevision", version.Revision.ToString());

                if (root.Element("VersionBuild") == null)
                    root.Add(new XElement("VersionBuild", "0"));

                document.Save(descriptorFilePath);

                if (!TryUpdateMatchingNuspecVersion(model, version))
                    return false;

                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"Failed to update source version for model '{model.ModelName}': {exception.Message}");
                return false;
            }
        }

        private static bool TryReadSourceVersion(ProfileEnvironmentModel model, out ModelVersion version)
        {
            version = default;

            var descriptorFilePath = GetDescriptorFilePath(model);
            if (descriptorFilePath.IsNullOrEmpty() || !File.Exists(descriptorFilePath))
                return false;

            try
            {
                var document = XDocument.Load(descriptorFilePath);
                var majorValue = document.Descendants("VersionMajor").FirstOrDefault()?.Value;
                var minorValue = document.Descendants("VersionMinor").FirstOrDefault()?.Value;
                var revisionValue = document.Descendants("VersionRevision").FirstOrDefault()?.Value;

                if (!int.TryParse(majorValue, out var major)
                    || !int.TryParse(minorValue, out var minor)
                    || !int.TryParse(revisionValue, out var revision))
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

        private static bool TryReadCompiledVersion(ProfileEnvironmentModel model, out ModelVersion version)
        {
            version = default;

            var dllPath = ResolveCompiledAssemblyPath(model);
            if (dllPath.IsNullOrEmpty() || !File.Exists(dllPath))
                return false;

            try
            {
                var fileInfo = FileVersionInfo.GetVersionInfo(dllPath);
                var candidate = fileInfo.FileVersion;

                if (candidate.IsNullOrEmpty())
                    candidate = fileInfo.ProductVersion;

                if (candidate.IsNullOrEmpty())
                    return false;

                var match = VersionPattern.Match(candidate);
                if (!match.Success)
                    return false;

                version = new ModelVersion(
                    int.Parse(match.Groups["major"].Value),
                    int.Parse(match.Groups["minor"].Value),
                    int.Parse(match.Groups["revision"].Value));

                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"Failed to read compiled version for model '{model.ModelName}': {exception.Message}");
                return false;
            }
        }

        private static string GetDescriptorFilePath(ProfileEnvironmentModel model)
        {
            if (model.MetadataFolder.IsNullOrEmpty() || model.ModelName.IsNullOrEmpty())
                return string.Empty;

            return Path.Combine(model.MetadataFolder, "Descriptor", $"{model.ModelName}.xml");
        }

        private static string ResolveCompiledAssemblyPath(ProfileEnvironmentModel model)
        {
            var assemblyNames = new[] { model.PackageId, model.ModelName }
                .Where(value => !value.IsNullOrEmpty())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var binFolder in GetCandidateBinFolders(model))
            {
                foreach (var assemblyName in assemblyNames)
                {
                    var candidate = Path.Combine(binFolder, $"Dynamics.AX.{assemblyName}.dll");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return string.Empty;
        }

        private static IEnumerable<string> GetCandidateBinFolders(ProfileEnvironmentModel model)
        {
            var candidates = new List<string>();

            if (!model.CompiledModelFolder.IsNullOrEmpty())
            {
                candidates.Add(Path.Combine(model.CompiledModelFolder, "bin"));

                var compiledFolderParent = Directory.GetParent(model.CompiledModelFolder)?.FullName;
                if (!compiledFolderParent.IsNullOrEmpty())
                    candidates.Add(Path.Combine(compiledFolderParent, "bin"));

                var compiledFolderGrandParent = !compiledFolderParent.IsNullOrEmpty()
                    ? Directory.GetParent(compiledFolderParent)?.FullName
                    : null;
                if (!compiledFolderGrandParent.IsNullOrEmpty())
                    candidates.Add(Path.Combine(compiledFolderGrandParent, "bin"));
            }

            if (!model.ModelRootFolder.IsNullOrEmpty())
                candidates.Add(Path.Combine(model.ModelRootFolder, "bin"));

            return candidates
                .Where(path => !path.IsNullOrEmpty())
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static void SetOrAddElement(XElement root, string elementName, string value)
        {
            var existing = root.Element(elementName);
            if (existing == null)
            {
                root.Add(new XElement(elementName, value));
                return;
            }

            existing.Value = value;
        }

        private static bool TryUpdateMatchingNuspecVersion(ProfileEnvironmentModel model, ModelVersion version)
        {
            var nuspecPath = ResolveMatchingNuspecPath(model);
            if (nuspecPath.IsNullOrEmpty())
                return true;

            try
            {
                var document = XDocument.Load(nuspecPath);
                var metadataElement = document.Root?.Name.LocalName == "package"
                    ? document.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "metadata")
                    : document.Descendants().FirstOrDefault(element => element.Name.LocalName == "metadata");

                if (metadataElement == null)
                {
                    MessageLogger.Warning($"Found nuspec for model '{model.ModelName}', but it has no metadata node: {nuspecPath}");
                    return false;
                }

                var versionElement = metadataElement.Elements().FirstOrDefault(element => element.Name.LocalName == "version");
                if (versionElement == null)
                {
                    metadataElement.Add(new XElement(metadataElement.GetDefaultNamespace() + "version", version.ToString()));
                }
                else
                {
                    versionElement.Value = version.ToString();
                }

                document.Save(nuspecPath);
                return true;
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"Failed to update nuspec version for model '{model.ModelName}': {exception.Message}");
                return false;
            }
        }

        private static string ResolveMatchingNuspecPath(ProfileEnvironmentModel model)
        {
            var searchRoot = ResolveNuspecSearchRoot(model);
            if (searchRoot.IsNullOrEmpty() || !Directory.Exists(searchRoot))
                return string.Empty;

            var candidateNames = new[] { model.ModelName, model.PackageId }
                .Where(value => !value.IsNullOrEmpty())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(value => $"{value}.nuspec")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (candidateNames.Count == 0)
                return string.Empty;

            var matchingFiles = Directory
                .EnumerateFiles(searchRoot, "*.nuspec", SearchOption.AllDirectories)
                .Where(path => candidateNames.Contains(Path.GetFileName(path)))
                .OrderBy(path => GetNuspecPriority(path, model))
                .ThenBy(path => path.Length)
                .ToList();

            return matchingFiles.FirstOrDefault() ?? string.Empty;
        }

        private static string ResolveNuspecSearchRoot(ProfileEnvironmentModel model)
        {
            if (!model.ModelRootFolder.IsNullOrEmpty() && Directory.Exists(model.ModelRootFolder))
                return model.ModelRootFolder;

            if (!model.ProjectFilePath.IsNullOrEmpty())
            {
                var projectDirectory = Path.GetDirectoryName(model.ProjectFilePath);
                if (!projectDirectory.IsNullOrEmpty() && Directory.Exists(projectDirectory))
                    return projectDirectory;
            }

            if (!model.MetadataFolder.IsNullOrEmpty() && Directory.Exists(model.MetadataFolder))
                return model.MetadataFolder;

            return string.Empty;
        }

        private static int GetNuspecPriority(string nuspecPath, ProfileEnvironmentModel model)
        {
            var fileName = Path.GetFileNameWithoutExtension(nuspecPath);

            if (!model.ModelName.IsNullOrEmpty() && fileName.Equals(model.ModelName, StringComparison.OrdinalIgnoreCase))
                return 0;

            if (!model.PackageId.IsNullOrEmpty() && fileName.Equals(model.PackageId, StringComparison.OrdinalIgnoreCase))
                return 1;

            return 2;
        }
    }

    public readonly record struct ModelVersion(int Major, int Minor, int Revision)
    {
        public ModelVersion WithIncrementedRevision() => new(Major, Minor, Revision + 1);

        public override string ToString() => $"{Major}.{Minor}.{Revision}";
    }
}
