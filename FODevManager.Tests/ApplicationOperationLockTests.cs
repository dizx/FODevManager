using System.Diagnostics;
using System.Text;
using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Operations;
using FODevManager.Services;
using FODevManager.Utils;
using Microsoft.Extensions.Configuration;

namespace FODevManager.Tests;

[TestFixture]
[NonParallelizable]
public class ApplicationOperationLockTests
{
    private string root = null!;
    private ApplicationOperationLock operationLock = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), "fodev-host-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        operationLock = new(Path.Combine(root, "operation.lock"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public async Task RealChildContenderCannotEnterUntilNestedWorkflowActuallyCompletes()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var boundary = new HostOperationBoundary(operationLock);
        async Task NestedServiceWork()
        {
            entered.SetResult();
            await finish.Task;
        }
        var running = boundary.RunAsync("outer workflow", async () =>
        {
            await Task.Run(NestedServiceWork);
            return 42;
        }, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            Assert.That(running.IsCompleted, Is.False);
            Assert.That(await RunContender(), Is.EqualTo(23));
        }
        finally { finish.TrySetResult(); }
        Assert.That(await running, Is.EqualTo(42));
        Assert.That(await RunContender(), Is.EqualTo(17));
    }

    private async Task<int> RunContender()
    {
        var script = "$ErrorActionPreference='Stop'; try { $lease=[System.IO.File]::Open('"
            + operationLock.LockPath.Replace("'", "''")
            + "',[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None); $lease.Dispose(); exit 17 } catch { if (($_.Exception.InnerException.HResult -band 65535) -in 32,33) { exit 23 }; exit 99 }";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var child = Process.Start(start)!;
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { if (!child.HasExited) child.Kill(true); }
        return child.ExitCode;
    }

    [Test]
    public async Task IndependentAsyncRequestsAndNestedBoundariesNeverInheritOwnership()
    {
        var boundary = new HostOperationBoundary(operationLock);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = boundary.RunAsync("first", async () =>
        {
            Assert.Throws<InvalidOperationException>(() => boundary.Run("incorrect nested boundary", () => true));
            await finish.Task;
            return true;
        });
        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await boundary.RunAsync("independent", () => Task.FromResult(true)));
        }
        finally { finish.SetResult(); }
        Assert.That(await first, Is.True);
        Assert.That(await boundary.RunAsync("later", () => Task.FromResult(true)), Is.True);
    }

    [Test]
    public void ExceptionsAndCancellationReleaseTheBoundary()
    {
        var boundary = new HostOperationBoundary(operationLock);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await boundary.RunAsync<bool>("failure", async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("nested service failed");
        }));
        Assert.ThrowsAsync<OperationCanceledException>(async () => await boundary.RunAsync<bool>("cancel", async () =>
        {
            await Task.Yield();
            throw new OperationCanceledException();
        }));
        using var lease = operationLock.TryAcquire();
        Assert.That(lease, Is.Not.Null);
    }

    [Test]
    public void CliBusyFailsBeforeHostConstructionAndLogsUsefulError()
    {
        using var lease = operationLock.TryAcquire();
        var constructed = false;
        var messages = new List<Message>();
        using var subscription = MessageBus.Subscribe(messages.Add);
        var result = Program.Execute(["-profile", "Test", "create"], () =>
        {
            constructed = true;
            return Program.BuildHost(root);
        }, operationLock: operationLock);
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(1));
            Assert.That(constructed, Is.False);
            Assert.That(messages.Any(m => m.Type == MessageType.Error && m.Content.Contains("busy")), Is.True);
        });
    }

    [Test]
    public async Task RealCliMutatorContendsWithMachineLeaseWhileReadCommandsStillWork()
    {
        using var lease = new ApplicationOperationLock().TryAcquire();
        Assert.That(lease, Is.Not.Null);
        Assert.That(await RunCli("-profile", "Test", "create"), Is.EqualTo(1));
        Assert.That(await RunCli("list"), Is.Zero);
        Assert.That(Directory.Exists(Path.Combine(root, "profiles")), Is.False);
        Assert.That(File.Exists(Path.Combine(root, "profiles", "Test.json")), Is.False);
    }

    private async Task<int> RunCli(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(FODevManager.Mcp.McpHost).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var key in new[] { "ProfileStoragePath", "DefaultSourceDirectory", "DeploymentBasePath", "DeployablePackages" })
            start.Environment[key] = Path.Combine(root, key == "ProfileStoragePath" ? "profiles" : key);
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { if (!child.HasExited) child.Kill(true); }
        await Task.WhenAll(stdout, stderr);
        return child.ExitCode;
    }

    [Test]
    public void PropertyEditsPreserveExternalFieldsMembershipAndModelStateOnFreshLoad()
    {
        var config = new AppConfig(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [nameof(AppConfig.ProfileStoragePath)] = root
        }).Build());
        var files = new FileService(config);
        var old = new RepositoryModel { RepoId = "repo", DisplayName = "Original", Task = "old task", PreferredBranch = "main" };
        var edited = new RepositoryModel { DisplayName = "Edited", Task = "old task", PreferredBranch = "main" };
        var changes = new PropertyEdits<RepositoryModel>(old, edited,
            nameof(RepositoryModel.DisplayName), nameof(RepositoryModel.Task), nameof(RepositoryModel.PreferredBranch));
        var profile = new ProfileModel { ProfileName = "Test", DatabaseName = "ExternalDB", Repositories = [new()
        {
            RepoId = "repo", DisplayName = "Original", Task = "external task", PreferredBranch = "external branch",
            Models = [new() { ModelName = "ExternalModel", ModelType = ModelType.Compiled, IsDeployed = true }]
        }] };
        files.SaveProfile(profile, skipExistCheck: true);
        new HostOperationBoundary(operationLock).Run("save properties", () =>
        {
            var fresh = files.LoadProfile("Test");
            changes.Apply(fresh.Repositories[0]);
            files.SaveProfile(fresh);
            return true;
        });
        var saved = files.LoadProfile("Test");
        Assert.Multiple(() =>
        {
            Assert.That(saved.DatabaseName, Is.EqualTo("ExternalDB"));
            Assert.That(saved.Repositories[0].DisplayName, Is.EqualTo("Edited"));
            Assert.That(saved.Repositories[0].Task, Is.EqualTo("external task"));
            Assert.That(saved.Repositories[0].PreferredBranch, Is.EqualTo("external branch"));
            Assert.That(saved.AllModels.Single().IsDeployed, Is.True);
        });
    }

    [Test]
    public void ModelEditsPreserveExternalPathsDeploymentAndUneditedVersionComponents()
    {
        var original = new ProfileEnvironmentModel { IsMainFOModel = false, PackageVersion = "1.0.0" };
        var edited = new ProfileEnvironmentModel { IsMainFOModel = true, PackageVersion = "1.0.0" };
        var fresh = new ProfileEnvironmentModel
        {
            ModelName = "Model", PackageVersion = "2.0.0", MetadataFolder = "external-path", IsDeployed = true
        };
        var edits = new PropertyEdits<ProfileEnvironmentModel>(original, edited,
            nameof(ProfileEnvironmentModel.IsMainFOModel), nameof(ProfileEnvironmentModel.PackageVersion));
        edits.Apply(fresh);
        var version = ModelVersionEdits.Apply(new(1, 2, 3), new(4, 2, 3), new(1, 5, 6));
        Assert.Multiple(() =>
        {
            Assert.That(fresh.IsMainFOModel, Is.True);
            Assert.That(fresh.PackageVersion, Is.EqualTo("2.0.0"));
            Assert.That(fresh.MetadataFolder, Is.EqualTo("external-path"));
            Assert.That(fresh.IsDeployed, Is.True);
            Assert.That(version, Is.EqualTo(new ModelVersion(4, 5, 6)));
        });
    }

    [Test]
    public void SettingsEditsWriteOnlyUserChoicesAndReloadPreservesExternalConfiguration()
    {
        var config = new AppConfig(new ConfigurationBuilder().Build());
        var settings = new SettingsOperations(config, root, "Task3Tests");
        var original = new SettingsUpdate { PushDeployablePackageOnBuild = false, AzureArtifactsPat = "old", DefaultSourceDirectory = "old-source" };
        var edited = original with { PushDeployablePackageOnBuild = true };
        var edits = new PropertyEdits<SettingsUpdate>(original, edited, nameof(SettingsUpdate.PushDeployablePackageOnBuild),
            nameof(SettingsUpdate.AzureArtifactsPat), nameof(SettingsUpdate.DefaultSourceDirectory));
        settings.Update(original with { AzureArtifactsPat = "external", DefaultSourceDirectory = Path.Combine(root, "external-source") });
        var patch = new SettingsUpdate();
        edits.Apply(patch);
        new HostOperationBoundary(operationLock).Run("Save settings", () => settings.Update(patch));
        Assert.Multiple(() =>
        {
            Assert.That(config.PushDeployablePackageOnBuild, Is.True);
            Assert.That(config.AzureArtifactsPat, Is.EqualTo("external"));
            Assert.That(config.DefaultSourceDirectory, Is.EqualTo(Path.Combine(root, "external-source")));
        });
    }

    [Test]
    public void ReadOnlyFileServiceDoesNotCreateStateDuringAnotherOperation()
    {
        var path = Path.Combine(root, "not-created");
        var config = new AppConfig(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [nameof(AppConfig.ProfileStoragePath)] = path
        }).Build());
        using var lease = operationLock.TryAcquire();
        var files = new FileService(config, ensureDirectory: false);
        Assert.That(files.GetAllProfiles(), Is.Empty);
        Assert.That(Directory.Exists(path), Is.False);
    }

    [Test]
    public async Task FailingNestedWorkRetainsLeaseThroughAsyncCleanup()
    {
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = new HostOperationBoundary(operationLock).RunAsync<bool>("workflow", async () =>
        {
            try { throw new InvalidOperationException("service failed"); }
            finally
            {
                cleaning.SetResult();
                await cleaned.Task;
            }
        });
        try
        {
            await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var contender = new ApplicationOperationLock(operationLock.LockPath).TryAcquire();
            Assert.That(contender, Is.Null);
            Assert.That(running.IsCompleted, Is.False);
        }
        finally { cleaned.SetResult(); }
        Assert.ThrowsAsync<InvalidOperationException>(async () => await running);
        using var released = operationLock.TryAcquire();
        Assert.That(released, Is.Not.Null);
    }
}
