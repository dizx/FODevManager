using FODevManager.Services;
using FODevManager.Models;
using FODevManager.Utils;
using System.Reflection;

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

        private static string BuildMsBuildArguments(string solutionFilePath, object buildContext, string buildOutputRoot)
        {
            var method = typeof(DeployablePackageService).GetMethod("BuildMsBuildArguments", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return (string)method!.Invoke(null, [solutionFilePath, buildContext, buildOutputRoot])!;
        }
    }
}
