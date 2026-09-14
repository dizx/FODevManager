using System.Diagnostics;
using System.Text.Json;
using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Operations;
using FODevManager.Services;
using FODevManager.Shared.Models;
using FODevManager.Shared.Utils.FODevManager.WinUI.Services;
using FODevManager.Utils;

namespace FODevManager.Tests;

[TestFixture, NonParallelizable]
public class HostCoordinationReviewTests
{
    private string root = null!;
    private AppConfig config = null!;
    private WorkflowContext context = null!;
    private CountingLinks links = null!;
    private int refreshes;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "fodev-task3-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        config = new AppConfig { ProfileStoragePath = Path.Combine(root, "profiles"), DefaultSourceDirectory = Path.Combine(root, "source"),
            DeploymentBasePath = Path.Combine(root, "deployment"), DeployablePackages = Path.Combine(root, "packages") };
        links = new();
        refreshes = 0;
        context = new(config) { Links = links, RefreshServiceState = () => { refreshes++; throw new AssertionException("Unexpected service control"); } };
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(config.DeploymentBasePath))
            foreach (var path in Directory.GetDirectories(config.DeploymentBasePath))
                if (new DirectoryInfo(path).LinkTarget != null) Directory.Delete(path);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }

    [Test]
    public void ReimportUsesActualOuterServiceStateContractWithoutRefreshingOrRealServiceControl()
    {
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        var incoming = Path.Combine(root, "incoming.json");
        File.WriteAllText(incoming, JsonSerializer.Serialize(new ProfileModel { ProfileName = "incoming" }));
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", ProfileFilePath = incoming, DatabaseName = "KeepDB",
            StandaloneModels = [new() { ModelName = "A", ModelType = ModelType.Compiled, CompiledModelFolder = payload, IsDeployed = true }] }, true);
        InstallLink("A", payload);
        var state = Singleton<W3cServiceState>.Instance;
        var previous = state.InOperation;
        var wasRunning = state.IsRunning;
        state.InOperation = true;
        state.IsRunning = false;
        try
        {
            Assert.Throws<InvalidOperationException>(() => ServiceHelper.RefreshW3SVCState(() => false));
            var desktop = new WorkflowContext(config) { Links = links, OuterOwnsServiceControl = true,
                RefreshServiceState = () => throw new AssertionException("Nested refresh") };
            var imported = new HostOperationBoundary(new(Path.Combine(root, "operation.lock"))).Run("reimport", () =>
                new ProfileOperations(desktop).Reimport("p", CancellationToken.None));
            Assert.That(imported.DatabaseName, Is.EqualTo("KeepDB"));
            Assert.That(imported.AllModels, Is.Empty);
            Assert.That(links.Deletes, Is.EqualTo(1));
            Assert.That(desktop.Ledger.LoadRecords(), Is.Empty);
            Assert.That(state.InOperation, Is.True);
        }
        finally { state.InOperation = previous; state.IsRunning = wasRunning; }
    }

    [Test]
    public void UnchangedAutomaticPreparationKeepsOwnedLinkLedgerAndProfileUntouched()
    {
        var repo = CreateCachedRepository();
        var profilePath = Path.Combine(config.ProfileStoragePath, "p.json");
        var saved = File.ReadAllText(profilePath);
        var ledger = JsonSerializer.Serialize(context.Ledger.LoadRecords());
        var timestamp = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(profilePath, timestamp);
        new PackageOperations(context).Prepare("p", CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(refreshes, Is.Zero);
            Assert.That(links.Deletes, Is.Zero);
            Assert.That(links.Creates, Is.Zero);
            Assert.That(File.ReadAllText(profilePath), Is.EqualTo(saved));
            Assert.That(File.GetLastWriteTimeUtc(profilePath), Is.EqualTo(timestamp));
            Assert.That(JsonSerializer.Serialize(context.Ledger.LoadRecords()), Is.EqualTo(ledger));
            Assert.That(new DeploymentOperations(context).Verify(context.Profile("p"), context.Profile("p").FindModel("A")!), Is.True);
        });
    }

    [Test]
    public void FailedAutomaticResolutionDoesNotUndeployHealthyPreviousPackage()
    {
        var repo = CreateCachedRepository();
        File.WriteAllText(Path.Combine(repo, "Build", "isv.config"), "<packages><package id=\"Pkg\" version=\"2.0.0\" /></packages>");
        // Invalid feed configuration fails before invoking an external package executable
        File.WriteAllText(Path.Combine(repo, "Build", "nuget.config"),
            "<configuration><packageSources><add key=\"private\" value=\"https://pkgs.dev.azure.com/test/feed/index.json\" /></packageSources></configuration>");
        var saved = File.ReadAllText(Path.Combine(config.ProfileStoragePath, "p.json"));
        Assert.Throws<InvalidOperationException>(() => new PackageOperations(context).Prepare("p", CancellationToken.None));
        Assert.That(refreshes, Is.Zero);
        Assert.That(links.Deletes, Is.Zero);
        Assert.That(File.ReadAllText(Path.Combine(config.ProfileStoragePath, "p.json")), Is.EqualTo(saved));
        Assert.That(new DirectoryInfo(Path.Combine(config.DeploymentBasePath, "A")).LinkTarget, Is.Not.Null);
    }

    [Test]
    public void ResolvedPackageChangeReplacesOnlyChangedDeployment()
    {
        var repo = CreateCachedRepository();
        var stablePayload = Path.Combine(root, "stable");
        Directory.CreateDirectory(stablePayload);
        var profile = context.Profile("p");
        profile.StandaloneModels.Add(new() { ModelName = "Stable", ModelType = ModelType.Compiled,
            CompiledModelFolder = stablePayload, IsDeployed = true });
        context.Files.SaveProfile(profile);
        InstallLink("Stable", stablePayload);
        var nextPayload = Path.Combine(config.DeployablePackages, "Pkg-2.0.0");
        Directory.CreateDirectory(nextPayload);
        File.WriteAllText(Path.Combine(nextPayload, "A.xref"), "new cached version");
        File.WriteAllText(Path.Combine(repo, "Build", "isv.config"), "<packages><package id=\"Pkg\" version=\"2.0.0\" /></packages>");
        var state = Singleton<W3cServiceState>.Instance;
        var previous = state.InOperation;
        state.InOperation = true;
        try
        {
            var controlled = new WorkflowContext(config) { Links = links, OuterOwnsServiceControl = true };
            new PackageOperations(controlled).Prepare("p", CancellationToken.None);
            Assert.That(links.Deletes, Is.EqualTo(1));
            Assert.That(links.Creates, Is.EqualTo(1));
            Assert.That(context.Profile("p").FindModel("A")!.CompiledModelFolder, Is.EqualTo(nextPayload));
            Assert.That(context.Profile("p").FindModel("A")!.IsDeployed, Is.True);
            Assert.That(new DirectoryInfo(Path.Combine(config.DeploymentBasePath, "Stable")).LinkTarget, Is.EqualTo(stablePayload));
            Assert.That(controlled.Ledger.LoadRecords().Single(r => r.ModelName == "Stable").SourcePath, Is.EqualTo(stablePayload));
        }
        finally { state.InOperation = previous; }
    }

    [Test]
    public void ProfileCreateModelReturnsUnderlyingFailure()
    {
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p" }, true);
        Assert.That(context.Profiles.CreateModel("p", "invalid?name"), Is.False);
        Assert.That(context.Profile("p").AllModels, Is.Empty);
    }

    [TestCase("success")]
    [TestCase("false")]
    [TestCase("logged-error")]
    [TestCase("exception")]
    [TestCase("cleanup-error")]
    [TestCase("cleanup-exception")]
    [TestCase("cancel")]
    public async Task DesktopOutcomeIncludesActionAndCleanup(string scenario)
    {
        var messages = new List<Message>();
        using var subscription = MessageBus.Subscribe(messages.Add);
        var cleanupFinished = false;
        var task = HostOperationExecution.RunAsync("probe", async () =>
        {
            await Task.Yield();
            if (scenario == "exception") throw new InvalidOperationException("action failed");
            if (scenario == "cancel") throw new OperationCanceledException();
            if (scenario == "logged-error") await Task.Run(() => MessageLogger.Error("legacy service failed"));
            return scenario != "false";
        }, async () =>
        {
            Assert.That(messages.Any(m => m.Content.Contains("completed")), Is.False);
            await Task.Yield();
            cleanupFinished = true;
            if (scenario == "cleanup-error") MessageLogger.Error("restart failed");
            if (scenario == "cleanup-exception") throw new InvalidOperationException("cleanup failed");
        });
        if (scenario == "success") Assert.That(await task, Is.True);
        else if (scenario == "cancel") Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        else Assert.ThrowsAsync<InvalidOperationException>(async () => await task);
        Assert.That(cleanupFinished, Is.True);
        Assert.That(messages.Count(m => m.Content.Contains("completed")), Is.EqualTo(scenario == "success" ? 1 : 0));
    }

    [Test]
    public async Task OutcomeCaptureDoesNotLeakBetweenIndependentRequests()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = HostOperationExecution.RunAsync("first", async () => { entered.SetResult(); await finish.Task; return true; }, () => Task.CompletedTask);
        await entered.Task;
        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await HostOperationExecution.RunAsync("second", () =>
            { MessageLogger.Error("second only"); return Task.FromResult(true); }, () => Task.CompletedTask));
            MessageLogger.Error("unrelated event");
        }
        finally { finish.SetResult(); }
        Assert.That(await first, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingOrStaleSelectedTargetsCannotSucceedOrExecuteOthers(bool missing)
    {
        var selected = new ProfileEnvironmentModel { ModelName = "A", MetadataFolder = "old", ModelType = ModelType.Source };
        var fresh = new ProfileModel { ProfileName = "p", StandaloneModels = missing ? [] :
            [new() { ModelName = "A", MetadataFolder = "new", ModelType = ModelType.Source }] };
        var validFirst = new ProfileEnvironmentModel { ModelName = "ValidFirst", ModelType = ModelType.Compiled };
        fresh.StandaloneModels.Insert(0, validFirst);
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => SelectedModelOperations.Run(fresh, [validFirst, selected], _ => { calls++; return true; }));
        Assert.That(calls, Is.Zero);
    }

    [Test]
    public void SelectedBatchAggregatesFalseResultsAndExceptions()
    {
        var models = new List<ProfileEnvironmentModel> { new() { ModelName = "A" }, new() { ModelName = "B" } };
        var profile = new ProfileModel { ProfileName = "p", StandaloneModels = models };
        var calls = 0;
        var error = Assert.Throws<AggregateException>(() => SelectedModelOperations.Run(profile, models, model =>
        {
            calls++;
            if (model.ModelName == "A") return false;
            throw new InvalidOperationException("B failed");
        }));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ExplicitMergeCheckFetchesNewRemoteCommitsEvenAfterRecentCheck()
    {
        var remote = Path.Combine(root, "remote.git");
        var author = Path.Combine(root, "author");
        var working = Path.Combine(root, "working");
        Git(root, "init", "--bare", remote);
        Git(root, "clone", remote, author);
        Git(author, "checkout", "-b", "main");
        File.WriteAllText(Path.Combine(author, "file.txt"), "initial");
        Commit(author);
        Git(author, "push", "origin", "main");
        Git(root, "clone", "-b", "main", remote, working);
        Git(working, "checkout", "-b", "feature");
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", Repositories =
            [new() { RepoId = "r", RepoRootFolder = working, MainBranchName = "main" }] }, true);
        var operations = new RepositoryOperations(context);
        Assert.That(await operations.CheckMainUpdates("p", "r", CancellationToken.None), Is.False);
        File.AppendAllText(Path.Combine(author, "file.txt"), "\nremote change");
        Commit(author);
        Git(author, "push", "origin", "main");
        Assert.That(await GitHelper.HasMainChangesAsync(working, "main", fetchUpdates: false), Is.False);
        var boundary = new HostOperationBoundary(new(Path.Combine(root, "operation.lock")));
        Assert.That(await boundary.RunAsync("fetch/check", () => operations.CheckMainUpdates("p", "r", CancellationToken.None)), Is.True);
        using (var dialogTimeLease = new ApplicationOperationLock(Path.Combine(root, "operation.lock")).TryAcquire())
        {
            Assert.That(dialogTimeLease, Is.Not.Null);
            Assert.ThrowsAsync<InvalidOperationException>(async () => await boundary.RunAsync("busy second stage",
                () => operations.CheckMainUpdates("p", "r", CancellationToken.None)));
        }
        Git(working, "remote", "set-url", "origin", Path.Combine(root, "missing.git"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await operations.CheckMainUpdates("p", "r", CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await operations.CheckMainUpdates("p", "r", cancelled.Token));
    }

    [TestCase("missing")]
    [TestCase("malformed")]
    [TestCase("invalid-model")]
    [TestCase("cancelled")]
    public void ReimportPreflightPreservesOwnedLink(string kind)
    {
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        var incoming = Path.Combine(root, "incoming.json");
        if (kind != "missing") File.WriteAllText(incoming, kind == "malformed" ? "{" :
            JsonSerializer.Serialize(new ProfileModel { ProfileName = "incoming", StandaloneModels =
                kind == "invalid-model" ? [new() { ModelName = "bad?name" }] : [] }));
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", ProfileFilePath = incoming,
            StandaloneModels = [new() { ModelName = "A", ModelType = ModelType.Compiled, CompiledModelFolder = payload, IsDeployed = true }] }, true);
        InstallLink("A", payload);
        using var cts = new CancellationTokenSource();
        if (kind == "cancelled") cts.Cancel();
        var error = Assert.Catch(() => new ProfileOperations(context).Reimport("p", cts.Token));
        if (kind == "cancelled") Assert.That(error, Is.InstanceOf<OperationCanceledException>());
        Assert.That(refreshes, Is.Zero);
        Assert.That(links.Deletes, Is.Zero);
        Assert.That(new DirectoryInfo(Path.Combine(config.DeploymentBasePath, "A")).LinkTarget, Is.EqualTo(payload));
        Assert.That(context.Ledger.LoadRecords(), Has.Count.EqualTo(1));
    }

    [Test]
    public void ReimportPropagatesCancellationAtImportDeploymentBoundary()
    {
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        var incoming = Path.Combine(root, "incoming.json");
        File.WriteAllText(incoming, JsonSerializer.Serialize(new ProfileModel { ProfileName = "incoming" }));
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", ProfileFilePath = incoming,
            StandaloneModels = [new() { ModelName = "A", ModelType = ModelType.Compiled, CompiledModelFolder = payload, IsDeployed = true }] }, true);
        InstallLink("A", payload);
        using var cts = new CancellationTokenSource();
        var controlled = new WorkflowContext(config) { Links = links, RefreshServiceState = () => cts.Cancel() };
        Assert.Throws<OperationCanceledException>(() => new ProfileOperations(controlled).Reimport("p", cts.Token));
        Assert.That(links.Deletes, Is.Zero);
        Assert.That(context.Profile("p").FindModel("A"), Is.Not.Null);
        Assert.That(new DirectoryInfo(Path.Combine(config.DeploymentBasePath, "A")).LinkTarget, Is.EqualTo(payload));
    }

    [TestCase("status")]
    [TestCase("switch")]
    [TestCase("assign")]
    [TestCase("reset")]
    [TestCase("release")]
    public void CorruptIndexFailsRequiredInspectionBeforeSideEffects(string operation)
    {
        var repo = Path.Combine(root, "git");
        Git(root, "init", "-b", "main", repo);
        Git(repo, "remote", "add", "origin", repo);
        File.WriteAllText(Path.Combine(repo, "file.txt"), "fixture");
        Commit(repo);
        File.WriteAllText(Path.Combine(repo, ".git", "index"), "corrupt index");
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", IsActive = true,
            Repositories = [new() { RepoId = "r", RepoRootFolder = repo }] }, true);
        context.Files.SaveProfile(new ProfileModel { ProfileName = "target" }, true);
        config.CheckUncommittedBeforeSwitch = true;
        var payload = Path.Combine(root, "payload");
        Directory.CreateDirectory(payload);
        var active = context.Profile("p");
        active.StandaloneModels.Add(new() { ModelName = "A", ModelType = ModelType.Compiled, CompiledModelFolder = payload, IsDeployed = true });
        context.Files.SaveProfile(active);
        InstallLink("A", payload);
        var saved = File.ReadAllText(Path.Combine(config.ProfileStoragePath, "p.json"));
        var ops = new RepositoryOperations(context);
        var error = Assert.Catch(() =>
        {
            switch (operation)
            {
                case "status": ops.Status("p", "r", CancellationToken.None).GetAwaiter().GetResult(); break;
                case "switch": new ProfileOperations(context).Switch("target", CancellationToken.None); break;
                case "assign": ops.AssignTask("p", "r", "123", "comment", true, "task", true, true); break;
                case "reset": ops.Reset("p"); break;
                case "release": ops.Release("p"); break;
            }
        });
        Assert.That(error, Is.TypeOf<InvalidOperationException>());
        Assert.That(error!.Message, Does.Contain("inspect").IgnoreCase);
        Assert.That(refreshes, Is.Zero);
        Assert.That(File.ReadAllText(Path.Combine(config.ProfileStoragePath, "p.json")), Is.EqualTo(saved));
        Assert.That(links.Deletes, Is.Zero);
        Assert.That(new DirectoryInfo(Path.Combine(config.DeploymentBasePath, "A")).LinkTarget, Is.EqualTo(payload));
        Assert.That(File.ReadAllText(Path.Combine(repo, ".git", "HEAD")), Does.Contain("refs/heads/main"));
    }

    [Test]
    public async Task StrictGitInspectionDistinguishesCleanDirtyAndUnavailableComparison()
    {
        var repo = Path.Combine(root, "git");
        Git(root, "init", "-b", "main", repo);
        Git(repo, "remote", "add", "origin", repo);
        File.WriteAllText(Path.Combine(repo, "file.txt"), "fixture");
        Commit(repo);
        Git(repo, "update-ref", "refs/remotes/origin/main", "HEAD");
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", Repositories = [new() { RepoId = "r", RepoRootFolder = repo }] }, true);
        var ops = new RepositoryOperations(context);
        var clean = JsonSerializer.SerializeToElement(await ops.Status("p", "r", CancellationToken.None));
        Assert.That(clean.GetProperty("Dirty").GetBoolean(), Is.False);
        Assert.That(clean.GetProperty("HasMainUpdates").GetBoolean(), Is.False);
        File.AppendAllText(Path.Combine(repo, "file.txt"), "dirty");
        var dirty = JsonSerializer.SerializeToElement(await ops.Status("p", "r", CancellationToken.None));
        Assert.That(dirty.GetProperty("Dirty").GetBoolean(), Is.True);
        Git(repo, "update-ref", "-d", "refs/remotes/origin/main");
        Assert.ThrowsAsync<InvalidOperationException>(() => ops.Status("p", "r", CancellationToken.None));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => GitHelper.IsWorkingTreeDirtyStrictAsync(repo, cts.Token));
    }

    private string CreateCachedRepository()
    {
        var repo = Path.Combine(root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, "Build"));
        Directory.CreateDirectory(Path.Combine(repo, "Project"));
        Git(root, "init", repo);
        Git(repo, "remote", "add", "origin", repo);
        File.WriteAllText(Path.Combine(repo, "Build", "isv.config"), "<packages><package id=\"Pkg\" version=\"1.0.0\" /></packages>");
        File.WriteAllText(Path.Combine(repo, "Build", "nuget.config"), "<configuration><packageSources /></configuration>");
        var payload = Path.Combine(config.DeployablePackages, "Pkg-1.0.0");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "A.xref"), "cached");
        context.Files.SaveProfile(new ProfileModel { ProfileName = "p", Repositories = [new() { RepoId = "r", RepoRootFolder = repo }] }, true);
        new PackageOperations(context).Prepare("p", CancellationToken.None);
        var profile = context.Profile("p");
        profile.FindModel("A")!.IsDeployed = true;
        context.Files.SaveProfile(profile);
        InstallLink("A", payload);
        return repo;
    }

    private void InstallLink(string name, string payload)
    {
        Directory.CreateDirectory(config.DeploymentBasePath);
        Directory.CreateSymbolicLink(Path.Combine(config.DeploymentBasePath, name), payload);
        context.Ledger.RecordDeployment(new DeployedModelRecord { ProfileName = "p", ModelName = name, SourcePath = payload });
    }

    private static void Commit(string repo)
    {
        Git(repo, "add", "file.txt");
        Git(repo, "-c", "user.name=Task3 test", "-c", "user.email=task3@example.invalid", "commit", "-m", "fixture");
    }

    private static void Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.That(process.WaitForExit(15000), Is.True);
        Task.WaitAll(stdout, stderr);
        Assert.That(process.ExitCode, Is.Zero, stderr.Result);
    }

    private sealed class CountingLinks : IDirectoryLinkService
    {
        private readonly DirectoryLinkService inner = new();
        public int Deletes { get; private set; }
        public int Creates { get; private set; }
        public bool Exists(string path) => inner.Exists(path);
        public string? ResolveLinkTarget(string path) => inner.ResolveLinkTarget(path);
        public void Delete(string path, bool recursive = true) { Deletes++; inner.Delete(path, recursive); }
        public void CreateSymbolicLink(string path, string target) { Creates++; inner.CreateSymbolicLink(path, target); }
    }
}
