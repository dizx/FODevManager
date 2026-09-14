using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FODevManager.Mcp;
using FODevManager.Operations;

namespace FODevManager.Tests;

[TestFixture, NonParallelizable]
public class McpProtocolTests
{
    private const string Secret = "mcp-wire-private-credential-42";
    private string root = null!;

    [SetUp]
    public void SetUp() => Directory.CreateDirectory(root = Path.Combine(Path.GetTempPath(), "fodev-wire-" + Guid.NewGuid().ToString("N")));

    [TearDown]
    public void TearDown() => Directory.Delete(root, true);

    [Test]
    public async Task ProductionWireInventoryMatchesCheckedDesktopMappingAndSafetyDescriptions()
    {
        VerifyPublishedArtifactsWhenSelected();
        await using var wire = Start();
        var initialized = await wire.Initialize();
        Assert.That(initialized.GetProperty("result").GetProperty("capabilities").TryGetProperty("tools", out _), Is.True);
        var response = await wire.Request("tools/list");
        var tools = response.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
        string[] Inventory(string file) => Regex.Matches(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, file)), @"(?m)^\| `([a-z_]+)` \|")
            .Select(m => m.Groups[1].Value).ToArray();
        var documented = Inventory("mcp.md");
        Assert.That(documented, Has.Length.EqualTo(52));
        Assert.That(documented.Distinct().Count(), Is.EqualTo(52));
        Assert.That(documented, Is.EquivalentTo(Inventory("mcp-task-2-report.md")));
        Assert.That(tools.Select(t => t.GetProperty("name").GetString()), Is.EquivalentTo(documented));
        foreach (var tool in tools)
        {
            Assert.That(tool.GetProperty("description").GetString(), Is.Not.Null.And.Not.Empty);
            Assert.That(tool.GetProperty("inputSchema").GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(tool.TryGetProperty("annotations", out _), Is.True);
        }
        JsonElement Tool(string name) => tools.Single(t => t.GetProperty("name").GetString() == name);
        Assert.That(Tool("package_build").GetProperty("inputSchema").GetProperty("required").EnumerateArray().Select(p => p.GetString()), Does.Contain("publish"));
        foreach (var (name, phrase) in new[] { ("package_build", "publish=true"), ("git_tag_release", "PUSH tags"),
            ("profile_switch", "control services"), ("repository_status", "without fetching"), ("profile_show", "saved state"),
            ("operation_status", "not deduplicate"), ("models_add_path", "deleting the original"), ("settings_update", "input-only") })
            Assert.That(Tool(name).GetProperty("description").GetString(), Does.Contain(phrase), name);
        await wire.CloseAndWait();
        wire.AssertClean(Secret);
    }

    [Test]
    public async Task ProductionErrorsDoNotPoisonSessionAndRequestIdsCorrelateWithoutDeduplication()
    {
        await using var wire = Start();
        await wire.Initialize();
        var unknownMethod = await wire.Request("not/a/method");
        Assert.That(unknownMethod.GetProperty("error").GetProperty("code").GetInt32(), Is.EqualTo(-32601));
        var unknownTool = await wire.Call("missing_tool");
        Assert.That(unknownTool.TryGetProperty("error", out _) || unknownTool.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
        var invalid = await wire.Call("profile_show", new { profileName = 123 });
        Assert.That(invalid.TryGetProperty("error", out _) || invalid.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
        var failure = await wire.Call("profile_show", new { profileName = Secret });
        Assert.That(failure.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
        Assert.That(Data(failure).GetProperty("status").GetString(), Is.EqualTo("failed"));
        var first = Data(await wire.Call("application_about", new { requestId = "repeat" }));
        var second = Data(await wire.Call("application_about", new { requestId = "repeat" }));
        Assert.That(first.GetProperty("operationId").GetString(), Is.Not.EqualTo(second.GetProperty("operationId").GetString()));
        var matches = Data(await wire.Call("operation_status", new { requestId = "repeat" })).GetProperty("operations");
        Assert.That(matches.GetArrayLength(), Is.EqualTo(2));
        var missing = await wire.Call("operation_status", new { operationId = "missing" });
        Assert.That(missing.GetProperty("result").GetProperty("isError").GetBoolean(), Is.True);
        await wire.CloseAndWait();
        wire.AssertClean(Secret);
        await using var restarted = Start();
        await restarted.Initialize();
        Assert.That(Data(await restarted.Call("operation_status", new { requestId = "repeat" })).GetProperty("operations").GetArrayLength(), Is.Zero);
        await restarted.CloseAndWait();
        restarted.AssertClean(Secret);
    }

    [Test]
    public async Task SettingsWritesAreLocalWhileEffectiveEnvironmentOverridesAndCredentialsStayPrivate()
    {
        var directory = CopyHost(false);
        await using var wire = Start(directory: directory);
        await wire.Initialize();
        var read = Data(await wire.Call("settings_read")).GetProperty("data");
        Assert.That(read.GetProperty("CredentialsConfigured").GetBoolean(), Is.True);
        Assert.That(read.TryGetProperty("AzureArtifactsPat", out _), Is.False);
        var localPath = Path.Combine(root, "saved-local");
        var update = await wire.Call("settings_update", new { settingsUpdate = new { ProfileStoragePath = localPath, AzureArtifactsPat = "new-input-only-secret", TaskUrl = "https://example.invalid/tasks/" } });
        Assert.That(update.GetProperty("result").GetProperty("isError").GetBoolean(), Is.False);
        var effective = Data(update).GetProperty("data").GetProperty("Effective");
        Assert.That(effective.GetProperty("ProfileStoragePath").GetString(), Is.EqualTo(Path.Combine(root, "profiles")));
        Assert.That(effective.GetProperty("TaskUrl").GetString(), Is.EqualTo("https://example.invalid/tasks/"));
        using var local = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "appsettings.json")));
        Assert.That(local.RootElement.GetProperty("ProfileStoragePath").GetString(), Is.EqualTo(localPath));
        Assert.That(local.RootElement.GetProperty("AzureArtifactsPat").GetString(), Is.EqualTo("new-input-only-secret"));
        await wire.CloseAndWait();
        wire.AssertClean(Secret, "new-input-only-secret");
    }

    [Test]
    public async Task WireCancellationRetainsRunningStatusProgressAndLeaseUntilNoncooperativeWorkFinishes()
    {
        var directory = CopyHost(true);
        await using var wire = Start(true, directory);
        await wire.Initialize();
        var (id, work) = wire.BeginRequest("tools/call", new { name = "test_noncooperative", arguments = new { requestId = "blocking" }, _meta = new { progressToken = "progress-1" } });
        await WaitFor(() => File.Exists(Path.Combine(directory, "entered")));
        await wire.Notify("notifications/cancelled", new { requestId = id, reason = "test cancellation" });
        await WaitFor(() => File.Exists(Path.Combine(directory, "cancelled")));
        var status = Data(await wire.Call("operation_status", new { requestId = "blocking" })).GetProperty("operations")[0];
        Assert.That(status.GetProperty("status").GetString(), Is.EqualTo("running"));
        Assert.That(status.GetProperty("completedAt").ValueKind, Is.EqualTo(JsonValueKind.Null));
        using (var lease = new ApplicationOperationLock(Path.Combine(directory, "probe.lock")).TryAcquire()) Assert.That(lease, Is.Null);
        await WaitFor(() => wire.Notifications.Any(n => n.GetProperty("method").GetString() == "notifications/progress"));
        var progress = wire.Notifications.First(n => n.GetProperty("method").GetString() == "notifications/progress").GetProperty("params");
        Assert.That(progress.GetProperty("progressToken").GetString(), Is.EqualTo("progress-1"));
        Assert.That(progress.GetProperty("progress").GetDouble(), Is.GreaterThan(0));
        Assert.That(progress.GetProperty("message").GetString(), Does.Contain("Entered controlled work").And.Not.Contain(Secret));
        Assert.That(work.IsCompleted, Is.False);
        var queued = wire.BeginRequest("tools/call", new { name = "application_about", arguments = new { requestId = "queued" } });
        await WaitForAsync(async () => Data(await wire.Call("operation_status", new { requestId = "queued" })).GetProperty("operations").GetArrayLength() == 1);
        await wire.Notify("notifications/cancelled", new { requestId = queued.Id });
        await WaitForAsync(async () => Data(await wire.Call("operation_status", new { requestId = "queued" })).GetProperty("operations")[0].GetProperty("status").GetString() == "cancelled");
        File.WriteAllText(Path.Combine(directory, "release"), "release");
        // The SDK suppresses the original response after cancellation; retained history is authoritative
        await WaitForAsync(async () => Data(await wire.Call("operation_status", new { requestId = "blocking" })).GetProperty("operations")[0].GetProperty("status").GetString() == "succeeded");
        var finished = Data(await wire.Call("operation_status", new { operationId = status.GetProperty("operationId").GetString() })).GetProperty("operations")[0];
        Assert.That(finished.GetProperty("status").GetString(), Is.EqualTo("succeeded"));
        Assert.That(finished.GetProperty("data").GetProperty("Finished").GetBoolean(), Is.True);
        using (var lease = new ApplicationOperationLock(Path.Combine(directory, "probe.lock")).TryAcquire()) Assert.That(lease, Is.Not.Null);
        await wire.CloseAndWait();
        wire.AssertClean(Secret);
    }

    [Test]
    public async Task DisconnectDuringWorkDrainsActualWorkerBeforeProcessExit()
    {
        var directory = CopyHost(true);
        await using var wire = Start(true, directory);
        await wire.Initialize();
        wire.BeginRequest("tools/call", new { name = "test_noncooperative", arguments = new { requestId = "disconnect" } });
        await WaitFor(() => File.Exists(Path.Combine(directory, "entered")));
        wire.CloseInput();
        await Task.Delay(300);
        Assert.That(wire.HasExited, Is.False);
        using (var lease = new ApplicationOperationLock(Path.Combine(directory, "probe.lock")).TryAcquire()) Assert.That(lease, Is.Null);
        File.WriteAllText(Path.Combine(directory, "release"), "release");
        await wire.WaitForExit();
        Assert.That(File.Exists(Path.Combine(directory, "finished")), Is.True);
        using (var lease = new ApplicationOperationLock(Path.Combine(directory, "probe.lock")).TryAcquire()) Assert.That(lease, Is.Not.Null);
        wire.AssertClean(Secret);
    }

    private string CopyHost(bool controlled)
    {
        var directory = Path.Combine(root, controlled ? "controlled" : "production");
        Directory.CreateDirectory(directory);
        foreach (var file in Directory.EnumerateFiles(TestContext.CurrentContext.TestDirectory))
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(directory, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "appsettings.Development.json"), "{}");
        return directory;
    }

    private Wire Start(bool controlled = false, string? directory = null)
    {
        var published = directory == null ? Environment.GetEnvironmentVariable("FODEV_MCP_TEST_EXECUTABLE") : null;
        var assembly = controlled ? typeof(McpProtocolHost.Program).Assembly.Location : typeof(McpHost).Assembly.Location;
        var start = new ProcessStartInfo(published ?? "dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root };
        if (published == null) start.ArgumentList.Add(directory == null ? assembly : Path.Combine(directory, Path.GetFileName(assembly)));
        start.ArgumentList.Add("mcp");
        foreach (var (key, value) in new[] { ("ProfileStoragePath", Path.Combine(root, "profiles")), ("DeploymentBasePath", Path.Combine(root, "deployment")),
            ("DefaultSourceDirectory", Path.Combine(root, "source")), ("DeployablePackages", Path.Combine(root, "packages")),
            ("AzureArtifactsPat", Secret), ("AzureArtifactsUsername", "wire-private-user"), ("AzureArtifactsApiKey", "wire-private-key"),
            ("DOTNET_ENVIRONMENT", "McpProtocolTests") }) start.Environment[key] = value;
        start.Environment.Remove("TaskUrl");
        return new Wire(start);
    }

    private static JsonElement Data(JsonElement response) => response.GetProperty("result").GetProperty("structuredContent");

    private static void VerifyPublishedArtifactsWhenSelected()
    {
        var executable = Environment.GetEnvironmentVariable("FODEV_MCP_TEST_EXECUTABLE");
        if (string.IsNullOrEmpty(executable)) return;
        var directory = Path.GetDirectoryName(executable)!;
        foreach (var name in new[] { "fodev.exe", "FODevManager.WinUI.exe", "MainWindow.xbf", "FODevManager.WinUI.pri",
            "ModelContextProtocol.dll", "ModelContextProtocol.Core.dll", "docs/mcp.md" })
            Assert.That(File.Exists(Path.Combine(directory, name)), Is.True, name);
        Assert.That(File.Exists(Path.Combine(directory, "FODevManager.McpProtocolHost.dll")), Is.False);
        Assert.That(File.Exists(Path.Combine(directory, "appsettings.Development.json")), Is.False);
        using var cli = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "fodev.deps.json")));
        using var desktop = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "FODevManager.WinUI.deps.json")));
        var cliLibraries = cli.RootElement.GetProperty("libraries").EnumerateObject().ToDictionary(p => p.Name.Split('/')[0], p => p.Name);
        foreach (var library in desktop.RootElement.GetProperty("libraries").EnumerateObject())
            if (cliLibraries.TryGetValue(library.Name.Split('/')[0], out var counterpart)) Assert.That(counterpart, Is.EqualTo(library.Name), "Shared publish dependency version");
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "appsettings.json")));
        foreach (var key in new[] { "AzureArtifactsUsername", "AzureArtifactsPat", "AzureArtifactsApiKey" })
            Assert.That(settings.RootElement.GetProperty(key).GetString() == "", Is.True, "Published credential must be empty");
        var nuget = System.Xml.Linq.XDocument.Load(Path.Combine(directory, "nuget.config"));
        Assert.That(nuget.Descendants().Any(e => e.Name.LocalName is "packageSourceCredentials" or "apikeys"), Is.False);
    }

    [Test]
    public void PresenceIndicatorExceptionCannotExposeStringOrStructuredCredentials()
    {
        var redactor = new SecretRedactor(new FODevManager.Utils.AppConfig());
        var data = (JsonElement)redactor.RedactData(new { CredentialsConfigured = "unconfigured-sensitive-value",
            Nested = new { CredentialsConfigured = new { Value = "another-sensitive-value" } } })!;
        Assert.That(data.GetProperty("CredentialsConfigured").GetString(), Is.EqualTo(SecretRedactor.Replacement));
        Assert.That(data.GetProperty("Nested").GetProperty("CredentialsConfigured").GetString(), Is.EqualTo(SecretRedactor.Replacement));
    }
    private static async Task WaitFor(Func<bool> ready) => await WaitForAsync(() => Task.FromResult(ready()));
    private static async Task WaitForAsync(Func<Task<bool>> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await ready())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Timed out waiting for protocol/worker state");
            await Task.Delay(20);
        }
    }

    private sealed class Wire : IAsyncDisposable
    {
        private readonly Process process;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
        private readonly ConcurrentQueue<string> output = new();
        private readonly Task reader;
        private readonly Task<string> stderr;
        private int nextId;
        public ConcurrentQueue<JsonElement> Notifications { get; } = new();
        public bool HasExited => process.HasExited;

        public Wire(ProcessStartInfo start)
        {
            process = Process.Start(start)!;
            stderr = process.StandardError.ReadToEndAsync();
            reader = Task.Run(async () =>
            {
                try
                {
                    while (await process.StandardOutput.ReadLineAsync() is { } line)
                    {
                        output.Enqueue(line);
                        using var document = JsonDocument.Parse(line);
                        var message = document.RootElement.Clone();
                        Assert.That(message.GetProperty("jsonrpc").GetString(), Is.EqualTo("2.0"));
                        if (message.TryGetProperty("id", out var id) && id.TryGetInt32(out var number) && pending.TryRemove(number, out var completion)) completion.TrySetResult(message);
                        else Notifications.Enqueue(message);
                    }
                }
                catch (Exception exception)
                {
                    foreach (var completion in pending.Values) completion.TrySetException(exception);
                    throw;
                }
            });
        }

        public async Task<JsonElement> Initialize()
        {
            var result = await Request("initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "fodev-wire-tests", version = "1.0" } });
            Assert.That(result.TryGetProperty("error", out _), Is.False);
            await Notify("notifications/initialized");
            return result;
        }

        public (int Id, Task<JsonElement> Response) BeginRequest(string method, object? parameters = null)
        {
            var id = Interlocked.Increment(ref nextId);
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = completion;
            process.StandardInput.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters ?? new { } }));
            process.StandardInput.Flush();
            return (id, completion.Task);
        }

        public Task<JsonElement> Request(string method, object? parameters = null) => BeginRequest(method, parameters).Response.WaitAsync(TimeSpan.FromSeconds(15));
        public Task<JsonElement> Call(string name, object? arguments = null) => Request("tools/call", new { name, arguments = arguments ?? new { } });
        public async Task Notify(string method, object? parameters = null)
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters ?? new { } }));
            await process.StandardInput.FlushAsync();
        }
        public void CloseInput() => process.StandardInput.Close();
        public async Task CloseAndWait() { CloseInput(); await WaitForExit(); }
        public async Task WaitForExit()
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await reader;
            Assert.That(process.ExitCode, Is.Zero, "MCP child exit code");
        }
        public void AssertClean(params string[] secrets)
        {
            Assert.That(reader.IsCompletedSuccessfully, Is.True, "Every stdout line must be JSON-RPC, including shutdown output");
            var transcript = string.Join('\n', output) + stderr.GetAwaiter().GetResult();
            foreach (var secret in secrets.Concat(new[] { "wire-private-user", "wire-private-key" }))
                Assert.That(transcript.Contains(secret, StringComparison.Ordinal), Is.False, "Configured credential leaked into child output");
        }
        public async ValueTask DisposeAsync()
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            process.Dispose();
        }
    }
}
