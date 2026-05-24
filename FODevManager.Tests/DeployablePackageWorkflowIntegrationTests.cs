using FODevManager.Models;
using FODevManager.Messages;
using FODevManager.Services;
using FODevManager.Utils;
using System.Text.Json;

namespace FODevManager.Tests
{
    [TestFixture]
    [Category("LocalIntegration")]
    public class DeployablePackageWorkflowIntegrationTests
    {
        private const string VikingProfilePath = @"C:\Users\morten\AppData\Roaming\FODevManager\viking.json";
        private const string VikingRepositoryRoot = @"C:\Dev\FO\Peritus Viking Assistance";
        private const string SourceRoot = @"C:\Dev\Source\FODevManager";
        private const string ModelName = "PTSVikingAssistance";

        [Test]
        [CancelAfter(600000)]
        public void BuildDeployableNugetPackage_Should_Build_PTSVikingAssistance_With_CarModel_Nuget_Reference()
        {
            Assert.That(File.Exists(VikingProfilePath), Is.True, $"Profile file is required: {VikingProfilePath}");
            Assert.That(Directory.Exists(VikingRepositoryRoot), Is.True, $"Repository is required: {VikingRepositoryRoot}");
            Assert.That(Directory.Exists(SourceRoot), Is.True, $"Source root is required: {SourceRoot}");
            Assert.That(File.Exists(@"C:\nuget\nugetutil.exe"), Is.True, @"NugetUtil is required at C:\nuget\nugetutil.exe");

            var profile = JsonSerializer.Deserialize<ProfileModel>(File.ReadAllText(VikingProfilePath));
            Assert.That(profile, Is.Not.Null);

            var model = profile!.FindModel(ModelName);
            Assert.That(model, Is.Not.Null);

            var config = new AppConfig
            {
                ProfileStoragePath = Path.GetDirectoryName(VikingProfilePath)!,
                DeploymentBasePath = @"C:\AOSService\PackagesLocalDirectory",
                DeployablePackages = @"C:\Users\morten\AppData\Roaming\FODevManager\DeployablePackages",
                DefaultSourceDirectory = @"C:\Dev",
                TaskUrl = AppConfig.DefaultTaskUrl,
                NuGetExecutablePath = @"C:\nuget\nuget.exe",
                AzureArtifactsPat = "local-integration-test-uses-installed-build-packages",
                PushDeployablePackageOnBuild = false
            };

            var originalCurrentDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(SourceRoot);

            try
            {
                var solutionService = new VisualStudioSolutionService(config);
                var packageService = new DeployablePackageService(config);
                var solutionFilePath = solutionService.CreateBuildSolution(profile, model!);
                var artifactsRoot = Path.Combine(VikingRepositoryRoot, "Artifacts", "BuildPackages", ModelName);
                var startedAt = DateTime.Now;

                var messages = new List<string>();
                using var subscription = MessageBus.Subscribe(message => messages.Add(message.Content));

                var succeeded = packageService.BuildDeployableNugetPackage(profile, model!, solutionFilePath);

                Assert.That(succeeded, Is.True, string.Join(Environment.NewLine, messages));
                Assert.That(Directory.Exists(artifactsRoot), Is.True);

                var createdPackages = Directory
                    .GetFiles(artifactsRoot, "*.nupkg", SearchOption.AllDirectories)
                    .Where(path => File.GetLastWriteTime(path) >= startedAt.AddSeconds(-1))
                    .ToList();

                Assert.That(createdPackages, Is.Not.Empty);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCurrentDirectory);
            }
        }
    }
}
