using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FODevManager.Messages;
using FODevManager.Operations;
using FODevManager.Utils;

namespace FODevManager.Tests;

[TestFixture]
public class OperationRunnerTests
{
    private string directory = null!;
    private AppConfig config = null!;
    private ApplicationOperationLock operationLock = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "FODevManager-operation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        config = new AppConfig { ProfileStoragePath = directory, AzureArtifactsPat = "configured-secret" };
        operationLock = new ApplicationOperationLock(Path.Combine(directory, "operation.lock"));
    }

    [TearDown]
    public void TearDown() => Directory.Delete(directory, true);

    private OperationRunner Runner(int history = 100, int diagnostics = 100) =>
        new(config, operationLock, history, diagnostics);

    [Test]
    public async Task SerializesAndExposesWaitingAndRunningStatus()
    {
        var runner = Runner();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = runner.RunAsync("first", true, async _ => { entered.SetResult(); await finish.Task; return 42; }, requestId: "request");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondEntered = false;
        var second = runner.RunAsync("second", false, _ => { secondEntered = true; return Task.FromResult<object?>(null); });
        try
        {
            Assert.That(secondEntered, Is.False);
            var active = runner.ListOperations().Single(x => x.RequestId == "request");
            Assert.That(runner.GetStatus(active.OperationId)!.Status, Is.EqualTo("running"));
            Assert.That(runner.ListOperations().Any(x => x.Status == "queued"), Is.True);
        }
        finally { finish.TrySetResult(); }
        Assert.That((await first).Status, Is.EqualTo("succeeded"));
        Assert.That((await second).Status, Is.EqualTo("succeeded"));
    }

    [Test]
    public async Task BusyLeaseDoesNotExecuteAndReleasesAcrossAsyncThreads()
    {
        var runner = Runner();
        var lease = operationLock.TryAcquire();
        Assert.That(lease, Is.Not.Null);
        using (lease)
        {
            var result = await runner.RunAsync("busy", true, _ => throw new AssertionException("Executed"));
            Assert.That(result.Status, Is.EqualTo("busy"));
            Assert.That(result.PartialChangesPossible, Is.False);
        }
        var other = new ApplicationOperationLock(Path.Combine(directory, "operation.lock"));
        var next = other.TryAcquire();
        Assert.That(next, Is.Not.Null);
        await Task.Run(() => next!.Dispose());
        next!.Dispose();
        using var reacquired = operationLock.TryAcquire();
        Assert.That(reacquired, Is.Not.Null);
    }

    [Test]
    public async Task CancellationBeforeAndWhileWaitingNeverExecutes()
    {
        var runner = Runner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await runner.RunAsync("pre", true, _ => throw new AssertionException("Executed"), cancellation.Token);
        Assert.That(cancelled.Status, Is.EqualTo("cancelled"));
        Assert.That(cancelled.PartialChangesPossible, Is.False);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = runner.RunAsync("running", false, async _ => { await finish.Task; return null; });
        using var waitingCancellation = new CancellationTokenSource();
        var waiting = runner.RunAsync("waiting", true, _ => throw new AssertionException("Executed"), waitingCancellation.Token);
        waitingCancellation.Cancel();
        try { Assert.That((await waiting).Status, Is.EqualTo("cancelled")); }
        finally { finish.TrySetResult(); await running; }
    }

    [Test]
    public async Task NonCooperativeWorkKeepsLeaseAndRunningStatusUntilItFinishes()
    {
        var runner = Runner();
        using var cancellation = new CancellationTokenSource();
        using var finish = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = Task.Run(() => runner.RunAsync("sync", true, _ =>
        {
            entered.SetResult();
            if (!finish.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return Task.FromResult<object?>(42);
        }, cancellation.Token));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try
        {
            Assert.That(running.IsCompleted, Is.False);
            Assert.That(runner.ListOperations().Single().Status, Is.EqualTo("running"));
            using var lease = operationLock.TryAcquire();
            Assert.That(lease, Is.Null);
        }
        finally { finish.Set(); }
        Assert.That((await running).Status, Is.EqualTo("succeeded"));
    }

    [Test]
    public async Task CapturesErrorsEvenWithThrowingProgressAndBoundsDiagnostics()
    {
        var runner = Runner(diagnostics: 2);
        var result = await runner.RunAsync("failure", true, _ =>
        {
            MessageLogger.Info("first");
            MessageLogger.Info("second");
            MessageLogger.Error("configured-secret failed");
            for (var i = 0; i < 10; i++) MessageLogger.Info("later info");
            return Task.FromResult<object?>(new { password = "unknown-secret", value = "configured-secret" });
        }, progress: new ThrowingProgress());
        Assert.That(result.Status, Is.EqualTo("failed"));
        Assert.That(result.PartialChangesPossible, Is.True);
        Assert.That(result.Diagnostics.Count, Is.LessThanOrEqualTo(2));
        Assert.That(result.DroppedDiagnosticCount, Is.GreaterThan(0));
        Assert.That(result.Diagnostics.Any(x => x.Contains("Error:") && x.Contains("failed")), Is.True);
        Assert.That(JsonSerializer.Serialize(result), Does.Not.Contain("configured-secret").And.Not.Contain("unknown-secret"));
        using var lease = operationLock.TryAcquire();
        Assert.That(lease, Is.Not.Null);
    }

    [Test]
    public async Task ExceptionsCancellationAndSerializationFailuresReleaseLease()
    {
        var runner = Runner();
        var failed = await runner.RunAsync("exception", true, _ => throw new InvalidOperationException("configured-secret"));
        Assert.That(failed.Status, Is.EqualTo("failed"));
        Assert.That(failed.PartialChangesPossible, Is.True);
        Assert.That(JsonSerializer.Serialize(failed), Does.Not.Contain("configured-secret"));
        using var cancellation = new CancellationTokenSource();
        var cancelled = await runner.RunAsync("cancel", true, token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult<object?>(null); }, cancellation.Token);
        Assert.That(cancelled.Status, Is.EqualTo("cancelled"));
        Assert.That(cancelled.PartialChangesPossible, Is.True);
        var invalid = await runner.RunAsync("invalid", true, _ => Task.FromResult<object?>(new BadData()));
        Assert.That(invalid.Status, Is.EqualTo("failed"));
        var good = await runner.RunAsync("good", true, _ => Task.FromResult<object?>(42));
        Assert.That(good.Status, Is.EqualTo("succeeded"));
    }

    [Test]
    public async Task HistoryIsBoundedAndSnapshotsAreDetached()
    {
        var runner = Runner(history: 2);
        var first = await runner.RunAsync("one", false, _ => Task.FromResult<object?>(new { value = 42 }));
        await runner.RunAsync("two", false, _ => Task.FromResult<object?>(null));
        await runner.RunAsync("three", false, _ => Task.FromResult<object?>(null));
        Assert.That(runner.ListOperations(), Has.Count.EqualTo(2));
        Assert.That(runner.GetStatus(first.OperationId), Is.Null);
        Assert.That(((JsonElement)first.Data!).GetProperty("value").GetInt32(), Is.EqualTo(42));
    }

    [Test]
    public void RedactsConfiguredAndUrlSecretsInNestedDataAndPropertyNames()
    {
        var redactor = new SecretRedactor(config);
        var data = redactor.RedactData(new Dictionary<string, object?>
        {
            ["configured-secret"] = 42,
            ["nested"] = new { url = "https://alice:unknown@host/path?sig=signature&x=ok&access_token=token#fragment", ApiKey = "other", text = "configured-secret" }
        });
        var json = JsonSerializer.Serialize(data);
        Assert.That(json, Does.Not.Contain("configured-secret").And.Not.Contain("alice").And.Not.Contain("unknown").And.Not.Contain("signature").And.Not.Contain("=token").And.Not.Contain("other"));
        Assert.That(json, Does.Contain("x=ok"));
    }

    [Test]
    public async Task LockContendsAcrossProcessesAndRecoversAfterOwnerExit()
    {
        var eventName = "Local\\FODevManager-test-" + Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var path = operationLock.LockPath.Replace("'", "''");
        var script = "$ErrorActionPreference='Stop'; $lease=[System.IO.File]::Open('" + path
            + "',[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None); "
            + "$ready=[System.Threading.EventWaitHandle]::OpenExisting('" + eventName + "'); $ready.Set() | Out-Null; Start-Sleep -Seconds 60";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var child = Process.Start(start)!;
        try
        {
            Assert.That(await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(15))), Is.True);
            using var blocked = operationLock.TryAcquire();
            Assert.That(blocked, Is.Null);
            var result = await Runner().RunAsync("child-busy", true, _ => throw new AssertionException("Executed"));
            Assert.That(result.Status, Is.EqualTo("busy"));
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        using var recovered = operationLock.TryAcquire();
        Assert.That(recovered, Is.Not.Null);
    }

    [Test]
    public async Task QueriesDoNotAcquireMutationLeaseAndCaptureIsExecutionScoped()
    {
        var first = Runner();
        var second = Runner();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = first.RunAsync("first", false, async _ => { await finish.Task; return null; });
        try
        {
            MessageLogger.Error("unrelated background message");
            using var lease = operationLock.TryAcquire();
            Assert.That(lease, Is.Not.Null);
            var result = await second.RunAsync("second", false, async _ =>
            {
                await Task.Run(() => MessageLogger.Error("scoped error"));
                return null;
            });
            Assert.That(result.Status, Is.EqualTo("failed"));
            Assert.That(result.PartialChangesPossible, Is.False);
        }
        finally { finish.TrySetResult(); }
        Assert.That((await running).Status, Is.EqualTo("succeeded"));
        Assert.That((await running).Diagnostics, Is.Empty);
    }

    [Test]
    public async Task DiagnosticsAndDataAreBoundedAndProgressIsRedacted()
    {
        var runner = Runner(diagnostics: 2);
        var messages = new List<string>();
        var result = await runner.RunAsync("probe configured-secret", false, _ =>
        {
            MessageLogger.Warning(new string('x', 10000) + "configured-secret");
            MessageLogger.Error("https://user:password@host/?api_key=hidden");
            return Task.FromResult<object?>(null);
        }, requestId: "configured-secret", progress: new CollectingProgress(messages));
        Assert.That(result.Diagnostics.All(x => x.Length < 4200), Is.True);
        Assert.That(string.Join(" ", messages), Does.Not.Contain("configured-secret").And.Not.Contain("password").And.Not.Contain("hidden"));
        Assert.That(JsonSerializer.Serialize(result), Does.Not.Contain("configured-secret"));
        var oversized = await runner.RunAsync("large", false, _ => Task.FromResult<object?>(new string('x', 1024 * 1024 + 1)));
        Assert.That(oversized.Status, Is.EqualTo("failed"));
        Assert.That(oversized.Data, Is.Null);
    }

    [Test]
    public async Task InvalidLockPathFailsExplicitlyWithoutExecuting()
    {
        var file = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(file, "test");
        var runner = new OperationRunner(config, new ApplicationOperationLock(Path.Combine(file, "operation.lock")));
        var result = await runner.RunAsync("invalid-lock", true, _ => throw new AssertionException("Executed"));
        Assert.That(result.Status, Is.EqualTo("failed"));
        Assert.That(result.PartialChangesPossible, Is.False);
        Assert.That(result.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public async Task NestedRunnerFailsWithoutDeadlockingAndOuterLeaseIsReleased()
    {
        var runner = Runner();
        var result = await runner.RunAsync("outer", true, async _ =>
            await runner.RunAsync("inner", true, _ => Task.FromResult<object?>(null))).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(result.Status, Is.EqualTo("failed"));
        Assert.That(result.PartialChangesPossible, Is.True);
        using var lease = operationLock.TryAcquire();
        Assert.That(lease, Is.Not.Null);
    }

    [Test]
    public void RedactorUsesCurrentConfigAndEncodedCredentials()
    {
        var redactor = new SecretRedactor(config);
        config.AzureArtifactsApiKey = "key/with+special chars";
        Assert.That(redactor.Redact(Uri.EscapeDataString(config.AzureArtifactsApiKey)), Is.EqualTo(SecretRedactor.Replacement));
        Assert.That(redactor.Redact("https://host/?X-Amz-Signature=unknown&%74oken=hidden"), Does.Not.Contain("unknown").And.Not.Contain("hidden"));
    }

    [TestCase("succeeded")]
    [TestCase("logged-error")]
    [TestCase("exception")]
    [TestCase("cancelled")]
    public async Task StatusPollingNeverObservesIncompleteOrIncorrectTerminalSnapshot(string outcome)
    {
        var runner = Runner(history: 8);
        using var stop = new CancellationTokenSource();
        using var ready = new ManualResetEventSlim();
        var polling = Task.Factory.StartNew(() =>
        {
            string? violation = null;
            var samples = 0;
            ready.Set();
            while (!stop.IsCancellationRequested)
            {
                foreach (var listed in runner.ListOperations())
                {
                    Check(listed);
                    if (runner.GetStatus(listed.OperationId) is { } status) Check(status);
                }
                Thread.Yield();
            }
            return (violation, samples);

            void Check(OperationResult snapshot)
            {
                samples++;
                if (snapshot.Status is "queued" or "running")
                {
                    if (snapshot.CompletedAt is not null) violation ??= "Active snapshot has completion time";
                    return;
                }
                if (snapshot.CompletedAt is null) violation ??= "Terminal snapshot lacks completion time";
                if (snapshot.Status == "succeeded" && outcome != "succeeded") violation ??= "Failed mutation transiently succeeded";
                if (snapshot.PartialChangesPossible != (outcome != "succeeded")) violation ??= "Terminal snapshot has incorrect partial-change flag";
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.That(ready.Wait(TimeSpan.FromSeconds(5)), Is.True);
            for (var i = 0; i < 300; i++)
            {
                using var cancellation = new CancellationTokenSource();
                var result = await runner.RunAsync("poll", true, async token =>
                {
                    await Task.Yield();
                    if (outcome == "logged-error") MessageLogger.Error("Mutation failed");
                    if (outcome == "exception") throw new InvalidOperationException("Mutation failed");
                    if (outcome == "cancelled") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                    return 42;
                }, cancellation.Token);
                Assert.That(result.Status, Is.EqualTo(outcome is "logged-error" or "exception" ? "failed" : outcome));
            }
        }
        finally { stop.Cancel(); }
        var observed = await polling.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(observed.samples, Is.GreaterThan(0));
        Assert.That(observed.violation, Is.Null);
    }

    [Test]
    public async Task StatusRemainsRunningWhileFailureDiagnosticsAreCaptured()
    {
        var runner = Runner();
        OperationResult? duringCapture = null;
        var result = await runner.RunAsync("failure", true, _ => throw new ObservedException(() =>
        {
            var listed = runner.ListOperations().Single();
            duringCapture = runner.GetStatus(listed.OperationId);
        }));
        Assert.That(duringCapture, Is.Not.Null);
        Assert.That(duringCapture!.Status, Is.EqualTo("running"));
        Assert.That(duringCapture.CompletedAt, Is.Null);
        Assert.That(result.Status, Is.EqualTo("failed"));
        Assert.That(result.CompletedAt, Is.Not.Null);
        Assert.That(result.PartialChangesPossible, Is.True);
    }

    [TestCase("key%2fwith%2bspecial%20chars")]
    [TestCase("key%2Fwith%2bspecial%20chars")]
    [TestCase("key%2fwith%2Bspecial+chars")]
    public void RedactsEquivalentPercentEscapeCasingWithoutChangingLiteralCase(string encoded)
    {
        config.AzureArtifactsApiKey = "key/with+special chars";
        var redactor = new SecretRedactor(config);
        var inputs = new[] { "before " + encoded + " after", "https://host/path/" + encoded + "/end", "https://host/?value=" + encoded + "&x=ok" };
        foreach (var input in inputs)
        {
            var expected = input.Replace(encoded, SecretRedactor.Replacement, StringComparison.Ordinal);
            Assert.That(redactor.Redact(input), Is.EqualTo(expected));
            var data = (JsonElement)redactor.RedactData(new { value = input })!;
            Assert.That(data.GetProperty("value").GetString(), Is.EqualTo(expected));
            var differentCredential = input.Replace("key", "Key", StringComparison.Ordinal);
            Assert.That(redactor.Redact(differentCredential), Is.EqualTo(differentCredential));
        }
        Assert.That(redactor.Redact("Key/with+special chars"), Is.EqualTo("Key/with+special chars"));
    }

    private sealed class ObservedException(Action observe) : Exception
    {
        public override string Message { get { observe(); return "Mutation failed"; } }
    }

    private sealed class CollectingProgress(List<string> messages) : IProgress<string>
    {
        public void Report(string value) => messages.Add(value);
    }

    private sealed class ThrowingProgress : IProgress<string>
    {
        public void Report(string value) => throw new InvalidOperationException("Observer failed");
    }

    private sealed class BadData
    {
        public string Value => throw new InvalidOperationException("configured-secret");
    }
}
