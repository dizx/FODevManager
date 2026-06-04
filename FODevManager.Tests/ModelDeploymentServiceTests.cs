using FODevManager.Models;
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

        private ModelDeploymentService CreateModelDeploymentService(string deploymentBasePath)
        {
            var config = new AppConfig
            {
                ProfileStoragePath = Path.Combine(_baseDir, "Profiles"),
                DefaultSourceDirectory = Path.Combine(_baseDir, "Source"),
                DeploymentBasePath = deploymentBasePath,
                DeployablePackages = Path.Combine(_baseDir, "DeployablePackages"),
                TaskUrl = string.Empty,
                ModelIdBegin = 1,
                ModelIdEnd = 999
            };

            var fileService = new FileService(config);
            var deployablePackageService = new DeployablePackageService(config);
            return new ModelDeploymentService(config, fileService, deployablePackageService);
        }
    }
}
