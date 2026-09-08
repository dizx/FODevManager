using System.Text.Json;
using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Services;
using FODevManager.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace FODevManager.Tests;

[TestFixture]
[NonParallelizable]
public class CliHostTests
{
    private string _root = null!;
    private string _profilePath = null!;
    private string _originalProfile = null!;
    private readonly Dictionary<string, string?> _environment = new();

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "FODevManager-CliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "profiles"));
        foreach (var (key, directory) in new[]
        {
            ("ProfileStoragePath", "profiles"), ("DeploymentBasePath", "deployment"),
            ("DefaultSourceDirectory", "source"), ("DeployablePackages", "packages")
        })
        {
            _environment[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, Path.Combine(_root, directory));
        }
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), JsonSerializer.Serialize(new
        {
            ProfileStoragePath = Path.Combine(_root, "profiles"),
            DeploymentBasePath = Path.Combine(_root, "deployment"),
            DefaultSourceDirectory = Path.Combine(_root, "source"),
            DeployablePackages = Path.Combine(_root, "packages"),
            TaskUrl = "https://example.invalid/json/",
            PushDeployablePackageOnBuild = true
        }));
        var profile = new ProfileModel
        {
            ProfileName = "Test",
            ProfileFilePath = Path.Combine(_root, "artifact.json"),
            StandaloneModels = new()
            {
                new() { ModelName = "SourceModel", ModelType = ModelType.Source },
                new() { ModelName = "CompiledModel", ModelType = ModelType.Compiled }
            },
            Repositories = new()
            {
                new() { RepoId = "repo", DisplayName = "Example", RepoRootFolder = Path.Combine(_root, "repo"),
                    GitUrl = "https://example.invalid/repo.git", Models = new()
                    {
                        new() { ModelName = "RepoModel", ModelType = ModelType.Source }
                    }
                }
            }
        };
        _profilePath = Path.Combine(_root, "profiles", "Test.json");
        _originalProfile = JsonSerializer.Serialize(profile);
        File.WriteAllText(_profilePath, _originalProfile);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var (key, value) in _environment)
            Environment.SetEnvironmentVariable(key, value);
        _environment.Clear();
        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void CompositionResolvesAllServicesAndDisablesPublishing()
    {
        using var host = Program.BuildHost(_root);
        Assert.Multiple(() =>
        {
            Assert.That(host.Services.GetRequiredService<ProfileService>(), Is.Not.Null);
            Assert.That(host.Services.GetRequiredService<ModelDeploymentService>(), Is.Not.Null);
            Assert.That(host.Services.GetRequiredService<ModelVersionService>(), Is.Not.Null);
            Assert.That(host.Services.GetRequiredService<IDeploymentLedgerService>(), Is.TypeOf<DeploymentLedgerService>());
            Assert.That(host.Services.GetRequiredService<IDirectoryLinkService>(), Is.TypeOf<DirectoryLinkService>());
            Assert.That(host.Services.GetRequiredService<AppConfig>().PushDeployablePackageOnBuild, Is.False);
        });
    }

    [TestCase("show")]
    [TestCase("repos")]
    [TestCase("list")]
    [TestCase("solution-path")]
    public void ReadCommandsDoNotMutateProfileOrConstructDeploymentServices(string command)
    {
        var messages = new List<Message>();
        using var subscription = MessageBus.Subscribe(messages.Add);
        Assert.That(Run("-profile", "Test", command), Is.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(messages.Any(message => message.Type == MessageType.Info), Is.True);
            Assert.That(File.ReadAllText(_profilePath), Is.EqualTo(_originalProfile));
            Assert.That(File.Exists(Path.Combine(_root, "artifact.json")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_root, "deployment")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_root, "repo")), Is.False);
        });
    }

    [Test]
    public void ExportIsPortableAndRefusesOverwrite()
    {
        var output = Path.Combine(_root, "export.json");
        Assert.That(Run("-profile", "Test", "export", output), Is.Zero);
        var exported = File.ReadAllText(output);
        using var json = JsonDocument.Parse(exported);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.TryGetProperty("ProfileFilePath", out _), Is.False);
            Assert.That(json.RootElement.GetProperty("StandaloneModels").GetArrayLength(), Is.EqualTo(2));
            Assert.That(json.RootElement.GetProperty("Repositories").GetArrayLength(), Is.EqualTo(1));
            Assert.That(exported, Does.Not.Contain("RepoRootFolder"));
            Assert.That(Run("-profile", "Test", "export", output), Is.EqualTo(1));
            Assert.That(File.ReadAllText(output), Is.EqualTo(exported));
            Assert.That(File.ReadAllText(_profilePath), Is.EqualTo(_originalProfile));
            Assert.That(File.Exists(Path.Combine(_root, "artifact.json")), Is.False);
        });
    }

    [Test]
    public void UsageAndHelpDoNotBuildHost()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Program.Execute(new[] { "help" }, () => throw new AssertionException("Host should not be built")), Is.Zero);
            Assert.That(Program.Execute(new[] { "-profile", "Test", "package-build" }, () => throw new AssertionException("Host should not be built")), Is.EqualTo(2));
        });
    }

    [Test]
    public void StartupAndMissingProfileAndModelFailuresReturnOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Program.Execute(new[] { "list" }, () => throw new InvalidOperationException("Startup failed")), Is.EqualTo(1));
            Assert.That(Run("-profile", "Missing", "show"), Is.EqualTo(1));
            Assert.That(Run("-profile", "Test", "-model", "Missing", "show"), Is.EqualTo(1));
        });
    }

    [Test]
    public void LoggedErrorsFailCommandAndSubscriptionDoesNotLeak()
    {
        Assert.That(Program.Execute(new[] { "list" }, () =>
        {
            MessageLogger.Error("Simulated swallowed startup error");
            return Program.BuildHost(_root);
        }), Is.EqualTo(1));
        Assert.That(Run("list"), Is.Zero);
    }

    [Test]
    public void SolutionEnsureCapturesSwallowedServiceErrors()
    {
        Assert.That(Run("-profile", "Test", "solution-ensure"), Is.EqualTo(1));
        Assert.That(File.ReadAllText(_profilePath), Is.EqualTo(_originalProfile));
    }

    [Test]
    public void PeriOnlySavesRepositoryTaskWithoutAccessingGit()
    {
        Assert.That(Run("-profile", "Test", "-model", "RepoModel", "peri", "123"), Is.Zero);
        using var host = Program.BuildHost(_root);
        var profile = host.Services.GetRequiredService<FileService>().LoadProfile("Test");
        Assert.Multiple(() =>
        {
            Assert.That(profile.Repositories[0].Task, Is.EqualTo("123"));
            Assert.That(Directory.Exists(Path.Combine(_root, "repo")), Is.False);
        });
    }

    [Test]
    public void FalseGitResultFailsCommandWithoutNetwork()
    {
        Assert.That(Run("-profile", "Test", "-model", "CompiledModel", "git-status"), Is.EqualTo(1));
    }

    [TestCase("SourceModel")]
    [TestCase("CompiledModel")]
    [TestCase("RepoModel")]
    public void RemovalWithoutDeploymentRemovesMembership(string modelName)
    {
        Assert.That(Run("-profile", "Test", "-model", modelName, "remove"), Is.Zero);
        using var host = Program.BuildHost(_root);
        Assert.That(host.Services.GetRequiredService<FileService>().LoadProfile("Test").FindModel(modelName), Is.Null);
    }

    [Test]
    public void RemovalRefusesRealDeploymentDirectoryWithoutChangingMembership()
    {
        var deploymentPath = Path.Combine(_root, "deployment", "SourceModel");
        Directory.CreateDirectory(deploymentPath);
        var sentinel = Path.Combine(deploymentPath, "keep.txt");
        File.WriteAllText(sentinel, "Do not delete");
        Assert.That(Run("-profile", "Test", "-model", "SourceModel", "remove"), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(sentinel), Is.EqualTo("Do not delete"));
            Assert.That(File.ReadAllText(_profilePath), Is.EqualTo(_originalProfile));
        });
    }

    [Test]
    public void PackageBuildWithMissingProjectFailsBeforeExternalTools()
    {
        Assert.That(Run("-profile", "Test", "-model", "SourceModel", "package-build"), Is.EqualTo(1));
    }

    [Test]
    public void RemovalRefusesPackageWideSideEffects()
    {
        var profile = JsonSerializer.Deserialize<ProfileModel>(_originalProfile)!;
        profile.StandaloneModels[1].ModelType = ModelType.CompiledNuget;
        var before = JsonSerializer.Serialize(profile);
        File.WriteAllText(_profilePath, before);
        Assert.That(Run("-profile", "Test", "-model", "CompiledModel", "remove"), Is.EqualTo(1));
        Assert.That(File.ReadAllText(_profilePath), Is.EqualTo(before));
    }

    [Test]
    public void EmptySolutionEnsureIsIdempotent()
    {
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(new ProfileModel { ProfileName = "Test" }));
        Assert.That(Run("-profile", "Test", "solution-ensure"), Is.Zero);
        var solutionPath = Path.Combine(_root, "source", "Test", "Test.sln");
        var solution = File.ReadAllText(solutionPath);
        Assert.That(Run("-profile", "Test", "solution-ensure"), Is.Zero);
        Assert.That(File.ReadAllText(solutionPath), Is.EqualTo(solution));
    }

    [Test]
    public void EnvironmentOverridesJsonAndDefaultFileIsOptional()
    {
        var previous = Environment.GetEnvironmentVariable("TaskUrl");
        try
        {
            Environment.SetEnvironmentVariable("TaskUrl", "https://example.invalid/override/");
            using (var host = Program.BuildHost(_root))
                Assert.That(host.Services.GetRequiredService<AppConfig>().TaskUrl, Is.EqualTo("https://example.invalid/override/"));
            File.Delete(Path.Combine(_root, "appsettings.json"));
            using var emptyHost = Program.BuildHost(_root);
            Assert.That(emptyHost.Services.GetRequiredService<AppConfig>().TaskUrl, Is.EqualTo("https://example.invalid/override/"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TaskUrl", previous);
        }
    }

    private int Run(params string[] args) => Program.Execute(args, () => Program.BuildHost(_root));

    [TestCase("delete")]
    [TestCase("undeploy")]
    public void DestructiveCommandsRefuseRealDirectoriesBeforeServiceControl(string command)
    {
        var path = Path.Combine(_root, "deployment", "RepoModel");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "keep.txt"), "keep");
        var refreshes = 0;
        Assert.That(Program.Execute(new[] { "-profile", "Test", command }, () => Program.BuildHost(_root),
            () => refreshes++), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(refreshes, Is.Zero);
            Assert.That(File.ReadAllText(_profilePath), Is.EqualTo(_originalProfile));
            Assert.That(File.ReadAllText(Path.Combine(path, "keep.txt")), Is.EqualTo("keep"));
        });
    }

    [TestCase("delete", "../escape")]
    [TestCase("undeploy", "..\\escape")]
    [TestCase("delete", "..")]
    [TestCase("undeploy", "C:\\escape")]
    [TestCase("undeploy", "Model.")]
    public void DestructiveCommandsRejectPersistedModelPathTraversal(string command, string name)
    {
        var profile = JsonSerializer.Deserialize<ProfileModel>(_originalProfile)!;
        profile.StandaloneModels[1].ModelName = name;
        var before = JsonSerializer.Serialize(profile);
        File.WriteAllText(_profilePath, before);
        var refreshes = 0;
        Assert.That(Program.Execute(new[] { "-profile", "Test", command }, () => Program.BuildHost(_root),
            () => refreshes++), Is.EqualTo(1));
        Assert.That(refreshes, Is.Zero);
        Assert.That(File.ReadAllText(_profilePath), Is.EqualTo(before));
    }

    [TestCase("delete")]
    [TestCase("undeploy")]
    public void AllLinksAreValidatedBeforeAnyOwnedLinkIsDeleted(string command)
    {
        var profile = JsonSerializer.Deserialize<ProfileModel>(_originalProfile)!;
        var links = new TestLinks();
        var records = new List<FODevManager.Shared.Models.DeployedModelRecord>();
        foreach (var model in profile.AllModels)
        {
            model.MetadataFolder = model.CompiledModelFolder = Path.Combine(_root, "sources", model.ModelName);
            links.Targets[Path.Combine(_root, "deployment", model.ModelName)] = model.MetadataFolder;
            records.Add(new()
            {
                ModelName = model.ModelName, SourcePath = model.MetadataFolder,
                ProfileName = "Test"
            });
        }
        records[^1].ProfileName = "Foreign";
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(profile));
        File.WriteAllText(Path.Combine(_root, "profiles", "deployed-models.json"), JsonSerializer.Serialize(records));
        var before = File.ReadAllText(_profilePath);
        var refreshes = 0;
        Assert.That(Program.Execute(new[] { "-profile", "Test", command },
            () => Program.BuildHost(_root, services => services.AddSingleton<IDirectoryLinkService>(links)),
            () => refreshes++), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(links.Deletes, Is.Zero);
            Assert.That(refreshes, Is.Zero);
            Assert.That(File.ReadAllText(_profilePath), Is.EqualTo(before));
        });
    }

    [TestCase("db-apply")]
    [TestCase("deploy")]
    [TestCase("switch")]
    [TestCase("db-set")]
    public void ServiceStateQueryFailureStopsDispatch(string command)
    {
        var profile = JsonSerializer.Deserialize<ProfileModel>(_originalProfile)!;
        profile.IsActive = command == "db-set";
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(profile));
        var before = File.ReadAllText(_profilePath);
        var args = new List<string> { "-profile", "Test", command };
        if (command == "db-set") args.Add("NewDatabase");
        var refreshes = 0;
        Assert.That(Program.Execute(args.ToArray(), () => Program.BuildHost(_root), () =>
        {
            refreshes++;
            throw new InvalidOperationException("Simulated service query failure");
        }), Is.EqualTo(1));
        Assert.That(refreshes, Is.EqualTo(1));
        Assert.That(File.ReadAllText(_profilePath), Is.EqualTo(before));
    }

    [Test]
    public void UndeployAllowsUndeployedNugetModelWithoutServiceControl()
    {
        var profile = JsonSerializer.Deserialize<ProfileModel>(_originalProfile)!;
        profile.StandaloneModels[1].ModelType = ModelType.CompiledNuget;
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(profile));
        Assert.That(Program.Execute(new[] { "-profile", "Test", "-model", "CompiledModel", "undeploy" },
            () => Program.BuildHost(_root), () => throw new AssertionException("No service control needed")), Is.Zero);
    }

    [Test]
    public void SolutionEnsureAddsFooWhenFooExtensionsAlreadyExists()
    {
        var projectPath = Path.Combine(_root, "Foo.rnrproj");
        File.WriteAllText(projectPath, "<Project />");
        var solutionPath = Path.Combine(_root, "Test.sln");
        File.WriteAllText(solutionPath,
            "Project(\"{FC65038C-1B2F-41E1-A629-BED71D161FFF}\") = \"FooExtensions\", \"FooExtensions.rnrproj\", \"{11111111-1111-1111-1111-111111111111}\"\r\nEndProject\r\n");
        File.WriteAllText(_profilePath, JsonSerializer.Serialize(new ProfileModel
        {
            ProfileName = "Test", SolutionFilePath = solutionPath,
            StandaloneModels = new() { new() { ModelName = "Foo", ProjectFilePath = projectPath } }
        }));
        Assert.That(Run("-profile", "Test", "solution-ensure"), Is.Zero);
        var solution = File.ReadAllText(solutionPath);
        Assert.That(solution, Does.Contain("\"Foo.rnrproj\""));
        Assert.That(Run("-profile", "Test", "solution-ensure"), Is.Zero);
        Assert.That(File.ReadAllText(solutionPath), Is.EqualTo(solution));
    }

    private sealed class TestLinks : IDirectoryLinkService
    {
        public Dictionary<string, string> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Deletes { get; private set; }
        public bool Exists(string path) => Targets.ContainsKey(path);
        public string? ResolveLinkTarget(string path) => Targets.GetValueOrDefault(path);
        public void CreateSymbolicLink(string linkPath, string targetPath) => throw new AssertionException("Unexpected link creation");
        public void Delete(string path, bool recursive = true)
        {
            Deletes++;
            throw new AssertionException("Unexpected link deletion");
        }
    }
}
