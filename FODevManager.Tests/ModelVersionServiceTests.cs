using FODevManager.Models;
using FODevManager.Services;
using System.IO;
using System.Reflection;

namespace FODevManager.Tests
{
    [TestFixture]
    public class ModelVersionServiceTests
    {
        private string _baseDir = string.Empty;
        private ModelVersionService _service = null!;

        [SetUp]
        public void Setup()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "FODevManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
            _service = new ModelVersionService();
        }

        [TearDown]
        public void Teardown()
        {
            if (Directory.Exists(_baseDir))
                Directory.Delete(_baseDir, true);
        }

        [Test]
        public void TryGetVersionText_Should_Read_Source_Model_Version_From_Descriptor()
        {
            var modelName = "TestModel";
            var metadataFolder = Path.Combine(_baseDir, "Metadata", modelName);
            var descriptorFolder = Path.Combine(metadataFolder, "Descriptor");
            Directory.CreateDirectory(descriptorFolder);

            File.WriteAllText(
                Path.Combine(descriptorFolder, $"{modelName}.xml"),
                """
                <AxModelInfo>
                  <VersionBuild>0</VersionBuild>
                  <VersionMajor>1</VersionMajor>
                  <VersionMinor>2</VersionMinor>
                  <VersionRevision>3</VersionRevision>
                </AxModelInfo>
                """);

            var model = new ProfileEnvironmentModel
            {
                ModelName = modelName,
                MetadataFolder = metadataFolder,
                ModelType = ModelType.Source
            };

            var ok = _service.TryGetVersionText(model, out var versionText);

            Assert.That(ok, Is.True);
            Assert.That(versionText, Is.EqualTo("1.2.3"));
        }

        [Test]
        public void TryUpdateSourceVersion_Should_Update_Descriptor_Without_Touching_Build()
        {
            var modelName = "EditModel";
            var metadataFolder = Path.Combine(_baseDir, "Metadata", modelName);
            var descriptorFolder = Path.Combine(metadataFolder, "Descriptor");
            Directory.CreateDirectory(descriptorFolder);

            var descriptorPath = Path.Combine(descriptorFolder, $"{modelName}.xml");
            File.WriteAllText(
                descriptorPath,
                """
                <AxModelInfo>
                  <VersionBuild>9</VersionBuild>
                  <VersionMajor>1</VersionMajor>
                  <VersionMinor>0</VersionMinor>
                  <VersionRevision>0</VersionRevision>
                </AxModelInfo>
                """);

            var model = new ProfileEnvironmentModel
            {
                ModelName = modelName,
                MetadataFolder = metadataFolder,
                ModelType = ModelType.Source
            };

            var ok = _service.TryUpdateSourceVersion(model, new ModelVersion(2, 4, 6));
            var xml = File.ReadAllText(descriptorPath);

            Assert.That(ok, Is.True);
            Assert.That(xml, Does.Contain("<VersionBuild>9</VersionBuild>"));
            Assert.That(xml, Does.Contain("<VersionMajor>2</VersionMajor>"));
            Assert.That(xml, Does.Contain("<VersionMinor>4</VersionMinor>"));
            Assert.That(xml, Does.Contain("<VersionRevision>6</VersionRevision>"));
        }

        [Test]
        public void TryGetVersionText_Should_Read_Compiled_Model_Version_From_Bin_Dll()
        {
            var modelName = "CompiledModel";
            var compiledFolder = Path.Combine(_baseDir, "Libs", modelName);
            var binFolder = Path.Combine(_baseDir, "bin");
            Directory.CreateDirectory(compiledFolder);
            Directory.CreateDirectory(binFolder);

            File.Copy(
                Assembly.GetExecutingAssembly().Location,
                Path.Combine(binFolder, $"Dynamics.AX.{modelName}.dll"),
                overwrite: true);

            var model = new ProfileEnvironmentModel
            {
                ModelName = modelName,
                CompiledModelFolder = compiledFolder,
                ModelType = ModelType.Compiled
            };

            var ok = _service.TryGetVersionText(model, out var versionText);

            Assert.That(ok, Is.True);
            Assert.That(versionText, Does.Match(@"\d+\.\d+\.\d+"));
        }

        [Test]
        public void TryUpdateSourceVersion_Should_Update_Matching_Nuspec_When_Present()
        {
            var modelName = "PackagedModel";
            var modelRoot = Path.Combine(_baseDir, "RepoRoot");
            var metadataFolder = Path.Combine(modelRoot, "Metadata", modelName);
            var descriptorFolder = Path.Combine(metadataFolder, "Descriptor");
            var nuspecFolder = Path.Combine(modelRoot, "Packaging");
            Directory.CreateDirectory(descriptorFolder);
            Directory.CreateDirectory(nuspecFolder);

            var descriptorPath = Path.Combine(descriptorFolder, $"{modelName}.xml");
            var nuspecPath = Path.Combine(nuspecFolder, $"{modelName}.nuspec");

            File.WriteAllText(
                descriptorPath,
                """
                <AxModelInfo>
                  <VersionBuild>0</VersionBuild>
                  <VersionMajor>1</VersionMajor>
                  <VersionMinor>0</VersionMinor>
                  <VersionRevision>0</VersionRevision>
                </AxModelInfo>
                """);

            File.WriteAllText(
                nuspecPath,
                """
                <package>
                  <metadata>
                    <id>PackagedModel</id>
                    <version>1.0.0</version>
                  </metadata>
                </package>
                """);

            var model = new ProfileEnvironmentModel
            {
                ModelName = modelName,
                ModelRootFolder = modelRoot,
                MetadataFolder = metadataFolder,
                ModelType = ModelType.Source
            };

            var ok = _service.TryUpdateSourceVersion(model, new ModelVersion(3, 5, 7));

            Assert.That(ok, Is.True);
            Assert.That(File.ReadAllText(descriptorPath), Does.Contain("<VersionMajor>3</VersionMajor>"));
            Assert.That(File.ReadAllText(nuspecPath), Does.Contain("<version>3.5.7</version>"));
        }

        [Test]
        public void TryUpdateSourceVersion_Should_Not_Update_Unrelated_Nuspec()
        {
            var modelName = "FocusedModel";
            var modelRoot = Path.Combine(_baseDir, "RepoRoot");
            var metadataFolder = Path.Combine(modelRoot, "Metadata", modelName);
            var descriptorFolder = Path.Combine(metadataFolder, "Descriptor");
            Directory.CreateDirectory(descriptorFolder);

            var descriptorPath = Path.Combine(descriptorFolder, $"{modelName}.xml");
            var unrelatedNuspecPath = Path.Combine(modelRoot, "AnotherPackage.nuspec");

            File.WriteAllText(
                descriptorPath,
                """
                <AxModelInfo>
                  <VersionBuild>0</VersionBuild>
                  <VersionMajor>1</VersionMajor>
                  <VersionMinor>0</VersionMinor>
                  <VersionRevision>0</VersionRevision>
                </AxModelInfo>
                """);

            File.WriteAllText(
                unrelatedNuspecPath,
                """
                <package>
                  <metadata>
                    <id>AnotherPackage</id>
                    <version>9.9.9</version>
                  </metadata>
                </package>
                """);

            var model = new ProfileEnvironmentModel
            {
                ModelName = modelName,
                ModelRootFolder = modelRoot,
                MetadataFolder = metadataFolder,
                ModelType = ModelType.Source
            };

            var ok = _service.TryUpdateSourceVersion(model, new ModelVersion(2, 1, 4));

            Assert.That(ok, Is.True);
            Assert.That(File.ReadAllText(descriptorPath), Does.Contain("<VersionRevision>4</VersionRevision>"));
            Assert.That(File.ReadAllText(unrelatedNuspecPath), Does.Contain("<version>9.9.9</version>"));
        }
    }
}
