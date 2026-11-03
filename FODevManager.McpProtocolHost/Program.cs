using FODevManager.Mcp;
using FODevManager.Messages;
using FODevManager.Operations;
using FODevManager.Utils;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FODevManager.McpProtocolHost;

public static class Program
{
    public static Task<int> Main() => McpHost.RunAsync(builder =>
    {
        builder.Services.AddSingleton<OperationRunner>(services => new OperationRunner(
            services.GetRequiredService<AppConfig>(), new ApplicationOperationLock(Path.Combine(AppContext.BaseDirectory, "probe.lock"))));
        builder.Services.AddMcpServer().WithTools<ControlledTools>();
    });
}

// This assembly is test-only and is never referenced by either shipping application
[McpServerToolType]
public sealed class ControlledTools(ToolExecution execution)
{
    [McpServerTool(Name = "test_noncooperative")]
    public Task<CallToolResult> Run(IProgress<ProgressNotificationValue> progress, CancellationToken token, string requestId) =>
        execution.Run("test_noncooperative", true, (context, cancellation) =>
        {
            using var registration = cancellation.Register(() => File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "cancelled"), "observed"));
            MessageLogger.Info("Entered controlled work " + context.Config.AzureArtifactsPat);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "entered"), "running");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!File.Exists(Path.Combine(AppContext.BaseDirectory, "release")))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Test controller did not release work");
                Thread.Sleep(20);
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "finished"), "completed");
            MessageLogger.Info("Completed controlled work");
            return new { Finished = true };
        }, progress, token, requestId);
}
