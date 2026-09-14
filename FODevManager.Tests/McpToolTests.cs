using FODevManager.Mcp;
using FODevManager.Models;
using FODevManager.Operations;
using FODevManager.Services;
using FODevManager.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using FODevManager.Shared.Models;
using System.Text.Json;

namespace FODevManager.Tests;

[TestFixture]
public class McpToolTests
{
    private string root = null!;
    private AppConfig config = null!;
    private WorkflowContext context = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "fodev-mcp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        config = new AppConfig
        {
            ProfileStoragePath = Path.Combine(root, "profiles"), DefaultSourceDirectory = Path.Combine(root, "source"),
            DeploymentBasePath = Path.Combine(root, "deployment"), DeployablePackages = Path.Combine(root, "packages")
        };
        context = new WorkflowContext(config);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public void ProfileCrudReadsFreshSavedStateAndRejectsInvalidTargets()
    {
        var profiles = new ProfileOperations(context);
        profiles.Create("isolated");
        Assert.That(profiles.Show("isolated").ProfileName, Is.EqualTo("isolated"));
        var saved = context.Files.LoadProfile("isolated");
        saved.DatabaseName = "ExternalChange";
        context.Files.SaveProfile(saved);
        Assert.That(profiles.Show("isolated").DatabaseName, Is.EqualTo("ExternalChange"));
        Assert.Throws<ArgumentException>(() => profiles.Show("../escape"));
        Assert.Throws<InvalidOperationException>(() => profiles.Create("isolated"));
        profiles.Delete("isolated", CancellationToken.None);
        Assert.That(profiles.List(), Is.Empty);
    }

    [Test]
    public void SavedQueriesDoNotCreateDeploymentOrSourceDirectories()
    {
        context.Files.SaveProfile(new ProfileModel { ProfileName = "saved" }, skipExistCheck: true);
        new ProfileOperations(context).Show("saved");
        new ModelOperations(context).List("saved");
        Assert.That(Directory.Exists(config.DeploymentBasePath), Is.False);
        Assert.That(Directory.Exists(config.DefaultSourceDirectory), Is.False);
    }

    [Test]
    public void PackageRemovalExpandsRepositoryMembersOnly()
    {
        var first = new ProfileEnvironmentModel { ModelName = "A", ModelType = ModelType.CompiledNuget, PackageId = "Pkg" };
        var second = new ProfileEnvironmentModel { ModelName = "B", ModelType = ModelType.CompiledNuget, PackageId = "pkg" };
        var profile = new ProfileModel { ProfileName = "p", Repositories = [new RepositoryModel { RepoId = "r", Models = [first, second] }],
            StandaloneModels = [new ProfileEnvironmentModel { ModelName = "Other", ModelType = ModelType.Compiled, PackageId = "Pkg" }] };
        Assert.That(ModelOperations.RemovalSet(profile, first).Select(m => m.ModelName), Is.EquivalentTo(new[] { "A", "B" }));
    }

    [Test]
    public async Task ErrorsAreStructuredRedactedMcpFailures()
    {
        config.AzureArtifactsPat = "private-credential";
        var runner = new OperationRunner(config, new ApplicationOperationLock(Path.Combine(root, "operation.lock")));
        var result = await runner.RunAsync("failure", true, _ => throw new InvalidOperationException("private-credential"), requestId: "reconnect");
        var adapted = ToolExecution.ToToolResult(result);
        Assert.That(adapted.IsError, Is.True);
        Assert.That(adapted.Content.OfType<TextContentBlock>().Single().Text, Does.Not.Contain("private-credential"));
        Assert.That(result.RequestId, Is.EqualTo("reconnect"));
    }

    [TestCase(false, "Authentication failed private-credential")]
    [TestCase(false, "No packages found.\nNetwork failure private-credential")]
    [TestCase(true, "No packages found.")]
    [TestCase(true, "Pkg 1.0.0\nPkg 2.0.0")]
    public async Task PackageVersionLookupDistinguishesFailureFromEmptySuccess(bool succeeded, string output)
    {
        config.AzureArtifactsPat = "private-credential";
        config.NuGetExecutablePath = Environment.ProcessPath!;
        var repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(repo);
        File.WriteAllText(Path.Combine(repo, "nuget.config"), "<configuration><packageSources /></configuration>");
        var calls = 0;
        var packages = new DeployablePackageService(config, versionQueryRunner: start =>
        {
            calls++;
            Assert.That(start.Arguments, Does.Contain("-NonInteractive"));
            return (succeeded, output);
        });
        var controlled = new WorkflowContext(config) { Packages = packages };
        controlled.Files.SaveProfile(new ProfileModel { ProfileName = "p", Repositories = [new() { RepoId = "r", RepoRootFolder = repo }] }, true);
        var runner = new OperationRunner(config, new ApplicationOperationLock(Path.Combine(root, "operation.lock")));
        var result = await runner.RunAsync("nuget_versions", false,
            _ => Task.FromResult<object?>(new PackageOperations(controlled).Versions("p", "r", "Pkg")));
        var adapted = ToolExecution.ToToolResult(result);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(adapted.IsError == true, Is.EqualTo(!succeeded));
        Assert.That(JsonSerializer.Serialize(adapted), Does.Not.Contain("private-credential"));
        if (succeeded) Assert.That(result.Data!.ToString(), output.Contains("Pkg") ? Does.Contain("2.0.0") : Is.EqualTo("[]"));
    }

    [Test]
    public void LiveInspectHandlesMissingLinksAndDeletionRefusesPhysicalDirectories()
    {
        var model = new ProfileEnvironmentModel { ModelName = "A", ModelType = ModelType.Compiled, CompiledModelFolder = Path.Combine(root, "payload") };
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", StandaloneModels = [model] }, skipExistCheck: true);
        Assert.DoesNotThrow(() => new DeploymentOperations(context).Inspect("p"));
        var path = Path.Combine(config.DeploymentBasePath, "A");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "keep.txt"), "payload");
        Assert.Throws<InvalidOperationException>(() => new ProfileOperations(context).Delete("p", CancellationToken.None));
        Assert.That(File.Exists(Path.Combine(path, "keep.txt")), Is.True);
        Assert.That(context.Files.ExistProfile("p"), Is.True);
    }

    [Test]
    public void OwnershipRequiresMatchingLinkAndSingleManagedLedgerRecord()
    {
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(config.DeploymentBasePath);
        var path = Path.Combine(config.DeploymentBasePath, "A");
        Directory.CreateSymbolicLink(path, payload);
        try
        {
            var model = new ProfileEnvironmentModel { ModelName = "A", ModelType = ModelType.Compiled, CompiledModelFolder = payload };
            var profile = new ProfileModel { ProfileName = "p", StandaloneModels = [model] };
            Assert.Throws<InvalidOperationException>(() => new DeploymentOperations(context).Verify(profile, model));
            context.Ledger.RecordDeployment(new DeployedModelRecord { ProfileName = "p", ModelName = "A", SourcePath = payload });
            Assert.That(new DeploymentOperations(context).Verify(profile, model), Is.True);
            profile.ProfileName = "foreign";
            Assert.Throws<InvalidOperationException>(() => new DeploymentOperations(context).Verify(profile, model));
        }
        finally { Directory.Delete(path); }
    }

    [Test]
    public async Task SettingsReloadRefreshesServicePathsAndRedaction()
    {
        File.WriteAllText(Path.Combine(root, "appsettings.json"), JsonSerializer.Serialize(config));
        var settings = new SettingsOperations(config, root, "McpTests");
        var runner = new OperationRunner(config, new ApplicationOperationLock(Path.Combine(root, "operation.lock")));
        var newStore = Path.Combine(root, "new-profiles");
        settings.Update(new SettingsUpdate { ProfileStoragePath = newStore, AzureArtifactsPat = "new-private-secret" });
        new WorkflowContext(config).Files.SaveProfile(new ProfileModel { ProfileName = "fresh" }, skipExistCheck: true);
        Assert.That(File.Exists(Path.Combine(newStore, "fresh.json")), Is.True);
        Assert.That(JsonSerializer.Serialize(settings.Read()), Does.Not.Contain("new-private-secret"));
        var result = await runner.RunAsync("redact", false, _ => Task.FromResult<object?>(new { Value = "new-private-secret" }));
        Assert.That(result.Data!.ToString(), Does.Not.Contain("new-private-secret"));
    }

    [Test]
    public async Task StatusByRequestIdBypassesRunningSynchronousTool()
    {
        var runner = new OperationRunner(config, new ApplicationOperationLock(Path.Combine(root, "operation.lock")));
        var execution = new ToolExecution(runner, config);
        var tools = new ApplicationTools(execution, runner, new SettingsOperations(config, root, "McpTests"), config);
        using var entered = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var work = execution.Run("blocking", false, (_, _) => { entered.Set(); finish.Wait(); return new { Finished = true }; },
            new InlineProgress(_ => { }), CancellationToken.None, "reconnect-id");
        try
        {
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            var status = tools.Status(requestId: "reconnect-id");
            Assert.That(status.IsError, Is.False);
            Assert.That(status.StructuredContent.ToString(), Does.Contain("running"));
        }
        finally { finish.Set(); await work; }
    }

    [Test]
    public async Task RegisteredSdkToolsCoverInventoryAndReturnStructuredErrorsAndProgress()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var stderr = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet", Arguments = [typeof(McpHost).Assembly.Location, "mcp"],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["ProfileStoragePath"] = config.ProfileStoragePath, ["DeploymentBasePath"] = config.DeploymentBasePath,
                ["DefaultSourceDirectory"] = config.DefaultSourceDirectory, ["DeployablePackages"] = config.DeployablePackages,
                ["AzureArtifactsPat"] = "protocol-private-secret"
            },
            StandardErrorLines = line => stderr.Enqueue(line)
        }), cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var expected = new[]
        {
            "profiles_list", "profile_show", "profile_active", "profile_create", "profile_delete", "profile_import_file", "profile_import_repository",
            "profile_export", "profile_refresh", "profile_switch", "profile_sync_check", "profile_sync_dismiss", "profile_sync_reimport",
            "models_list", "model_show", "models_add_path", "model_create", "model_remove", "model_properties_update", "model_version", "model_version_update",
            "repositories_list", "repository_show", "repository_properties_update", "repository_status", "git_fetch", "repository_remote", "git_assign_task",
            "repository_task_url", "git_merge_main", "git_reset_profile", "git_tag_release", "nuget_versions", "nuget_add", "nuget_update", "nuget_remove",
            "nuget_prepare", "package_build", "deployment_inspect", "deployment_deploy", "deployment_undeploy", "deployment_undeploy_all", "solution_path",
            "solution_ensure", "solution_open", "database_get", "database_set", "database_apply", "application_about", "settings_read", "settings_update", "operation_status"
        };
        Assert.That(tools.Select(t => t.Name), Is.EquivalentTo(expected));
        foreach (var tool in tools)
        {
            Assert.That(tool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("progress", out _), Is.False, tool.Name);
            Assert.That(tool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("token", out _), Is.False, tool.Name);
        }
        var progress = new System.Collections.Concurrent.ConcurrentQueue<ProgressNotificationValue>();
        var created = await tools.Single(t => t.Name == "profile_create")
            .CallAsync(new Dictionary<string, object?> { ["profileName"] = "protocol", ["requestId"] = "creation" },
                progress: new InlineProgress(progress.Enqueue), cancellationToken: timeout.Token);
        Assert.That(created.IsError, Is.False, created.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
        Assert.That(created.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("succeeded"));
        Assert.That(progress, Is.Not.Empty);
        var failed = await client.CallToolAsync("profile_show", new Dictionary<string, object?> { ["profileName"] = "protocol-private-secret" }, cancellationToken: timeout.Token);
        Assert.That(failed.IsError, Is.True);
        Assert.That(failed.StructuredContent.ToString(), Does.Not.Contain("protocol-private-secret"));
        var status = await client.CallToolAsync("operation_status", new Dictionary<string, object?> { ["requestId"] = "creation" }, cancellationToken: timeout.Token);
        Assert.That(status.IsError, Is.False);
        Assert.That(string.Join('\n', stderr), Does.Not.Contain("protocol-private-secret"));
    }

    private sealed class InlineProgress(Action<ProgressNotificationValue> report) : IProgress<ProgressNotificationValue>
    {
        public void Report(ProgressNotificationValue value) => report(value);
    }

    [Test]
    public void RealPackageRemovalUpdatesReferenceAndAllMembersWithoutRemovingOtherModelKinds()
    {
        var build = Path.Combine(root, "repo", "Build");
        Directory.CreateDirectory(build);
        var isvPath = Path.Combine(build, "isv.config");
        File.WriteAllText(isvPath, "<packages><package id=\"Pkg\" version=\"1.0.0\" /></packages>");
        File.WriteAllText(Path.Combine(build, "nuget.config"), "<configuration><packageSources /></configuration>");
        var first = new ProfileEnvironmentModel { ModelName = "A", ModelType = ModelType.CompiledNuget, PackageId = "Pkg", PackageVersion = "1.0.0" };
        var second = new ProfileEnvironmentModel { ModelName = "B", ModelType = ModelType.CompiledNuget, PackageId = "Pkg", PackageVersion = "1.0.0" };
        context.Files.SaveProfile(new ProfileModel
        {
            ProfileName = "p", Repositories = [new RepositoryModel { RepoId = "r", RepoRootFolder = Path.GetDirectoryName(build)!,
                Models = [first, second, new ProfileEnvironmentModel { ModelName = "Source", ModelType = ModelType.Source }] }],
            StandaloneModels = [new ProfileEnvironmentModel { ModelName = "Compiled", ModelType = ModelType.Compiled }]
        }, skipExistCheck: true);
        new PackageOperations(context).Remove("p", "r", "Pkg", CancellationToken.None);
        Assert.That(context.Profile("p").AllModels.Select(m => m.ModelName), Is.EquivalentTo(new[] { "Source", "Compiled" }));
        Assert.That(File.ReadAllText(isvPath), Does.Not.Contain("Pkg"));
    }

    [Test]
    public void PackageRemovalValidatesEveryMemberBeforeReferenceOrFirstLinkChanges()
    {
        var build = Path.Combine(root, "repo", "Build");
        Directory.CreateDirectory(build);
        var isvPath = Path.Combine(build, "isv.config");
        const string original = "<packages><package id=\"Pkg\" version=\"1.0.0\" /></packages>";
        File.WriteAllText(isvPath, original);
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(config.DeploymentBasePath);
        var link = Path.Combine(config.DeploymentBasePath, "A");
        Directory.CreateSymbolicLink(link, payload);
        Directory.CreateDirectory(Path.Combine(config.DeploymentBasePath, "B"));
        try
        {
            context.Files.SaveProfile(new ProfileModel { ProfileName = "p", Repositories = [new RepositoryModel
            {
                RepoId = "r", RepoRootFolder = Path.GetDirectoryName(build)!, Models =
                [new ProfileEnvironmentModel { ModelName = "A", ModelType = ModelType.CompiledNuget, PackageId = "Pkg", CompiledModelFolder = payload },
                 new ProfileEnvironmentModel { ModelName = "B", ModelType = ModelType.CompiledNuget, PackageId = "Pkg", CompiledModelFolder = payload }]
            }] }, skipExistCheck: true);
            context.Ledger.RecordDeployment(new DeployedModelRecord { ProfileName = "p", ModelName = "A", SourcePath = payload });
            Assert.Throws<InvalidOperationException>(() => new ModelOperations(context).Remove("p", "A", CancellationToken.None));
            Assert.That(new DirectoryInfo(link).LinkTarget, Is.Not.Null);
            Assert.That(File.ReadAllText(isvPath), Is.EqualTo(original));
            Assert.That(context.Profile("p").AllModels, Has.Count.EqualTo(2));
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    public void LinkedReimportPreservesDefaultDatabaseAndLocalName()
    {
        var linked = Path.Combine(root, "linked.json");
        File.WriteAllText(linked, JsonSerializer.Serialize(new ProfileModel { ProfileName = "upstream", DatabaseName = "RemoteDb" }));
        context.Files.SaveProfile(new ProfileModel { ProfileName = "local", DatabaseName = "AXDB", ProfileFilePath = linked }, skipExistCheck: true);
        var result = new ProfileOperations(context).Reimport("local", CancellationToken.None);
        Assert.That(result.DatabaseName, Is.EqualTo("AXDB"));
        Assert.That(result.ProfileName, Is.EqualTo("local"));
        Assert.That(context.Files.ExistProfile("upstream"), Is.False);
    }

    [Test]
    public void UndeployMissingLinkRepairsSavedFlagAndOwnedLedgerWithoutServiceControl()
    {
        var model = new ProfileEnvironmentModel { ModelName = "A", ModelType = ModelType.Compiled, IsDeployed = true };
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", StandaloneModels = [model] }, skipExistCheck: true);
        context.Ledger.RecordDeployment(new DeployedModelRecord { ProfileName = "p", ModelName = "A", SourcePath = Path.Combine(root, "old") });
        new DeploymentOperations(context).Undeploy("p", "A", CancellationToken.None);
        Assert.That(context.Profile("p").AllModels.Single().IsDeployed, Is.False);
        Assert.That(context.Ledger.LoadRecords(), Is.Empty);
        Assert.That(Directory.Exists(config.DeploymentBasePath), Is.False);
    }

    [Test]
    public void CreateRefusesExistingDestinationOwnedByAnotherProfile()
    {
        var destination = Path.Combine(config.DefaultSourceDirectory, "Foo");
        Directory.CreateDirectory(Path.Combine(destination, "Project", "Foo"));
        Directory.CreateDirectory(Path.Combine(destination, "Metadata", "Foo", "Descriptor"));
        var project = Path.Combine(destination, "Project", "Foo", "Foo.rnrproj");
        var descriptor = Path.Combine(destination, "Metadata", "Foo", "Descriptor", "Foo.xml");
        File.WriteAllText(project, "original project");
        File.WriteAllText(descriptor, "original descriptor");
        context.Files.SaveProfile(new ProfileModel { ProfileName = "owner", StandaloneModels =
            [new ProfileEnvironmentModel { ModelName = "Foo", ModelRootFolder = destination }] }, skipExistCheck: true);
        context.Files.SaveProfile(new ProfileModel { ProfileName = "other" }, skipExistCheck: true);
        Assert.Throws<InvalidOperationException>(() => new ModelOperations(context).Create("other", "Foo"));
        Assert.That(File.ReadAllText(project), Is.EqualTo("original project"));
        Assert.That(File.ReadAllText(descriptor), Is.EqualTo("original descriptor"));
        Assert.That(Directory.Exists(Path.Combine(destination, "Metadata", "Foo", "XppMetadata")), Is.False);
        Assert.That(context.Profile("other").AllModels, Is.Empty);
    }

    [TestCase("mismatch")]
    [TestCase("existing-output")]
    [TestCase("foreign-link")]
    [TestCase("unmanaged-link")]
    public void InstalledAddRejectsUnsafeConversionBeforeAnyCopyOrDeletion(string scenario)
    {
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p" }, skipExistCheck: true);
        var installed = Path.Combine(config.DeploymentBasePath, "A");
        var other = Path.Combine(config.DeploymentBasePath, "B");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "sentinel"), "B original");
        var payload = Path.Combine(root, "installed-payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "sentinel"), "A original");
        var linked = scenario.EndsWith("link", StringComparison.Ordinal);
        if (linked) Directory.CreateSymbolicLink(installed, payload);
        else { Directory.CreateDirectory(installed); File.WriteAllText(Path.Combine(installed, "sentinel"), "A original"); }
        if (scenario == "foreign-link") context.Ledger.RecordDeployment(new DeployedModelRecord
            { ProfileName = "foreign", ModelName = "A", SourcePath = payload });
        if (scenario == "existing-output")
        {
            Directory.CreateDirectory(Path.Combine(config.DefaultSourceDirectory, "A"));
            File.WriteAllText(Path.Combine(config.DefaultSourceDirectory, "A", "keep"), "existing output");
        }
        // Isolate the old conversion implementation while reproducing its destructive path
        var state = Singleton<FODevManager.Shared.Utils.FODevManager.WinUI.Services.W3cServiceState>.Instance;
        var previous = state.InOperation;
        state.InOperation = true;
        try
        {
            Assert.Throws<InvalidOperationException>(() => new ModelOperations(context).Add("p", installed, scenario == "mismatch" ? "B" : "A"));
            Assert.That(Directory.Exists(installed), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(other, "sentinel")), Is.EqualTo("B original"));
            Assert.That(File.ReadAllText(Path.Combine(payload, "sentinel")), Is.EqualTo("A original"));
            Assert.That(context.Profile("p").AllModels, Is.Empty);
        }
        finally
        {
            state.InOperation = previous;
            if (linked && new DirectoryInfo(installed).LinkTarget != null) Directory.Delete(installed);
        }
    }

    [Test]
    public void DeploymentPrefixSiblingIsAddedAsExistingCompiledModelWithoutConversion()
    {
        var repo = config.DeploymentBasePath + "-sibling";
        Directory.CreateDirectory(Path.Combine(repo, "Project"));
        var compiled = Path.Combine(repo, "Libs", "A");
        Directory.CreateDirectory(compiled);
        File.WriteAllText(Path.Combine(compiled, "A.xref"), "compiled");
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p" }, skipExistCheck: true);
        new ModelOperations(context).Add("p", compiled, "A");
        Assert.That(context.Profile("p").FindModel("A")?.ModelType, Is.EqualTo(ModelType.Compiled));
        Assert.That(File.Exists(Path.Combine(compiled, "A.xref")), Is.True);
        Assert.That(Directory.Exists(Path.Combine(config.DefaultSourceDirectory, "A")), Is.False);
    }

    [Test]
    public void DiscoveryValidatesEntireCollectionBeforeAddingFirstModel()
    {
        var repo = Path.Combine(root, "collection");
        Directory.CreateDirectory(Path.Combine(repo, "Project"));
        var compiled = Path.Combine(repo, "Libs", "Good");
        Directory.CreateDirectory(compiled);
        File.WriteAllText(Path.Combine(compiled, "Good.xref"), "compiled");
        Directory.CreateDirectory(Path.Combine(repo, "Metadata", "MissingProject"));
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p" }, skipExistCheck: true);
        Assert.That(() => new ModelOperations(context).Add("p", repo, "ignored"), Throws.Exception);
        Assert.That(context.Profile("p").AllModels, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RefreshPreservesDeployedPackageMembershipWhenReferencesChange(bool removed)
    {
        var repo = CreateCachedPackageRepository("2.0.0");
        if (removed) File.WriteAllText(Path.Combine(repo, "Build", "isv.config"), "<packages />");
        var oldPayload = Path.Combine(config.DeployablePackages, "Pkg-1.0.0");
        Directory.CreateDirectory(oldPayload);
        var model = new ProfileEnvironmentModel { ModelName = "A", ModelType = ModelType.CompiledNuget,
            PackageId = "Pkg", PackageVersion = "1.0.0", CompiledModelFolder = oldPayload, ModelRootFolder = repo, IsDeployed = true };
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", Repositories =
            [new RepositoryModel { RepoId = "r", RepoRootFolder = repo, GitUrl = "https://example.invalid/repo", Models = [model] }] }, skipExistCheck: true);
        Directory.CreateDirectory(config.DeploymentBasePath);
        var link = Path.Combine(config.DeploymentBasePath, "A");
        Directory.CreateSymbolicLink(link, oldPayload);
        context.Ledger.RecordDeployment(new DeployedModelRecord { ProfileName = "p", ModelName = "A", SourcePath = oldPayload });
        try
        {
            var refreshed = new ProfileOperations(context).Refresh("p");
            Assert.That(refreshed.FindModel("A")!.PackageVersion, Is.EqualTo("1.0.0"));
            Assert.That(refreshed.FindModel("A")!.CompiledModelFolder, Is.EqualTo(oldPayload));
            Assert.That(new DeploymentOperations(context).Verify(refreshed, refreshed.FindModel("A")!), Is.True);
        }
        finally { Directory.Delete(link); }
    }

    [Test]
    public void ImportNeverDeletesForeignLegacyVersionedPackageLink()
    {
        var repo = CreateCachedPackageRepository("1.0.0");
        var foreign = Path.Combine(root, "foreign-payload");
        Directory.CreateDirectory(foreign);
        Directory.CreateDirectory(config.DeploymentBasePath);
        var link = Path.Combine(config.DeploymentBasePath, "Pkg-1.0.0");
        Directory.CreateSymbolicLink(link, foreign);
        context.Ledger.RecordDeployment(new DeployedModelRecord { ProfileName = "foreign", ModelName = "Pkg-1.0.0", SourcePath = foreign });
        var definition = new ProfileModel { ProfileName = "imported", Repositories = [new RepositoryModel
        {
            RepoId = "r", RepoRootFolder = repo, GitUrl = "https://example.invalid/repo", Models =
            [new ProfileEnvironmentModel { ModelName = "Pkg-1.0.0", ModelType = ModelType.CompiledNuget, PackageId = "Pkg", PackageVersion = "1.0.0" }]
        }] };
        var path = Path.Combine(root, "incoming.json");
        File.WriteAllText(path, JsonSerializer.Serialize(definition));
        try
        {
            new ProfileOperations(context).ImportFile(path, null);
            Assert.That(new DirectoryInfo(link).LinkTarget, Is.Not.Null);
            Assert.That(context.Ledger.LoadRecords().Single().ProfileName, Is.EqualTo("foreign"));
        }
        finally { if (new DirectoryInfo(link).LinkTarget != null) Directory.Delete(link); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PreparationNeverOverwritesIncomingLinkedDefinition(bool hasPackages)
    {
        var repo = hasPackages ? CreateCachedPackageRepository("1.0.0") : Path.Combine(root, "source-only");
        Directory.CreateDirectory(repo);
        var linked = Path.Combine(root, "incoming.json");
        const string incoming = "{\"ProfileName\":\"pending-incoming-profile\"}";
        File.WriteAllText(linked, incoming);
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", ProfileFilePath = linked,
            Repositories = [new RepositoryModel { RepoId = "r", RepoRootFolder = repo }] }, skipExistCheck: true);
        var savedPath = Path.Combine(config.ProfileStoragePath, "p.json");
        var originalTime = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(savedPath, originalTime);
        new PackageOperations(context).Prepare("p", CancellationToken.None);
        Assert.That(File.ReadAllText(linked), Is.EqualTo(incoming));
        if (!hasPackages) Assert.That(File.GetLastWriteTimeUtc(savedPath), Is.EqualTo(originalTime));
        else Assert.That(context.Profile("p").FindModel("A"), Is.Not.Null);
    }

    private string CreateCachedPackageRepository(string version)
    {
        var repo = Path.Combine(root, "package-repo");
        var build = Path.Combine(repo, "Build");
        Directory.CreateDirectory(build);
        Directory.CreateDirectory(Path.Combine(repo, "Project"));
        File.WriteAllText(Path.Combine(build, "isv.config"), $"<packages><package id=\"Pkg\" version=\"{version}\" /></packages>");
        File.WriteAllText(Path.Combine(build, "nuget.config"), "<configuration><packageSources /></configuration>");
        var payload = Path.Combine(config.DeployablePackages, "Pkg-" + version);
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "A.xref"), "cached");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git")
        { ArgumentList = { "init", repo }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true })!;
        process.WaitForExit();
        Assert.That(process.ExitCode, Is.Zero);
        File.AppendAllText(Path.Combine(repo, ".git", "config"), "\n[remote \"origin\"]\n\turl = " + repo.Replace('\\', '/') + "\n");
        return repo;
    }

    [Test]
    public void ExistingSourceAddPreservesOriginalProjectAndDescriptor()
    {
        var destination = Path.Combine(config.DefaultSourceDirectory, "Foo");
        var projectFolder = Path.Combine(destination, "Project", "Foo");
        var descriptorFolder = Path.Combine(destination, "Metadata", "Foo", "Descriptor");
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(descriptorFolder);
        var project = Path.Combine(projectFolder, "Foo.rnrproj");
        var descriptor = Path.Combine(descriptorFolder, "Foo.xml");
        File.WriteAllText(project, "<Project />");
        File.WriteAllText(descriptor, "original descriptor");
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p" }, skipExistCheck: true);
        new ModelOperations(context).Add("p", destination, "Foo");
        Assert.That(context.Profile("p").FindModel("Foo")?.ModelType, Is.EqualTo(ModelType.Source));
        Assert.That(File.ReadAllText(project), Is.EqualTo("<Project />"));
        Assert.That(File.ReadAllText(descriptor), Is.EqualTo("original descriptor"));
    }

    [Test]
    public void AddingOwnManagedInstalledLinkIsANonDestructiveNoOp()
    {
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        Directory.CreateDirectory(config.DeploymentBasePath);
        var path = Path.Combine(config.DeploymentBasePath, "A");
        Directory.CreateSymbolicLink(path, payload);
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", StandaloneModels =
            [new ProfileEnvironmentModel { ModelName = "A", ModelType = ModelType.Compiled, CompiledModelFolder = payload }] }, skipExistCheck: true);
        context.Ledger.RecordDeployment(new DeployedModelRecord { ProfileName = "p", ModelName = "A", SourcePath = payload });
        try
        {
            new ModelOperations(context).Add("p", path + Path.DirectorySeparatorChar, "A");
            Assert.That(new DirectoryInfo(path).LinkTarget, Is.Not.Null);
            Assert.That(context.Profile("p").AllModels, Has.Count.EqualTo(1));
            Assert.That(Directory.Exists(config.DefaultSourceDirectory), Is.False);
        }
        finally { Directory.Delete(path); }
    }

    [Test]
    public void ConversionPreflightRejectsLinkedDescendantsAndCopyVerificationDetectsChangedBytes()
    {
        var source = Path.Combine(root, "physical");
        Directory.CreateDirectory(Path.Combine(source, "Descriptor"));
        File.WriteAllText(Path.Combine(source, "Descriptor", "A.xml"), "<AxModelInfo><Name>A</Name></AxModelInfo>");
        File.WriteAllText(Path.Combine(source, "extensionless"), "original bytes");
        var destination = Path.Combine(root, "new-output");
        var snapshot = ModelPathSafety.ValidateConversion(source, destination, "A");
        Directory.CreateDirectory(destination);
        FileHelper.CopyDirectory(source, destination);
        Assert.DoesNotThrow(() => ModelPathSafety.VerifyCopy(source, destination, snapshot));
        File.WriteAllText(Path.Combine(destination, "extensionless"), "changed bytes");
        Assert.Throws<InvalidOperationException>(() => ModelPathSafety.VerifyCopy(source, destination, snapshot));
        var childLink = Path.Combine(source, "linked-child");
        Directory.CreateSymbolicLink(childLink, destination);
        try
        {
            Assert.Throws<InvalidOperationException>(() => ModelPathSafety.ValidateConversion(source, Path.Combine(root, "another-output"), "A"));
            Assert.That(File.ReadAllText(Path.Combine(source, "extensionless")), Is.EqualTo("original bytes"));
        }
        finally { Directory.Delete(childLink); }
    }
}
