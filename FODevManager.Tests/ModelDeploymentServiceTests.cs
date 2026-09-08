using FODevManager.Models;
using FODevManager.Messages;
using FODevManager.Services;
using FODevManager.Shared.Models;
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

        [Test]
        public void AddModelToProfileIfNotExists_Should_Find_Dotted_Git_Repository_Root()
        {
            var profileName = "DottedRepositoryProfile";
            var modelName = "BankConnect";
            var repositoryRoot = Path.Combine(_baseDir, "Peritus.BankConnect");
            var metadataFolder = Path.Combine(repositoryRoot, "Metadata", modelName);
            var projectFolder = Path.Combine(repositoryRoot, "Project", modelName);
            var projectFile = Path.Combine(projectFolder, $"{modelName}.rnrproj");
            var gitFolder = Path.Combine(repositoryRoot, ".git");

            Directory.CreateDirectory(metadataFolder);
            Directory.CreateDirectory(projectFolder);
            Directory.CreateDirectory(gitFolder);
            File.WriteAllText(projectFile, string.Empty);
            File.WriteAllText(
                Path.Combine(gitFolder, "config"),
                "[remote \"origin\"]\n    url = https://example.com/Peritus.BankConnect.git");
            SaveProfile(new ProfileModel { ProfileName = profileName });

            var service = CreateModelDeploymentService();

            service.AddModelToProfileIfNotExists(profileName, modelName, repositoryRoot, ModelType.Source);

            var savedProfile = LoadProfile(profileName);
            Assert.That(savedProfile.Repositories, Has.Count.EqualTo(1));
            Assert.That(savedProfile.Repositories.Single().RepoRootFolder, Is.EqualTo(repositoryRoot));
            Assert.That(savedProfile.Repositories.Single().Models.Single().ModelName, Is.EqualTo(modelName));
            Assert.That(savedProfile.StandaloneModels, Is.Empty);
        }

        [Test]
        public void DeploymentLedger_Should_Write_To_Profile_Storage_File()
        {
            var config = CreateAppConfig(Path.Combine(_baseDir, "Deployment"));
            var service = new DeploymentLedgerService(config, new FileService(config));

            service.RecordDeployment(new DeployedModelRecord
            {
                ModelName = "CarModel",
                ProfileName = "ProfileA",
                ModelType = ModelType.Source,
                SourcePath = Path.Combine(_baseDir, "Source", "Metadata", "CarModel"),
                Version = "1.0.0",
                DeployedAtUtc = DateTime.UtcNow
            });

            var ledgerPath = Path.Combine(config.ProfileStoragePath, "deployed-models.json");
            Assert.That(File.Exists(ledgerPath), Is.True);

            var json = File.ReadAllText(ledgerPath);
            Assert.That(json, Does.Contain("CarModel"));
            Assert.That(json, Does.Contain("ProfileA"));
        }

        [Test]
        public void FileService_Should_Not_Treat_Deployment_Ledger_As_Profile()
        {
            var config = CreateAppConfig(Path.Combine(_baseDir, "Deployment"));
            var fileService = new FileService(config);

            fileService.SaveProfile(new ProfileModel { ProfileName = "ProfileA" }, skipExistCheck: true);
            File.WriteAllText(Path.Combine(config.ProfileStoragePath, "deployed-models.json"), "[]");

            var profileNames = fileService.GetAllProfileNames();

            Assert.That(profileNames, Is.EqualTo(new[] { "ProfileA" }));
        }

        [Test]
        public void DeploymentLedger_Should_Allow_Same_Model_From_Same_Source_Path()
        {
            var config = CreateAppConfig(Path.Combine(_baseDir, "Deployment"));
            var service = new DeploymentLedgerService(config, new FileService(config));
            var sourcePath = Path.Combine(_baseDir, "Source", "Metadata", "CarModel");

            service.RecordDeployment(new DeployedModelRecord
            {
                ModelName = "CarModel",
                ProfileName = "ProfileA",
                ModelType = ModelType.Source,
                SourcePath = sourcePath
            });

            var allowed = service.CanDeploy("ProfileB", "CarModel", sourcePath, out var blocker);

            Assert.That(allowed, Is.True);
            Assert.That(blocker, Is.Null);
        }

        [Test]
        public void DeploymentLedger_Should_Block_Same_Model_From_Different_Source_Path()
        {
            var config = CreateAppConfig(Path.Combine(_baseDir, "Deployment"));
            var service = new DeploymentLedgerService(config, new FileService(config));
            var firstSourcePath = Path.Combine(_baseDir, "SourceA", "Metadata", "CarModel");
            var secondSourcePath = Path.Combine(_baseDir, "SourceB", "Metadata", "CarModel");

            service.RecordDeployment(new DeployedModelRecord
            {
                ModelName = "CarModel",
                ProfileName = "ProfileA",
                ModelType = ModelType.Source,
                SourcePath = firstSourcePath
            });

            var allowed = service.CanDeploy("ProfileB", "CarModel", secondSourcePath, out var blocker);

            Assert.That(allowed, Is.False);
            Assert.That(blocker, Is.Not.Null);
            Assert.That(blocker!.ProfileName, Is.EqualTo("ProfileA"));
            Assert.That(blocker.SourcePath, Is.EqualTo(DeploymentLedgerService.NormalizePath(firstSourcePath)));
        }

        [Test]
        public void DeployModel_Should_Record_Successful_Deployment_In_Ledger()
        {
            var profileName = "DeployProfile";
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var sourcePath = Path.Combine(_baseDir, "Source", "Metadata", modelName);
            Directory.CreateDirectory(sourcePath);

            SaveProfile(new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Source,
                        MetadataFolder = sourcePath
                    }
                ]
            });

            var service = CreateModelDeploymentService(deploymentBasePath);

            service.DeployModel(profileName, modelName);

            var config = CreateAppConfig(deploymentBasePath);
            var ledger = new DeploymentLedgerService(config, new FileService(config));
            var record = ledger.LoadRecords().Single(record => record.ModelName == modelName);

            Assert.That(record.ProfileName, Is.EqualTo(profileName));
            Assert.That(record.SourcePath, Is.EqualTo(DeploymentLedgerService.NormalizePath(sourcePath)));
            Assert.That(record.ModelType, Is.EqualTo(ModelType.Source));
        }

        [Test]
        public void DeployModel_Should_Return_False_When_Link_Creation_Fails()
        {
            var profileName = "DeployProfile";
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var sourcePath = Path.Combine(_baseDir, "Source", "Metadata", modelName);
            Directory.CreateDirectory(sourcePath);

            SaveProfile(new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Source,
                        MetadataFolder = sourcePath
                    }
                ]
            });

            var service = CreateModelDeploymentService(deploymentBasePath, out var linkService);
            linkService.CreateException = new UnauthorizedAccessException("A required privilege is not held by the client");

            var deployed = service.DeployModel(profileName, modelName);

            Assert.That(deployed, Is.False);
        }

        [Test]
        public void DeployAllUndeployedModels_Should_Return_False_When_Link_Creation_Fails()
        {
            var profileName = "DeployProfile";
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var sourcePath = Path.Combine(_baseDir, "Source", "Metadata", modelName);
            Directory.CreateDirectory(sourcePath);

            SaveProfile(new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Source,
                        MetadataFolder = sourcePath
                    }
                ]
            });

            var service = CreateModelDeploymentService(deploymentBasePath, out var linkService);
            linkService.CreateException = new UnauthorizedAccessException("A required privilege is not held by the client");

            var deployed = service.DeployAllUndeployedModels(profileName);

            Assert.That(deployed, Is.False);
            Assert.That(LoadProfile(profileName).AllModels.Single().IsDeployed, Is.False);
        }

        [Test]
        public void DeployOutdatedDeployedModels_Should_Replace_Old_CompiledNuget_Source_Path()
        {
            var profileName = "Motus";
            var modelName = "BluestarMotus";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var oldPackagePath = Path.Combine(_baseDir, "DeployablePackages", "BluestarMotus-7.6.18");
            var newPackagePath = Path.Combine(_baseDir, "DeployablePackages", "BluestarMotus-7.7.18");
            Directory.CreateDirectory(oldPackagePath);
            Directory.CreateDirectory(newPackagePath);

            SaveProfile(new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.CompiledNuget,
                        CompiledModelFolder = newPackagePath,
                        PackageId = modelName,
                        PackageVersion = "7.7.18",
                        IsDeployed = true
                    }
                ]
            });

            var service = CreateModelDeploymentService(deploymentBasePath, out var linkService);
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, modelName), oldPackagePath);

            var config = CreateAppConfig(deploymentBasePath);
            var ledger = new DeploymentLedgerService(config, new FileService(config));
            ledger.RecordDeployment(new DeployedModelRecord
            {
                ModelName = modelName,
                ProfileName = "Motus-release",
                ModelType = ModelType.CompiledNuget,
                SourcePath = oldPackagePath,
                PackageId = modelName,
                PackageVersion = "7.6.18"
            });

            var deployed = service.RedeployModelsWithChangedSource(profileName);

            Assert.That(deployed, Is.True);
            Assert.That(linkService.ResolveLinkTarget(Path.Combine(deploymentBasePath, modelName)), Is.EqualTo(newPackagePath));
            var record = ledger.LoadRecords().Single(record => record.ModelName == modelName);
            Assert.That(record.SourcePath, Is.EqualTo(DeploymentLedgerService.NormalizePath(newPackagePath)));
            Assert.That(record.PackageVersion, Is.EqualTo("7.7.18"));
        }

        [Test]
        public void DeployModel_Should_Block_When_Ledger_Has_Different_Source_Path()
        {
            var profileName = "CurrentProfile";
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var firstSourcePath = Path.Combine(_baseDir, "SourceA", "Metadata", modelName);
            var secondSourcePath = Path.Combine(_baseDir, "SourceB", "Metadata", modelName);
            Directory.CreateDirectory(firstSourcePath);
            Directory.CreateDirectory(secondSourcePath);

            var service = CreateModelDeploymentService(deploymentBasePath, out var linkService);
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, modelName), firstSourcePath);

            var config = CreateAppConfig(deploymentBasePath);
            var fileService = new FileService(config);
            var ledger = new DeploymentLedgerService(config, fileService);
            ledger.RecordDeployment(new DeployedModelRecord
            {
                ModelName = modelName,
                ProfileName = "OtherProfile",
                ModelType = ModelType.Source,
                SourcePath = firstSourcePath
            });

            SaveProfile(new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Source,
                        MetadataFolder = secondSourcePath
                    }
                ]
            });

            var messages = new List<string>();
            using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));

            service.DeployModel(profileName, modelName);

            Assert.That(linkService.ResolveLinkTarget(Path.Combine(deploymentBasePath, modelName)), Is.EqualTo(firstSourcePath));
            Assert.That(messages, Has.Some.Contains("is already deployed from profile 'OtherProfile'"));
        }

        [Test]
        public void DeployAllUndeployedModels_Should_Block_When_Ledger_Has_Different_Source_Path()
        {
            var profileName = "CurrentProfile";
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var firstSourcePath = Path.Combine(_baseDir, "SourceA", "Metadata", modelName);
            var secondSourcePath = Path.Combine(_baseDir, "SourceB", "Metadata", modelName);
            Directory.CreateDirectory(firstSourcePath);
            Directory.CreateDirectory(secondSourcePath);

            var service = CreateModelDeploymentService(deploymentBasePath, out var linkService);
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, modelName), firstSourcePath);

            var config = CreateAppConfig(deploymentBasePath);
            var fileService = new FileService(config);
            var ledger = new DeploymentLedgerService(config, fileService);
            ledger.RecordDeployment(new DeployedModelRecord
            {
                ModelName = modelName,
                ProfileName = "OtherProfile",
                ModelType = ModelType.Source,
                SourcePath = firstSourcePath
            });

            SaveProfile(new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Source,
                        MetadataFolder = secondSourcePath
                    }
                ]
            });

            service.DeployAllUndeployedModels(profileName);

            Assert.That(linkService.ResolveLinkTarget(Path.Combine(deploymentBasePath, modelName)), Is.EqualTo(firstSourcePath));
        }

        [Test]
        public void UnDeployModel_Should_Remove_Ledger_Record()
        {
            var profileName = "DeployProfile";
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var sourcePath = Path.Combine(_baseDir, "Source", "Metadata", modelName);
            Directory.CreateDirectory(sourcePath);

            var service = CreateModelDeploymentService(deploymentBasePath, out var linkService);
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, modelName), sourcePath);

            SaveProfile(new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Source,
                        MetadataFolder = sourcePath,
                        IsDeployed = true
                    }
                ]
            });

            var config = CreateAppConfig(deploymentBasePath);
            var ledger = new DeploymentLedgerService(config, new FileService(config));
            ledger.RecordDeployment(new DeployedModelRecord
            {
                ModelName = modelName,
                ProfileName = profileName,
                ModelType = ModelType.Source,
                SourcePath = sourcePath
            });

            service.UnDeployModel(profileName, modelName);

            Assert.That(ledger.LoadRecords().Any(record => record.ModelName == modelName), Is.False);
        }

        [Test]
        public void DeploymentLedger_Should_SelfHeal_Existing_Link_To_Known_Profile_Model()
        {
            var profileName = "ProfileA";
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var sourcePath = Path.Combine(_baseDir, "Source", "Metadata", modelName);
            Directory.CreateDirectory(sourcePath);

            var linkService = new FakeDirectoryLinkService();
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, modelName), sourcePath);

            SaveProfile(new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Source,
                        MetadataFolder = sourcePath
                    }
                ]
            });

            var config = CreateAppConfig(deploymentBasePath);
            var ledger = new DeploymentLedgerService(config, new FileService(config), linkService);

            ledger.SelfHeal();

            var record = ledger.LoadRecords().Single();
            Assert.That(record.ModelName, Is.EqualTo(modelName));
            Assert.That(record.ProfileName, Is.EqualTo(profileName));
            Assert.That(record.IsUnmanaged, Is.False);
            Assert.That(record.SourcePath, Is.EqualTo(DeploymentLedgerService.NormalizePath(sourcePath)));
        }

        [Test]
        public void DeploymentLedger_Should_Ignore_Unmatched_Aos_Deployment_Folders()
        {
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var sourcePath = Path.Combine(_baseDir, "External", modelName);
            Directory.CreateDirectory(sourcePath);

            var linkService = new FakeDirectoryLinkService();
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, modelName), sourcePath);

            var config = CreateAppConfig(deploymentBasePath);
            var ledger = new DeploymentLedgerService(config, new FileService(config), linkService);

            ledger.SelfHeal();

            Assert.That(ledger.LoadRecords(), Is.Empty);
        }

        [Test]
        public void DeploymentLedger_Should_Remove_Stale_Unmatched_Unmanaged_Records()
        {
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var sourcePath = Path.Combine(_baseDir, "External", modelName);
            Directory.CreateDirectory(sourcePath);

            var linkService = new FakeDirectoryLinkService();
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, modelName), sourcePath);

            var config = CreateAppConfig(deploymentBasePath);
            var ledger = new DeploymentLedgerService(config, new FileService(config), linkService);
            ledger.RecordDeployment(new DeployedModelRecord
            {
                ModelName = modelName,
                SourcePath = sourcePath,
                IsUnmanaged = true
            });

            ledger.SelfHeal();

            Assert.That(ledger.LoadRecords(), Is.Empty);
        }

        [Test]
        public void DeployModel_Should_Remove_New_Link_When_Ledger_Save_Fails()
        {
            var profileName = "DeployProfile";
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var sourcePath = Path.Combine(_baseDir, "Source", "Metadata", modelName);
            Directory.CreateDirectory(sourcePath);

            SaveProfile(new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Source,
                        MetadataFolder = sourcePath
                    }
                ]
            });

            var service = CreateModelDeploymentService(deploymentBasePath, out var linkService);
            var config = CreateAppConfig(deploymentBasePath);
            var ledgerPath = Path.Combine(config.ProfileStoragePath, "deployed-models.json");
            service = CreateModelDeploymentService(deploymentBasePath, out linkService, new ThrowingDeploymentLedgerService());

            service.DeployModel(profileName, modelName);

            Assert.That(linkService.ResolveLinkTarget(Path.Combine(deploymentBasePath, modelName)), Is.Null);
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
            return CreateModelDeploymentService(deploymentBasePath, out _);
        }

        private ModelDeploymentService CreateModelDeploymentService(string deploymentBasePath, out FakeDirectoryLinkService linkService)
            => CreateModelDeploymentService(deploymentBasePath, out linkService, null);

        private ModelDeploymentService CreateModelDeploymentService(string deploymentBasePath, out FakeDirectoryLinkService linkService, IDeploymentLedgerService? deploymentLedgerService)
        {
            var config = CreateAppConfig(deploymentBasePath);

            var fileService = new FileService(config);
            var deployablePackageService = new DeployablePackageService(config);
            var modelVersionService = new ModelVersionService();
            linkService = new FakeDirectoryLinkService();
            deploymentLedgerService ??= new DeploymentLedgerService(config, fileService, linkService);
            return new ModelDeploymentService(config, fileService, deployablePackageService, deploymentLedgerService, modelVersionService, linkService);
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

        private sealed class FakeDirectoryLinkService : IDirectoryLinkService
        {
            private readonly Dictionary<string, string> _links = new(StringComparer.OrdinalIgnoreCase);

            public Action? AfterCreate { get; set; }

            public Exception? CreateException { get; set; }

            public bool Exists(string path)
                => _links.ContainsKey(Normalize(path)) || Directory.Exists(path);

            public void CreateSymbolicLink(string linkPath, string targetPath)
            {
                if (CreateException != null)
                    throw CreateException;

                Directory.CreateDirectory(linkPath);
                _links[Normalize(linkPath)] = Path.GetFullPath(targetPath);
                AfterCreate?.Invoke();
            }

            public void Delete(string path, bool recursive = true)
            {
                _links.Remove(Normalize(path));

                if (Directory.Exists(path))
                    Directory.Delete(path, recursive);
            }

            public string? ResolveLinkTarget(string linkPath)
                => _links.TryGetValue(Normalize(linkPath), out var targetPath) ? targetPath : null;

            private static string Normalize(string path)
                => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private sealed class ThrowingDeploymentLedgerService : IDeploymentLedgerService
        {
            public IReadOnlyList<DeployedModelRecord> LoadRecords()
                => [];

            public bool CanDeploy(string profileName, string modelName, string sourcePath, out DeployedModelRecord? blocker)
            {
                blocker = null;
                return true;
            }

            public bool IsModelDeployed(ProfileEnvironmentModel model)
                => false;

            public void RecordDeployment(DeployedModelRecord record)
                => throw new IOException("Ledger write failed");

            public void RemoveDeployment(string modelName)
            {
            }

            public void Clear()
            {
            }

            public void SelfHeal()
            {
            }
        }
    }
}
