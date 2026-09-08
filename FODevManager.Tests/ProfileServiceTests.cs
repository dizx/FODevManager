using FODevManager.Services;
using FODevManager.Models;
using FODevManager.Shared.Models;
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

        [Test]
        public void SwitchProfile_Should_Replace_Different_Source_Deployment_And_Update_Ledger()
        {
            var service = CreateProfileService(out var linkService);
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var firstSourcePath = Path.Combine(_baseDir, "SourceA", "Metadata", "CarModel");
            var secondSourcePath = Path.Combine(_baseDir, "SourceB", "Metadata", "CarModel");
            Directory.CreateDirectory(firstSourcePath);
            Directory.CreateDirectory(secondSourcePath);

            var oldProfile = new ProfileModel
            {
                ProfileName = "OldProfile",
                IsActive = true
            };

            var newProfile = new ProfileModel
            {
                ProfileName = "NewProfile",
                IsActive = false,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = "CarModel",
                        ModelType = ModelType.Source,
                        MetadataFolder = secondSourcePath
                    }
                ]
            };

            var config = CreateAppConfig();
            var fileService = new FileService(config);
            fileService.SaveProfile(oldProfile, skipExistCheck: true);
            fileService.SaveProfile(newProfile, skipExistCheck: true);
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, "CarModel"), firstSourcePath);

            var ledger = new DeploymentLedgerService(config, fileService);
            ledger.RecordDeployment(new DeployedModelRecord
            {
                ModelName = "CarModel",
                ProfileName = "ThirdProfile",
                ModelType = ModelType.Source,
                SourcePath = firstSourcePath
            });

            var switched = service.SwitchProfile("NewProfile");

            Assert.That(switched, Is.True);
            Assert.That(linkService.ResolveLinkTarget(Path.Combine(deploymentBasePath, "CarModel")), Is.EqualTo(secondSourcePath));
            Assert.That(ledger.LoadRecords().Single().ProfileName, Is.EqualTo("NewProfile"));
            Assert.That(ledger.LoadRecords().Single().SourcePath, Is.EqualTo(DeploymentLedgerService.NormalizePath(secondSourcePath)));
        }

        [Test]
        public void SwitchProfile_Should_Remove_Managed_Deployments_From_NonActive_Profiles()
        {
            var service = CreateProfileService(out var linkService);
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var activeSourcePath = Path.Combine(_baseDir, "SourceA", "Metadata", "ActiveModel");
            var otherSourcePath = Path.Combine(_baseDir, "SourceB", "Metadata", "OtherModel");
            var newSourcePath = Path.Combine(_baseDir, "SourceC", "Metadata", "NewModel");
            Directory.CreateDirectory(activeSourcePath);
            Directory.CreateDirectory(otherSourcePath);
            Directory.CreateDirectory(newSourcePath);

            var activeProfile = new ProfileModel
            {
                ProfileName = "ActiveProfile",
                IsActive = true,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = "ActiveModel",
                        ModelType = ModelType.Source,
                        MetadataFolder = activeSourcePath,
                        IsDeployed = true
                    }
                ]
            };

            var otherProfile = new ProfileModel
            {
                ProfileName = "OtherProfile",
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = "OtherModel",
                        ModelType = ModelType.Source,
                        MetadataFolder = otherSourcePath,
                        IsDeployed = true
                    }
                ]
            };

            var newProfile = new ProfileModel
            {
                ProfileName = "NewProfile",
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = "NewModel",
                        ModelType = ModelType.Source,
                        MetadataFolder = newSourcePath
                    }
                ]
            };

            var config = CreateAppConfig();
            var fileService = new FileService(config);
            fileService.SaveProfile(activeProfile, skipExistCheck: true);
            fileService.SaveProfile(otherProfile, skipExistCheck: true);
            fileService.SaveProfile(newProfile, skipExistCheck: true);
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, "ActiveModel"), activeSourcePath);
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, "OtherModel"), otherSourcePath);

            var ledger = new DeploymentLedgerService(config, fileService);
            ledger.RecordDeployment(new DeployedModelRecord
            {
                ModelName = "ActiveModel",
                ProfileName = "ActiveProfile",
                ModelType = ModelType.Source,
                SourcePath = activeSourcePath
            });
            ledger.RecordDeployment(new DeployedModelRecord
            {
                ModelName = "OtherModel",
                ProfileName = "OtherProfile",
                ModelType = ModelType.Source,
                SourcePath = otherSourcePath
            });

            var switched = service.SwitchProfile("NewProfile");

            Assert.That(switched, Is.True);
            Assert.That(linkService.ResolveLinkTarget(Path.Combine(deploymentBasePath, "ActiveModel")), Is.Null);
            Assert.That(linkService.ResolveLinkTarget(Path.Combine(deploymentBasePath, "OtherModel")), Is.Null);
            Assert.That(linkService.ResolveLinkTarget(Path.Combine(deploymentBasePath, "NewModel")), Is.EqualTo(newSourcePath));
            Assert.That(ledger.LoadRecords().Select(record => record.ModelName), Is.EquivalentTo(new[] { "NewModel" }));
        }

        [Test]
        public void UpdateDeploymentStatus_Should_Use_Deployment_Ledger_Not_Filesystem_Link()
        {
            var service = CreateProfileService();
            var profileName = "ProfileA";
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var sourcePath = Path.Combine(_baseDir, "Source", "Metadata", modelName);
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(Path.Combine(deploymentBasePath, modelName));

            var profile = new ProfileModel
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
            };

            var fileService = new FileService(CreateAppConfig());
            fileService.SaveProfile(profile, skipExistCheck: true);

            service.UpdateDeploymentStatus(profileName);

            var savedProfile = fileService.LoadProfile(profileName);
            Assert.That(savedProfile.AllModels.Single().IsDeployed, Is.False);
        }

        [Test]
        public void UpdateDeploymentStatus_Should_SelfHeal_Existing_Known_Link_Before_Updating_Profile()
        {
            var service = CreateProfileService(out var linkService);
            var profileName = "ProfileA";
            var modelName = "CarModel";
            var deploymentBasePath = Path.Combine(_baseDir, "Deployment");
            var sourcePath = Path.Combine(_baseDir, "Source", "Metadata", modelName);
            Directory.CreateDirectory(sourcePath);
            linkService.CreateSymbolicLink(Path.Combine(deploymentBasePath, modelName), sourcePath);

            var profile = new ProfileModel
            {
                ProfileName = profileName,
                StandaloneModels =
                [
                    new ProfileEnvironmentModel
                    {
                        ModelName = modelName,
                        ModelType = ModelType.Source,
                        MetadataFolder = sourcePath,
                        IsDeployed = false
                    }
                ]
            };

            var fileService = new FileService(CreateAppConfig());
            fileService.SaveProfile(profile, skipExistCheck: true);

            service.UpdateDeploymentStatus(profileName);

            var savedProfile = fileService.LoadProfile(profileName);
            Assert.That(savedProfile.AllModels.Single().IsDeployed, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BuildDeployableNugetPackage_Should_Validate_Compiled_Payload_Without_Creating_A_Solution(bool repositoryBacked)
        {
            var service = CreateProfileService(out var linkService);
            var model = new ProfileEnvironmentModel
            {
                ModelName = "CompiledModel",
                ModelType = ModelType.Compiled,
                ModelRootFolder = _baseDir,
                CompiledModelFolder = Path.Combine(_baseDir, "missing-compiled-root"),
                ProjectFilePath = string.Empty,
                MetadataFolder = string.Empty
            };
            var profile = new ProfileModel { ProfileName = "CompiledProfile" };
            if (repositoryBacked)
                profile.Repositories = [new RepositoryModel { RepoRootFolder = _baseDir, Models = [model] }];
            else
                profile.StandaloneModels = [model];
            var fileService = new FileService(CreateAppConfig());
            fileService.SaveProfile(profile, skipExistCheck: true);
            var originalProfile = JsonSerializer.Serialize(fileService.LoadProfile(profile.ProfileName));
            var linkPath = Path.Combine(_baseDir, "Deployment", model.ModelName);
            linkService.CreateSymbolicLink(linkPath, model.CompiledModelFolder);
            var messages = new List<string>();
            using var subscription = FODevManager.Messages.MessageBus.Subscribe(message => messages.Add(message.Content));

            Assert.That(service.BuildDeployableNugetPackage(profile.ProfileName, model.ModelName), Is.False);

            Assert.That(messages.Any(message => message.Contains("Compiled model folder is missing or invalid")), Is.True);
            Assert.That(Directory.GetFiles(_baseDir, "*.sln", SearchOption.AllDirectories), Is.Empty);
            Assert.That(JsonSerializer.Serialize(fileService.LoadProfile(profile.ProfileName)), Is.EqualTo(originalProfile));
            Assert.That(linkService.ResolveLinkTarget(linkPath), Is.EqualTo(model.CompiledModelFolder));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BuildDeployableNugetPackage_Should_Convert_References_Before_Creating_Build_Solution(bool ambiguous)
        {
            var config = CreateAppConfig();
            config.DeployablePackages = string.Empty;
            var service = CreateProfileService(out _, config);
            var projectDirectory = Path.Combine(_baseDir, "Project", "PTSSSH");
            var producerDirectory = Path.Combine(_baseDir, "Libs", "Peritus.Ssh");
            Directory.CreateDirectory(projectDirectory);
            Directory.CreateDirectory(producerDirectory);
            var producerPath = Path.Combine(producerDirectory, "Peritus.Ssh.csproj");
            File.WriteAllText(producerPath, "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net48</TargetFramework></PropertyGroup></Project>");
            if (ambiguous)
                File.WriteAllText(Path.Combine(producerDirectory, "Other.csproj"), "<Project><AssemblyName>Peritus.Ssh</AssemblyName></Project>");
            var model = new ProfileEnvironmentModel
            {
                ModelName = "PTSSSH", ModelType = ModelType.Source,
                ProjectFilePath = Path.Combine(projectDirectory, "PTSSSH.rnrproj")
            };
            File.WriteAllText(model.ProjectFilePath, "<Project><ItemGroup><Reference Include='Peritus.Ssh'><HintPath>../../Libs/Peritus.Ssh/bin/$(Configuration)/net48/Peritus.Ssh.dll</HintPath><Private>True</Private></Reference></ItemGroup></Project>");
            var originalProject = File.ReadAllBytes(model.ProjectFilePath);
            var profile = new ProfileModel
            {
                ProfileName = "SourceProfile", SolutionFilePath = Path.Combine(_baseDir, "Source.sln"),
                Repositories = [new RepositoryModel { RepoRootFolder = _baseDir, Models = [model] }]
            };
            File.WriteAllText(profile.SolutionFilePath, "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
                "Project(\"{FC65038C-1B2F-41E1-A629-BED71D161FFF}\") = \"PTSSSH\", \"Project\\PTSSSH\\PTSSSH.rnrproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\nEndProject\r\n");
            new FileService(config).SaveProfile(profile, skipExistCheck: true);

            Assert.That(service.BuildDeployableNugetPackage(profile.ProfileName, model.ModelName), Is.False);
            var solutionPath = Path.Combine(projectDirectory, "PTSSSH.packagebuild.sln");
            Assert.That(File.Exists(solutionPath), Is.EqualTo(!ambiguous));
            if (ambiguous)
                Assert.That(File.ReadAllBytes(model.ProjectFilePath), Is.EqualTo(originalProject));
            else
            {
                var reference = System.Xml.Linq.XDocument.Load(model.ProjectFilePath).Descendants("ProjectReference").Single();
                var guid = reference.Element("Project")!.Value;
                var solution = File.ReadAllText(solutionPath);
                Assert.That(solution, Does.Contain($"\"Peritus.Ssh\", \"..\\..\\Libs\\Peritus.Ssh\\Peritus.Ssh.csproj\", \"{guid}\""));
                Assert.That(solution, Does.Contain($"{guid}.Debug|Any CPU.Build.0"));
            }
            Assert.That(Directory.Exists(Path.Combine(producerDirectory, "bin")), Is.False);
        }

        private ProfileService CreateProfileService()
            => CreateProfileService(out _);

        private ProfileService CreateProfileService(out FakeDirectoryLinkService linkService, AppConfig? configOverride = null)
        {
            var config = configOverride ?? CreateAppConfig();

            var fileService = new FileService(config);
            var solutionService = new VisualStudioSolutionService(config);
            var deployablePackageService = new DeployablePackageService(config);
            var modelVersionService = new ModelVersionService();
            linkService = new FakeDirectoryLinkService();
            var deploymentLedgerService = new DeploymentLedgerService(config, fileService, linkService);
            var modelDeploymentService = new ModelDeploymentService(config, fileService, deployablePackageService, deploymentLedgerService, modelVersionService, linkService);
            var profilesContainer = new ProfilesContainer(fileService);

            return new ProfileService(
                config,
                fileService,
                solutionService,
                modelDeploymentService,
                deployablePackageService,
                modelVersionService,
                profilesContainer,
                deploymentLedgerService);
        }

        private AppConfig CreateAppConfig()
        {
            return new AppConfig
            {
                ProfileStoragePath = Path.Combine(_baseDir, "Profiles"),
                DefaultSourceDirectory = Path.Combine(_baseDir, "Source"),
                DeploymentBasePath = Path.Combine(_baseDir, "Deployment"),
                DeployablePackages = Path.Combine(_baseDir, "DeployablePackages"),
                TaskUrl = string.Empty,
                ModelIdBegin = 1,
                ModelIdEnd = 999,
                CheckUncommittedBeforeSwitch = false
            };
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

        private sealed class FakeDirectoryLinkService : IDirectoryLinkService
        {
            private readonly Dictionary<string, string> _links = new(StringComparer.OrdinalIgnoreCase);

            public bool Exists(string path)
                => _links.ContainsKey(Normalize(path)) || Directory.Exists(path);

            public void CreateSymbolicLink(string linkPath, string targetPath)
            {
                Directory.CreateDirectory(linkPath);
                _links[Normalize(linkPath)] = Path.GetFullPath(targetPath);
            }

            public void Delete(string path, bool recursive = true)
                => _links.Remove(Normalize(path));

            public string? ResolveLinkTarget(string linkPath)
                => _links.TryGetValue(Normalize(linkPath), out var targetPath) ? targetPath : null;

            private static string Normalize(string path)
                => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
