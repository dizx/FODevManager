using FODevManager.Models;
using FODevManager.Messages;
using FODevManager.Services;
using FODevManager.Utils;

namespace FODevManager.Tests
{
    [TestFixture]
    public class ModelDeploymentServiceTests
    {
        private string _baseDir = string.Empty;

        [SetUp]
        public void Setup()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "FODevManagerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_baseDir);
        }

        [TearDown]
        public void Teardown()
        {
            if (Directory.Exists(_baseDir))
                Directory.Delete(_baseDir, true);
        }

        [Test]
        public void IsModelActuallyDeployed_Should_Return_False_When_Deployed_Path_Does_Not_Point_To_Current_Compiled_Nuget_Folder()
        {
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var oldDeploymentPath = Path.Combine(deploymentBasePath, "CarModel");
            var currentCompiledFolder = Path.Combine(_baseDir, "DeployablePackages", "CarModel-2.0.0");

            Directory.CreateDirectory(oldDeploymentPath);
            Directory.CreateDirectory(currentCompiledFolder);

            var service = CreateModelDeploymentService(deploymentBasePath);
            var model = new ProfileEnvironmentModel
            {
                ModelName = "CarModel",
                ModelType = ModelType.CompiledNuget,
                CompiledModelFolder = currentCompiledFolder,
                PackageId = "CarModel",
                PackageVersion = "2.0.0"
            };

            Assert.That(service.IsModelActuallyDeployed(model), Is.False);
        }

        [Test]
        public void AddModelToProfileIfNotExists_Should_Block_Source_When_Compiled_Model_With_Same_Name_Exists()
        {
            var profileName = "DuplicateSourceProfile";
            var modelName = "CarModel";
            var modelRoot = Path.Combine(_baseDir, "CarModelRoot");
            var metadataFolder = Path.Combine(modelRoot, "Metadata", modelName);
            var projectFolder = Path.Combine(modelRoot, "Project", modelName);
            var projectFile = Path.Combine(projectFolder, $"{modelName}.rnrproj");

            Directory.CreateDirectory(metadataFolder);
            Directory.CreateDirectory(projectFolder);
            File.WriteAllText(projectFile, string.Empty);

            var profile = new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Compiled,
                        CompiledModelFolder = Path.Combine(_baseDir, "Compiled", modelName)
                    }
                ]
            };
            SaveProfile(profile);

            var service = CreateModelDeploymentService();
            var messages = new List<string>();
            using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));

            service.AddModelToProfileIfNotExists(profileName, modelName, modelRoot, ModelType.Source);

            var savedProfile = LoadProfile(profileName);
            Assert.That(savedProfile.AllModels, Has.Count.EqualTo(1));
            Assert.That(savedProfile.AllModels.Single().ModelType, Is.EqualTo(ModelType.Compiled));
            Assert.That(messages, Has.Some.EqualTo($"❌ Model '{modelName}' already exists in profile '{profileName}'. Skipping add"));
        }

        [Test]
        public void AddModelToProfileIfNotExists_Should_Block_Compiled_When_Source_Model_With_Same_Name_Exists()
        {
            var profileName = "DuplicateCompiledProfile";
            var modelName = "CarModel";
            var compiledFolder = Path.Combine(_baseDir, "Compiled", modelName);
            Directory.CreateDirectory(compiledFolder);
            File.WriteAllText(Path.Combine(compiledFolder, $"{modelName}.xref"), string.Empty);

            var profile = new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Source,
                        ModelRootFolder = Path.Combine(_baseDir, "Source", modelName),
                        MetadataFolder = Path.Combine(_baseDir, "Source", modelName, "Metadata", modelName),
                        ProjectFilePath = Path.Combine(_baseDir, "Source", modelName, "Project", modelName, $"{modelName}.rnrproj")
                    }
                ]
            };
            SaveProfile(profile);

            var service = CreateModelDeploymentService();
            var messages = new List<string>();
            using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));

            service.AddModelToProfileIfNotExists(profileName, modelName, compiledFolder, ModelType.Compiled);

            var savedProfile = LoadProfile(profileName);
            Assert.That(savedProfile.AllModels, Has.Count.EqualTo(1));
            Assert.That(savedProfile.AllModels.Single().ModelType, Is.EqualTo(ModelType.Source));
            Assert.That(messages, Has.Some.EqualTo($"❌ Model '{modelName}' already exists in profile '{profileName}'. Skipping add"));
        }

        private ModelDeploymentService CreateModelDeploymentService()
            => CreateModelDeploymentService(Path.Combine(_baseDir, "Deployment"));

        private void SaveProfile(ProfileModel profile)
        {
            var config = CreateAppConfig(Path.Combine(_baseDir, "Deployment"));
            var fileService = new FileService(config);
            fileService.SaveProfile(profile, skipExistCheck: true);
        }

        private ProfileModel LoadProfile(string profileName)
        {
            var config = CreateAppConfig(Path.Combine(_baseDir, "Deployment"));
            var fileService = new FileService(config);
            return fileService.LoadProfile(profileName);
        }

        private ModelDeploymentService CreateModelDeploymentService(string deploymentBasePath)
        {
            var config = CreateAppConfig(deploymentBasePath);

            var fileService = new FileService(config);
            var deployablePackageService = new DeployablePackageService(config);
            return new ModelDeploymentService(config, fileService, deployablePackageService);
        }

        private AppConfig CreateAppConfig(string deploymentBasePath)
        {
            return new AppConfig
            {
                ProfileStoragePath = Path.Combine(_baseDir, "Profiles"),
                DefaultSourceDirectory = Path.Combine(_baseDir, "Source"),
                DeploymentBasePath = deploymentBasePath,
                DeployablePackages = Path.Combine(_baseDir, "DeployablePackages"),
                TaskUrl = string.Empty,
                ModelIdBegin = 1,
                ModelIdEnd = 999
            };
        }
    }
}
