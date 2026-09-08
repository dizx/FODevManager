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
            Assert.That(arguments, Does.Contain("/t:Rebuild"));
            Assert.That(arguments, Does.Contain("/p:Configuration=Debug"));
            Assert.That(arguments, Does.Contain("/p:ReferencePath=\"C:\\AOSService\\PackagesLocalDirectory;C:\\Packages\\CarModel;"));
            Assert.That(arguments, Does.Contain("/p:MetadataDirectory=\"C:\\Dev\\FO\\Repo\\Metadata\""));
        }

        [Test]
        public void BuildContext_Should_Use_Mapped_Package_Roots_Without_Changing_Stored_Paths()
        {
            if (!OperatingSystem.IsWindows())
                Assert.Ignore("DOS-device mappings are only available on Windows");

            var config = new AppConfig
            {
                DeployablePackages = Path.Combine(_baseDir, "DeployablePackages"),
                DeploymentBasePath = Path.Combine(_baseDir, "Metadata")
            };
            var service = new DeployablePackageService(config);
            var model = new ProfileEnvironmentModel { ModelRootFolder = _baseDir, MetadataFolder = config.DeploymentBasePath };
            var profile = new ProfileModel { StandaloneModels = [model] };
            var originalConfig = System.Text.Json.JsonSerializer.Serialize(config);
            var originalProfile = System.Text.Json.JsonSerializer.Serialize(profile);
            var packageIds = new[]
            {
                "Microsoft.Dynamics.AX.Platform.CompilerPackage",
                "Microsoft.Dynamics.AX.Platform.DevALM.BuildXpp",
                "Microsoft.Dynamics.AX.Application1.DevALM.BuildXpp",
                "Microsoft.Dynamics.AX.Application2.DevALM.BuildXpp",
                "Microsoft.Dynamics.AX.ApplicationSuite.DevALM.BuildXpp"
            };
            var roots = packageIds.ToDictionary(id => id,
                id => Path.Combine(config.DeployablePackages, "BuildPackages", id + ".1.0.0"));
            foreach (var root in roots.Values)
            {
                Directory.CreateDirectory(Path.Combine(root, "DevAlm"));
                Directory.CreateDirectory(Path.Combine(root, "ref", "net40"));
                File.WriteAllText(Path.Combine(root, "identity.txt"), root);
            }

            using var scope = BuildCachePathScope.Create(Path.Combine(config.DeployablePackages, "BuildPackages"));
            var mappedRoot = scope.RootPath;
            var effectiveRoots = roots.ToDictionary(pair => pair.Key, pair => scope.MapPath(pair.Value));
            foreach (var pair in effectiveRoots)
            {
                Assert.That(File.ReadAllText(Path.Combine(pair.Value, "identity.txt")), Is.EqualTo(roots[pair.Key]));
                File.WriteAllText(Path.Combine(pair.Value, "identity.txt"), "same file");
                Assert.That(File.ReadAllText(Path.Combine(roots[pair.Key], "identity.txt")), Is.EqualTo("same file"));
            }

            var method = typeof(DeployablePackageService).GetMethod("TryBuildMsBuildContext", BindingFlags.NonPublic | BindingFlags.Instance)!;
            object?[] parameters = [effectiveRoots, Array.Empty<string>(), model.MetadataFolder, null];
            Assert.That((bool)method.Invoke(service, parameters)!, Is.True);
            var arguments = BuildMsBuildArguments("test.sln", parameters[3]!, Path.Combine(_baseDir, "output"));
            Assert.That(arguments, Does.Contain($"/p:FrameworkDirectory=\"{effectiveRoots[packageIds[0]]}\""));
            Assert.That(arguments, Does.Contain($"/p:BuildTasksDirectory=\"{Path.Combine(effectiveRoots[packageIds[0]], "DevAlm")}\""));
            foreach (var id in packageIds.Skip(1))
                Assert.That(arguments, Does.Contain(Path.Combine(effectiveRoots[id], "ref", "net40")));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(config), Is.EqualTo(originalConfig));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(profile), Is.EqualTo(originalProfile));
            foreach (var pair in effectiveRoots)
            {
                Assert.That(pair.Value, Does.StartWith(mappedRoot));
                Assert.That(pair.Value.Length, Is.LessThan(roots[pair.Key].Length));
                Assert.That(arguments, Does.Not.Contain(roots[pair.Key]));
            }
            scope.Dispose();
            Assert.That(Directory.Exists(mappedRoot), Is.False);
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

        [TestCase(true)]
        [TestCase(false)]
        public void BuildCompiledModelPackage_Should_Stage_Complete_Payload_Without_Build_Configuration_Or_Mutation(bool repositoryBacked)
        {
            var model = CreateCompiledPayload();
            var profile = new ProfileModel { ProfileName = "CompiledProfile" };
            if (repositoryBacked)
                profile.Repositories = [new RepositoryModel { RepoRootFolder = _baseDir, Models = [model] }];
            else
                profile.StandaloneModels = [model];

            var originalProfile = System.Text.Json.JsonSerializer.Serialize(profile);
            var originalFiles = Directory.GetFiles(model.CompiledModelFolder, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(model.CompiledModelFolder, path), File.ReadAllBytes);
            var stagedRoots = new List<string>();
            Func<string, string, bool> packagePayload = (payloadRoot, outputRoot) =>
            {
                stagedRoots.Add(payloadRoot);
                Assert.That(Path.GetFileName(payloadRoot), Is.EqualTo(model.ModelName));
                Assert.That(payloadRoot, Does.StartWith(Path.Combine(_baseDir, "Artifacts", "BuildPackages", model.ModelName)));
                Assert.That(Directory.Exists(outputRoot), Is.True);
                Assert.That(Directory.GetFiles(payloadRoot, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(payloadRoot, path)), Is.EquivalentTo(originalFiles.Keys));
                foreach (var file in originalFiles)
                    Assert.That(File.ReadAllBytes(Path.Combine(payloadRoot, file.Key)), Is.EqualTo(file.Value));

                File.WriteAllText(Path.Combine(payloadRoot, "generated.nuspec"), "staged only");
                return true;
            };

            Assert.That(BuildCompiledModelPackage(profile, model, packagePayload), Is.True);
            Assert.That(BuildCompiledModelPackage(profile, model, packagePayload), Is.True);
            Assert.That(stagedRoots.Distinct().Count(), Is.EqualTo(2));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(profile), Is.EqualTo(originalProfile));
            Assert.That(Directory.GetFiles(model.CompiledModelFolder, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(model.CompiledModelFolder, path)), Is.EquivalentTo(originalFiles.Keys));
            foreach (var file in originalFiles)
                Assert.That(File.ReadAllBytes(Path.Combine(model.CompiledModelFolder, file.Key)), Is.EqualTo(file.Value));
        }

        [TestCase("missing-root")]
        [TestCase("empty-root")]
        [TestCase("relative-root")]
        [TestCase("missing-descriptor")]
        [TestCase("invalid-descriptor")]
        [TestCase("wrong-model")]
        [TestCase("xref-only")]
        [TestCase("invalid-assembly")]
        [TestCase("invalid-name")]
        public void BuildCompiledModelPackage_Should_Reject_Invalid_Payload(string invalidPayload)
        {
            var model = CreateCompiledPayload();
            var descriptorPath = Path.Combine(model.CompiledModelFolder, "Descriptor", "TestModel.xml");
            var assemblyPath = Path.Combine(model.CompiledModelFolder, "bin", "Dynamics.AX.TestModel.dll");
            switch (invalidPayload)
            {
                case "missing-root": model.CompiledModelFolder = Path.Combine(_baseDir, "missing"); break;
                case "empty-root": model.CompiledModelFolder = string.Empty; break;
                case "relative-root": model.CompiledModelFolder = "relative"; break;
                case "missing-descriptor": File.Delete(descriptorPath); break;
                case "invalid-descriptor": File.WriteAllText(descriptorPath, "invalid xml"); break;
                case "wrong-model": File.WriteAllText(descriptorPath, "<AxModelInfo><Name>OtherModel</Name></AxModelInfo>"); break;
                case "xref-only":
                    File.Delete(assemblyPath);
                    File.WriteAllText(Path.Combine(model.CompiledModelFolder, "TestModel.xref"), "xref");
                    break;
                case "invalid-assembly": File.WriteAllText(assemblyPath, "not an assembly"); break;
                case "invalid-name": model.ModelName = "../TestModel"; break;
            }

            var called = false;
            Assert.That(BuildCompiledModelPackage(new ProfileModel(), model, (_, _) => { called = true; return true; }), Is.False);
            Assert.That(called, Is.False);
            Assert.That(Directory.Exists(Path.Combine(_baseDir, "Artifacts")), Is.False);
        }

        [Test]
        public void BuildCompiledModelPackage_Should_Keep_Artifacts_Outside_Standalone_Compiled_Root_And_Propagate_Packaging_Failure()
        {
            var model = CreateCompiledPayload();
            model.ModelRootFolder = model.CompiledModelFolder;
            Assert.That(BuildCompiledModelPackage(new ProfileModel { StandaloneModels = [model] }, model,
                (payloadRoot, _) =>
                {
                    Assert.That(payloadRoot, Does.Not.StartWith(model.CompiledModelFolder + Path.DirectorySeparatorChar));
                    return false;
                }), Is.False);
            Assert.That(Directory.Exists(Path.Combine(model.CompiledModelFolder, "Artifacts")), Is.False);
        }

        [Test]
        public void BuildDeployableNugetPackage_Should_Refuse_CompiledNuget_Without_Restore()
        {
            var service = new DeployablePackageService(new AppConfig { DeployablePackages = string.Empty });
            var model = CreateCompiledPayload();
            model.ModelType = ModelType.CompiledNuget;
            Assert.That(service.BuildDeployableNugetPackage(new ProfileModel(), model, string.Empty), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_baseDir, "Artifacts")), Is.False);
        }

        [Test]
        public void BuildDeployableNugetPackage_Should_Not_Fall_Back_To_Compiled_Payload_For_Source_Without_Solution()
        {
            var service = new DeployablePackageService(new AppConfig { DeployablePackages = string.Empty });
            var model = CreateCompiledPayload();
            model.ModelType = ModelType.Source;
            Assert.That(service.BuildDeployableNugetPackage(new ProfileModel(), model, string.Empty), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_baseDir, "Artifacts")), Is.False);
        }

        private ProfileEnvironmentModel CreateCompiledPayload()
        {
            var compiledRoot = Path.Combine(_baseDir, "compiled-original");
            Directory.CreateDirectory(Path.Combine(compiledRoot, "Descriptor"));
            Directory.CreateDirectory(Path.Combine(compiledRoot, "bin", "runtimes", "win-x64", "native"));
            Directory.CreateDirectory(Path.Combine(compiledRoot, "Resources", "nested"));
            File.WriteAllText(Path.Combine(compiledRoot, "Descriptor", "TestModel.xml"),
                "<AxModelInfo><Name>TestModel</Name></AxModelInfo>");
            File.Copy(typeof(DeployablePackageService).Assembly.Location,
                Path.Combine(compiledRoot, "bin", "Dynamics.AX.TestModel.dll"));
            File.WriteAllText(Path.Combine(compiledRoot, "bin", "Peritus.Ssh.dll"), "dependency");
            File.WriteAllText(Path.Combine(compiledRoot, "bin", "Renci.SshNet.dll"), "dependency");
            File.WriteAllText(Path.Combine(compiledRoot, "bin", "runtimes", "win-x64", "native", "support.dll"), "nested dependency");
            File.WriteAllText(Path.Combine(compiledRoot, "Resources", "nested", "resource"), "deployable metadata");
            return new ProfileEnvironmentModel
            {
                ModelName = "TestModel",
                ModelType = ModelType.Compiled,
                ModelRootFolder = _baseDir,
                CompiledModelFolder = compiledRoot,
                ProjectFilePath = string.Empty,
                MetadataFolder = string.Empty
            };
        }

        private static bool BuildCompiledModelPackage(ProfileModel profile, ProfileEnvironmentModel model, Func<string, string, bool> packagePayload)
        {
            var method = typeof(DeployablePackageService).GetMethod("BuildCompiledModelPackage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (bool)method!.Invoke(null, [profile, model, packagePayload])!;
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
