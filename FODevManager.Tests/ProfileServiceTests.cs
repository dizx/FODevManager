using FODevManager.Services;
using FODevManager.Utils;
using System;
using System.IO;

namespace FODevManager.Tests
{
    [TestFixture]
    public class ProfileServiceTests
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
        public void CreateProfile_Should_Mark_New_Profile_As_Active()
        {
            var service = CreateProfileService();

            service.CreateProfile("OldProfile");
            service.CreateProfile("NewProfile");

            var oldProfile = service.LoadProfile("OldProfile");
            var newProfile = service.LoadProfile("NewProfile");

            Assert.That(oldProfile.IsActive, Is.False);
            Assert.That(newProfile.IsActive, Is.True);
            Assert.That(service.GetActiveProfileName(), Is.EqualTo("NewProfile"));
        }

        private ProfileService CreateProfileService()
        {
            var config = new AppConfig
            {
                ProfileStoragePath = Path.Combine(_baseDir, "Profiles"),
                DefaultSourceDirectory = Path.Combine(_baseDir, "Source"),
                DeploymentBasePath = Path.Combine(_baseDir, "Deployment"),
                DeployablePackages = string.Empty,
                TaskUrl = string.Empty,
                ModelIdBegin = 1,
                ModelIdEnd = 999
            };

            var fileService = new FileService(config);
            var solutionService = new VisualStudioSolutionService(config);
            var deployablePackageService = new DeployablePackageService(config);
            var modelDeploymentService = new ModelDeploymentService(config, fileService, deployablePackageService);
            var profilesContainer = new ProfilesContainer(fileService);

            return new ProfileService(
                config,
                fileService,
                solutionService,
                modelDeploymentService,
                deployablePackageService,
                profilesContainer);
        }
    }
}
