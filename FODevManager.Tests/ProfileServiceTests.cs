using FODevManager.Services;
using FODevManager.Models;
using FODevManager.Utils;
using System;
using System.IO;
using System.Text.Json;

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

        [Test]
        public void ImportProfile_Should_Preserve_Local_NonDefault_DatabaseName()
        {
            var service = CreateProfileService();

            service.CreateProfile("SyncProfile");
            service.SetDatabaseName("SyncProfile", "LocalDb");

            var importPath = CreateExportProfile(
                "SyncProfile",
                "RepoDb",
                Path.Combine(_baseDir, "Import", "SyncProfile.json"));

            var importedProfile = service.ImportProfile(importPath);
            var savedProfile = service.LoadProfile("SyncProfile");

            Assert.That(importedProfile.DatabaseName, Is.EqualTo("LocalDb"));
            Assert.That(savedProfile.DatabaseName, Is.EqualTo("LocalDb"));
        }

        [Test]
        public void ImportProfile_Should_Use_Imported_DatabaseName_When_Local_Profile_Is_Default()
        {
            var service = CreateProfileService();

            service.CreateProfile("SyncProfile");

            var importPath = CreateExportProfile(
                "SyncProfile",
                "RepoDb",
                Path.Combine(_baseDir, "Import", "SyncProfile.json"));

            var importedProfile = service.ImportProfile(importPath);
            var savedProfile = service.LoadProfile("SyncProfile");

            Assert.That(importedProfile.DatabaseName, Is.EqualTo("RepoDb"));
            Assert.That(savedProfile.DatabaseName, Is.EqualTo("RepoDb"));
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

        private string CreateExportProfile(string profileName, string databaseName, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var exportProfile = new ProfileModel
            {
                ProfileName = profileName,
                DatabaseName = databaseName,
                SolutionFilePath = $"{profileName}.sln"
            };

            var json = JsonSerializer.Serialize(exportProfile);
            File.WriteAllText(path, json);
            return path;
        }
    }
}
