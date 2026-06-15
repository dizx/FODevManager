using FODevManager.Services;
using FODevManager.Models;
using FODevManager.Utils;
using System.Reflection;
using System.Xml.Linq;

namespace FODevManager.Tests
{
    [TestFixture]
    public class DeployablePackageServiceTests
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
        public void ResolveCompiledModelName_Should_Use_Xref_Name_When_Package_Root_Is_Versioned()
        {
            var packageRoot = Path.Combine(_baseDir, "BluestarMotus-7.6.18");
            Directory.CreateDirectory(packageRoot);
            File.WriteAllText(Path.Combine(packageRoot, "BluestarMotus.xref"), "");

            var modelName = ResolveCompiledModelName(packageRoot, "BluestarMotus", "7.6.18");

            Assert.That(modelName, Is.EqualTo("BluestarMotus"));
        }

        [Test]
        public void ResolveCompiledModelName_Should_Use_Compiled_Assembly_Name_When_Xref_Is_Missing()
        {
            var packageRoot = Path.Combine(_baseDir, "BluestarMotus-7.6.18");
            var binRoot = Path.Combine(packageRoot, "bin");
            Directory.CreateDirectory(binRoot);
            File.WriteAllText(Path.Combine(binRoot, "Dynamics.AX.BluestarMotus.dll"), "");

            var modelName = ResolveCompiledModelName(packageRoot, "BluestarMotus", "7.6.18");

            Assert.That(modelName, Is.EqualTo("BluestarMotus"));
        }

        [Test]
        public void ResolveReferencedCompiledNugetReferenceFolders_Should_Use_Descriptor_And_Isv_Config_Version()
        {
            var repositoryRoot = Path.Combine(_baseDir, "repo");
            var buildRoot = Path.Combine(repositoryRoot, "Build");
            var sourceMetadataRoot = Path.Combine(repositoryRoot, "Metadata", "PTSVikingAssistance");
            var descriptorRoot = Path.Combine(sourceMetadataRoot, "Descriptor");
            var carModelRoot = Path.Combine(_baseDir, "DeployablePackages", "CarModel-1.0.0");
            var carModelBinRoot = Path.Combine(carModelRoot, "bin");
            var wrongVersionRoot = Path.Combine(_baseDir, "DeployablePackages", "CarModel-2.0.0");
            var unlistedModelRoot = Path.Combine(_baseDir, "DeployablePackages", "UnlistedModel-1.0.0");

            Directory.CreateDirectory(buildRoot);
            Directory.CreateDirectory(descriptorRoot);
            Directory.CreateDirectory(carModelBinRoot);
            Directory.CreateDirectory(wrongVersionRoot);
            Directory.CreateDirectory(unlistedModelRoot);

            File.WriteAllText(Path.Combine(buildRoot, "isv.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="CarModel" version="1.0.0" />
                </packages>
                """);

            File.WriteAllText(Path.Combine(descriptorRoot, "PTSVikingAssistance.xml"), """
                <?xml version="1.0" encoding="utf-8"?>
                <AxModelInfo xmlns:i="http://www.w3.org/2001/XMLSchema-instance">
                  <ModuleReferences xmlns:d2p1="http://schemas.microsoft.com/2003/10/Serialization/Arrays">
                    <d2p1:string>ApplicationSuite</d2p1:string>
                    <d2p1:string>CarModel</d2p1:string>
                  </ModuleReferences>
                  <Name>PTSVikingAssistance</Name>
                </AxModelInfo>
                """);

            var sourceModel = new ProfileEnvironmentModel
            {
                ModelName = "PTSVikingAssistance",
                ModelRootFolder = repositoryRoot,
                MetadataFolder = sourceMetadataRoot,
                ModelType = ModelType.Source
            };

            var profile = new ProfileModel
            {
                ProfileName = "Viking",
                Repositories =
                [
                    new RepositoryModel
                    {
                        DisplayName = "Peritus Viking Assistance",
                        RepoRootFolder = repositoryRoot,
                        Models =
                        [
                            sourceModel,
                            new ProfileEnvironmentModel
                            {
                                ModelName = "CarModel-1.0.0",
                                ModelRootFolder = repositoryRoot,
                                CompiledModelFolder = carModelRoot,
                                PackageId = "CarModel",
                                PackageVersion = "1.0.0",
                                ModelType = ModelType.CompiledNuget
                            },
                            new ProfileEnvironmentModel
                            {
                                ModelName = "CarModel-2.0.0",
                                ModelRootFolder = repositoryRoot,
                                CompiledModelFolder = wrongVersionRoot,
                                PackageId = "CarModel",
                                PackageVersion = "2.0.0",
                                ModelType = ModelType.CompiledNuget
                            },
                            new ProfileEnvironmentModel
                            {
                                ModelName = "UnlistedModel-1.0.0",
                                ModelRootFolder = repositoryRoot,
                                CompiledModelFolder = unlistedModelRoot,
                                PackageId = "UnlistedModel",
                                PackageVersion = "1.0.0",
                                ModelType = ModelType.CompiledNuget
                            }
                        ]
                    }
                ]
            };

            var referenceFolders = ResolveReferencedCompiledNugetReferenceFolders(profile, sourceModel);

            Assert.That(referenceFolders, Is.EquivalentTo(new[] { carModelRoot, carModelBinRoot }));
        }

        [Test]
        public void PrepareCompiledNugetReferenceRoot_Should_Expose_Model_By_Unversioned_Module_Name()
        {
            var runRoot = Path.Combine(_baseDir, "run");
            var compiledModelFolder = Path.Combine(_baseDir, "DeployablePackages", "CarModel-1.0.0");
            var compiledBinFolder = Path.Combine(compiledModelFolder, "bin");
            Directory.CreateDirectory(compiledBinFolder);
            File.WriteAllText(Path.Combine(compiledBinFolder, "Dynamics.AX.CarModel.dll"), string.Empty);

            var compiledModel = new ProfileEnvironmentModel
            {
                ModelName = "CarModel-1.0.0",
                CompiledModelFolder = compiledModelFolder,
                PackageId = "CarModel",
                PackageVersion = "1.0.0",
                ModelType = ModelType.CompiledNuget
            };

            var referenceRoot = PrepareCompiledNugetReferenceRoot(runRoot, [compiledModel]);

            Assert.That(referenceRoot, Is.EqualTo(Path.Combine(runRoot, "CompiledNugetReferences")));
            Assert.That(Directory.Exists(Path.Combine(referenceRoot, "CarModel", "bin")), Is.True);
            Assert.That(File.Exists(Path.Combine(referenceRoot, "CarModel", "bin", "Dynamics.AX.CarModel.dll")), Is.True);
        }

        [Test]
        public void ResolveReferencedCompiledNugetReferenceFolders_Should_Fail_When_Isv_Package_Is_Not_In_Profile()
        {
            var repositoryRoot = Path.Combine(_baseDir, "repo");
            var buildRoot = Path.Combine(repositoryRoot, "Build");
            var sourceMetadataRoot = Path.Combine(repositoryRoot, "Metadata", "PTSVikingAssistance");
            var descriptorRoot = Path.Combine(sourceMetadataRoot, "Descriptor");

            Directory.CreateDirectory(buildRoot);
            Directory.CreateDirectory(descriptorRoot);

            File.WriteAllText(Path.Combine(buildRoot, "isv.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="CarModel" version="1.0.0" />
                </packages>
                """);

            File.WriteAllText(Path.Combine(descriptorRoot, "PTSVikingAssistance.xml"), """
                <?xml version="1.0" encoding="utf-8"?>
                <AxModelInfo xmlns:i="http://www.w3.org/2001/XMLSchema-instance">
                  <ModuleReferences xmlns:d2p1="http://schemas.microsoft.com/2003/10/Serialization/Arrays">
                    <d2p1:string>CarModel</d2p1:string>
                  </ModuleReferences>
                  <Name>PTSVikingAssistance</Name>
                </AxModelInfo>
                """);

            var sourceModel = new ProfileEnvironmentModel
            {
                ModelName = "PTSVikingAssistance",
                ModelRootFolder = repositoryRoot,
                MetadataFolder = sourceMetadataRoot,
                ModelType = ModelType.Source
            };

            var profile = new ProfileModel
            {
                ProfileName = "Viking",
                Repositories =
                [
                    new RepositoryModel
                    {
                        DisplayName = "Peritus Viking Assistance",
                        RepoRootFolder = repositoryRoot,
                        Models = [sourceModel]
                    }
                ]
            };

            Assert.That(TryResolveReferencedCompiledNugetReferenceFolders(profile, sourceModel, out var referenceFolders), Is.False);
            Assert.That(referenceFolders, Is.Empty);
        }

        [Test]
        public void ResolveReferencedCompiledNugetReferenceFolders_Should_Ignore_Isv_Package_When_Model_Exists_As_Source_In_Profile()
        {
            var repositoryRoot = Path.Combine(_baseDir, "repo");
            var buildRoot = Path.Combine(repositoryRoot, "Build");
            var sourceMetadataRoot = Path.Combine(repositoryRoot, "Metadata", "PTSVikingAssistance");
            var sourceDescriptorRoot = Path.Combine(sourceMetadataRoot, "Descriptor");
            var referencedMetadataRoot = Path.Combine(repositoryRoot, "Metadata", "CarModel");

            Directory.CreateDirectory(buildRoot);
            Directory.CreateDirectory(sourceDescriptorRoot);
            Directory.CreateDirectory(referencedMetadataRoot);

            File.WriteAllText(Path.Combine(buildRoot, "isv.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="CarModel" version="1.0.0" />
                </packages>
                """);

            File.WriteAllText(Path.Combine(sourceDescriptorRoot, "PTSVikingAssistance.xml"), """
                <?xml version="1.0" encoding="utf-8"?>
                <AxModelInfo xmlns:i="http://www.w3.org/2001/XMLSchema-instance">
                  <ModuleReferences xmlns:d2p1="http://schemas.microsoft.com/2003/10/Serialization/Arrays">
                    <d2p1:string>CarModel</d2p1:string>
                  </ModuleReferences>
                  <Name>PTSVikingAssistance</Name>
                </AxModelInfo>
                """);

            var sourceModel = new ProfileEnvironmentModel
            {
                ModelName = "PTSVikingAssistance",
                ModelRootFolder = repositoryRoot,
                MetadataFolder = sourceMetadataRoot,
                ModelType = ModelType.Source
            };

            var referencedSourceModel = new ProfileEnvironmentModel
            {
                ModelName = "CarModel",
                ModelRootFolder = repositoryRoot,
                MetadataFolder = referencedMetadataRoot,
                ModelType = ModelType.Source
            };

            var profile = new ProfileModel
            {
                ProfileName = "Viking",
                Repositories =
                [
                    new RepositoryModel
                    {
                        DisplayName = "Peritus Viking Assistance",
                        RepoRootFolder = repositoryRoot,
                        Models = [sourceModel, referencedSourceModel]
                    }
                ]
            };

            Assert.That(TryResolveReferencedCompiledNugetReferenceFolders(profile, sourceModel, out var referenceFolders), Is.True);
            Assert.That(referenceFolders, Is.Empty);
        }

        [Test]
        public void EnsureCompiledNugetModels_Should_Not_Add_Package_When_Model_Exists_As_Source_In_Profile()
        {
            var repositoryRoot = Path.Combine(_baseDir, "repo");
            var buildRoot = Path.Combine(repositoryRoot, "Build");
            var metadataRoot = Path.Combine(repositoryRoot, "Metadata", "CarModel");
            var deployablePackagesRoot = Path.Combine(_baseDir, "DeployablePackages");
            var packageRoot = Path.Combine(deployablePackagesRoot, "CarModel-1.0.0");

            Directory.CreateDirectory(buildRoot);
            Directory.CreateDirectory(metadataRoot);
            Directory.CreateDirectory(packageRoot);

            File.WriteAllText(Path.Combine(buildRoot, "isv.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="CarModel" version="1.0.0" />
                </packages>
                """);
            File.WriteAllText(Path.Combine(buildRoot, "nuget.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources />
                </configuration>
                """);
            File.WriteAllText(Path.Combine(packageRoot, "CarModel.xref"), string.Empty);

            var sourceModel = new ProfileEnvironmentModel
            {
                ModelName = "CarModel",
                ModelRootFolder = repositoryRoot,
                MetadataFolder = metadataRoot,
                ModelType = ModelType.Source
            };

            var repository = new RepositoryModel
            {
                DisplayName = "Repo",
                RepoRootFolder = repositoryRoot,
                Models = [sourceModel]
            };
            var profile = new ProfileModel
            {
                ProfileName = "Profile",
                Repositories = [repository]
            };
            var service = new DeployablePackageService(new AppConfig
            {
                DeployablePackages = deployablePackagesRoot,
                DeploymentBasePath = Path.Combine(_baseDir, "Deployment")
            });

            var updated = service.EnsureCompiledNugetModels(profile, repository);

            Assert.That(updated, Is.False);
            Assert.That(repository.Models, Has.Count.EqualTo(1));
            Assert.That(repository.Models.Single(), Is.SameAs(sourceModel));
        }

        [Test]
        public void EnsureCompiledNugetModels_Should_Not_Add_Package_When_Model_Exists_As_Compiled_In_Profile()
        {
            var repositoryRoot = Path.Combine(_baseDir, "repo");
            var buildRoot = Path.Combine(repositoryRoot, "Build");
            var compiledFolder = Path.Combine(repositoryRoot, "Libs", "CarModel");
            var deployablePackagesRoot = Path.Combine(_baseDir, "DeployablePackages");
            var packageRoot = Path.Combine(deployablePackagesRoot, "CarModel-1.0.0");

            Directory.CreateDirectory(buildRoot);
            Directory.CreateDirectory(compiledFolder);
            Directory.CreateDirectory(packageRoot);

            File.WriteAllText(Path.Combine(buildRoot, "isv.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="CarModel" version="1.0.0" />
                </packages>
                """);
            File.WriteAllText(Path.Combine(buildRoot, "nuget.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources />
                </configuration>
                """);
            File.WriteAllText(Path.Combine(packageRoot, "CarModel.xref"), string.Empty);

            var compiledModel = new ProfileEnvironmentModel
            {
                ModelName = "CarModel",
                ModelRootFolder = repositoryRoot,
                CompiledModelFolder = compiledFolder,
                ModelType = ModelType.Compiled
            };

            var repository = new RepositoryModel
            {
                DisplayName = "Repo",
                RepoRootFolder = repositoryRoot,
                Models = [compiledModel]
            };
            var profile = new ProfileModel
            {
                ProfileName = "Profile",
                Repositories = [repository]
            };
            var service = new DeployablePackageService(new AppConfig
            {
                DeployablePackages = deployablePackagesRoot,
                DeploymentBasePath = Path.Combine(_baseDir, "Deployment")
            });

            var updated = service.EnsureCompiledNugetModels(profile, repository);

            Assert.That(updated, Is.False);
            Assert.That(repository.Models, Has.Count.EqualTo(1));
            Assert.That(repository.Models.Single(), Is.SameAs(compiledModel));
        }

        [Test]
        public void EnsureCompiledNugetModels_Should_Update_Existing_CompiledNuget_Model_With_Same_Name()
        {
            var repositoryRoot = Path.Combine(_baseDir, "repo");
            var buildRoot = Path.Combine(repositoryRoot, "Build");
            var deployablePackagesRoot = Path.Combine(_baseDir, "DeployablePackages");
            var packageRoot = Path.Combine(deployablePackagesRoot, "CarModel-1.0.0");
            var packageBinRoot = Path.Combine(packageRoot, "bin");

            Directory.CreateDirectory(buildRoot);
            Directory.CreateDirectory(packageBinRoot);

            File.WriteAllText(Path.Combine(buildRoot, "isv.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="CarModel" version="1.0.0" />
                </packages>
                """);
            File.WriteAllText(Path.Combine(buildRoot, "nuget.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources />
                </configuration>
                """);
            File.WriteAllText(Path.Combine(packageRoot, "CarModel.xref"), string.Empty);
            File.WriteAllText(Path.Combine(packageBinRoot, "Dynamics.AX.CarModel.dll"), string.Empty);

            var existingNugetModel = new ProfileEnvironmentModel
            {
                ModelName = "CarModel",
                ModelRootFolder = repositoryRoot,
                CompiledModelFolder = Path.Combine(_baseDir, "old", "CarModel"),
                PackageId = "CarModel",
                PackageVersion = "0.9.0",
                ModelType = ModelType.CompiledNuget
            };

            var repository = new RepositoryModel
            {
                DisplayName = "Repo",
                RepoRootFolder = repositoryRoot,
                Models = [existingNugetModel]
            };
            var profile = new ProfileModel
            {
                ProfileName = "Profile",
                Repositories = [repository]
            };
            var service = new DeployablePackageService(new AppConfig
            {
                DeployablePackages = deployablePackagesRoot,
                DeploymentBasePath = Path.Combine(_baseDir, "Deployment")
            });

            var updated = service.EnsureCompiledNugetModels(profile, repository);

            Assert.That(updated, Is.True);
            Assert.That(repository.Models, Has.Count.EqualTo(1));
            Assert.That(repository.Models.Single(), Is.SameAs(existingNugetModel));
            Assert.That(existingNugetModel.CompiledModelFolder, Is.EqualTo(packageRoot));
            Assert.That(existingNugetModel.PackageVersion, Is.EqualTo("1.0.0"));
        }

        [Test]
        public void EnsureCompiledNugetModels_Should_Not_Add_Resolved_Model_When_Model_Name_Already_Exists()
        {
            var repositoryRoot = Path.Combine(_baseDir, "repo");
            var buildRoot = Path.Combine(repositoryRoot, "Build");
            var deployablePackagesRoot = Path.Combine(_baseDir, "DeployablePackages");
            var packageRoot = Path.Combine(deployablePackagesRoot, "Vendor.Package-1.0.0");

            Directory.CreateDirectory(buildRoot);
            Directory.CreateDirectory(packageRoot);

            File.WriteAllText(Path.Combine(buildRoot, "isv.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <packages>
                  <package id="Vendor.Package" version="1.0.0" />
                </packages>
                """);
            File.WriteAllText(Path.Combine(buildRoot, "nuget.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources />
                </configuration>
                """);
            File.WriteAllText(Path.Combine(packageRoot, "CarModel.xref"), string.Empty);

            var sourceModel = new ProfileEnvironmentModel
            {
                ModelName = "CarModel",
                ModelRootFolder = repositoryRoot,
                MetadataFolder = Path.Combine(repositoryRoot, "Metadata", "CarModel"),
                ModelType = ModelType.Source
            };

            var repository = new RepositoryModel
            {
                DisplayName = "Repo",
                RepoRootFolder = repositoryRoot,
                Models = [sourceModel]
            };
            var profile = new ProfileModel
            {
                ProfileName = "Profile",
                Repositories = [repository]
            };
            var service = new DeployablePackageService(new AppConfig
            {
                DeployablePackages = deployablePackagesRoot,
                DeploymentBasePath = Path.Combine(_baseDir, "Deployment")
            });

            var updated = service.EnsureCompiledNugetModels(profile, repository);

            Assert.That(updated, Is.False);
            Assert.That(repository.Models, Has.Count.EqualTo(1));
            Assert.That(repository.Models.Single(), Is.SameAs(sourceModel));
        }

        [Test]
        public void ResolveBuildMetadataDirectory_Should_Use_Source_Metadata_Parent_When_Available()
        {
            var metadataRoot = Path.Combine(_baseDir, "Metadata");
            var modelMetadataRoot = Path.Combine(metadataRoot, "PTSVikingAssistance");
            Directory.CreateDirectory(modelMetadataRoot);

            var model = new ProfileEnvironmentModel
            {
                ModelName = "PTSVikingAssistance",
                MetadataFolder = modelMetadataRoot,
                ModelType = ModelType.Source
            };

            var metadataDirectory = ResolveBuildMetadataDirectory(model, @"C:\AOSService\PackagesLocalDirectory");

            Assert.That(metadataDirectory, Is.EqualTo(metadataRoot));
        }

        [Test]
        public void BuildMsBuildArguments_Should_Request_Restore_For_Sdk_Project_References()
        {
            var context = CreateMsBuildContext(
                @"C:\BuildPackages\Compiler",
                @"C:\BuildPackages\Compiler\DevAlm",
                @"C:\Dev\FO\Repo\Metadata",
                @"C:\AOSService\PackagesLocalDirectory;C:\Packages\CarModel");

            var arguments = BuildMsBuildArguments(
                @"C:\Dev\FO\Repo\Model.packagebuild.sln",
                context,
                @"C:\Temp\BuildOutput");

            Assert.That(arguments, Does.Contain("/restore"));
            Assert.That(arguments, Does.Contain("/p:MetadataDirectory=\"C:\\Dev\\FO\\Repo\\Metadata\""));
        }

        [Test]
        public void IsNuGetNoPackagesFoundOutput_Should_Ignore_List_Deprecation_Warning()
        {
            var output = """
                WARNING: 'NuGet list' is deprecated. Use 'NuGet search' instead.
                No packages found.
                """;

            Assert.That(IsNuGetNoPackagesFoundOutput(output), Is.True);
        }

        [Test]
        public void CreateCredentialedNugetConfig_Should_Add_Azure_Artifacts_Source_Credentials()
        {
            var nugetConfigPath = Path.Combine(_baseDir, "nuget.config");
            File.WriteAllText(nugetConfigPath, """
                <?xml version="1.0" encoding="utf-8"?>
                <configuration>
                  <packageSources>
                    <add key="PeritusPackages" value="https://peritus-no.pkgs.visualstudio.com/_packaging/PeritusPackages/nuget/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            var service = new DeployablePackageService(new AppConfig
            {
                DeployablePackages = Path.Combine(_baseDir, "DeployablePackages"),
                DeploymentBasePath = Path.Combine(_baseDir, "Metadata"),
                AzureArtifactsUsername = "FODevManager",
                AzureArtifactsPat = "pat-token"
            });

            var created = CreateCredentialedNugetConfig(service, nugetConfigPath, out var credentialedConfigPath);

            Assert.That(created, Is.True);
            Assert.That(credentialedConfigPath, Is.Not.EqualTo(nugetConfigPath));

            var document = XDocument.Load(credentialedConfigPath);
            var credentials = document
                .Descendants("packageSourceCredentials")
                .Elements("PeritusPackages")
                .Elements("add")
                .ToDictionary(
                    element => element.Attribute("key")!.Value,
                    element => element.Attribute("value")!.Value);

            Assert.That(credentials["Username"], Is.EqualTo("FODevManager"));
            Assert.That(credentials["ClearTextPassword"], Is.EqualTo("pat-token"));
        }

        private static string ResolveCompiledModelName(string modelFolder, string packageId, string packageVersion)
        {
            var serviceType = typeof(DeployablePackageService);
            var packageReferenceType = serviceType.GetNestedType("PackageReference", BindingFlags.NonPublic);
            Assert.That(packageReferenceType, Is.Not.Null);

            var packageReference = Activator.CreateInstance(packageReferenceType!, packageId, packageVersion);
            var method = serviceType.GetMethod("ResolveCompiledModelName", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return (string)method!.Invoke(null, [modelFolder, packageReference])!;
        }

        private static IReadOnlyList<string> ResolveReferencedCompiledNugetReferenceFolders(ProfileModel profile, ProfileEnvironmentModel model)
        {
            var succeeded = TryResolveReferencedCompiledNugetReferenceFolders(profile, model, out var referenceFolders);

            Assert.That(succeeded, Is.True);
            return referenceFolders;
        }

        private static bool TryResolveReferencedCompiledNugetReferenceFolders(
            ProfileModel profile,
            ProfileEnvironmentModel model,
            out IReadOnlyList<string> referenceFolders)
        {
            var service = new DeployablePackageService(new AppConfig
            {
                DeployablePackages = Path.Combine(Path.GetTempPath(), "FODevManagerTests", "DeployablePackages"),
                DeploymentBasePath = Path.Combine(Path.GetTempPath(), "FODevManagerTests", "Metadata")
            });

            var method = typeof(DeployablePackageService).GetMethod("TryResolveReferencedCompiledNugetReferenceFolders", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null);

            object?[] parameters = [profile, model, null];
            var succeeded = (bool)method!.Invoke(service, parameters)!;
            referenceFolders = (IReadOnlyList<string>?)parameters[2] ?? [];

            return succeeded;
        }

        private static string ResolveBuildMetadataDirectory(ProfileEnvironmentModel model, string fallbackMetadataDirectory)
        {
            var method = typeof(DeployablePackageService).GetMethod("ResolveBuildMetadataDirectory", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return (string)method!.Invoke(null, [model, fallbackMetadataDirectory])!;
        }

        private static string PrepareCompiledNugetReferenceRoot(string runRoot, IReadOnlyCollection<ProfileEnvironmentModel> compiledModels)
        {
            var method = typeof(DeployablePackageService).GetMethod("PrepareCompiledNugetReferenceRoot", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return (string)method!.Invoke(null, [runRoot, compiledModels])!;
        }

        private static object CreateMsBuildContext(string compilerPackageRoot, string buildTasksDirectory, string metadataDirectory, string referenceFolder)
        {
            var contextType = typeof(DeployablePackageService).GetNestedType("MsBuildContext", BindingFlags.NonPublic);
            Assert.That(contextType, Is.Not.Null);

            return Activator.CreateInstance(contextType!, compilerPackageRoot, buildTasksDirectory, metadataDirectory, referenceFolder)!;
        }

        private static bool IsNuGetNoPackagesFoundOutput(string output)
        {
            var method = typeof(DeployablePackageService).GetMethod("IsNuGetNoPackagesFoundOutput", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return (bool)method!.Invoke(null, [output])!;
        }

        private static bool CreateCredentialedNugetConfig(DeployablePackageService service, string nugetConfigPath, out string credentialedConfigPath)
        {
            var method = typeof(DeployablePackageService).GetMethod("CreateCredentialedNugetConfig", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null);

            object?[] parameters = [nugetConfigPath, null];
            var created = (bool)method!.Invoke(service, parameters)!;
            credentialedConfigPath = (string?)parameters[1] ?? string.Empty;
            return created;
        }

        private static string BuildMsBuildArguments(string solutionFilePath, object buildContext, string buildOutputRoot)
        {
            var method = typeof(DeployablePackageService).GetMethod("BuildMsBuildArguments", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return (string)method!.Invoke(null, [solutionFilePath, buildContext, buildOutputRoot])!;
        }
    }
}
