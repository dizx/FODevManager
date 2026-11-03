global using ModelContextProtocol;
using System.Text.Json;
using FODevManager.Operations;
using FODevManager.Utils;
using ModelContextProtocol.Protocol;

namespace FODevManager.Mcp;

public sealed class ToolExecution(OperationRunner runner, AppConfig config)
{
    public Task<CallToolResult> Run(string name, bool mutating, Func<WorkflowContext, CancellationToken, object?> action,
        IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId) =>
        RunAsync(name, mutating, (context, ct) => Task.FromResult(action(context, ct)), progress, token, requestId);

    public async Task<CallToolResult> RunAsync(string name, bool mutating, Func<WorkflowContext, CancellationToken, Task<object?>> action,
        IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId)
    {
        // Await the worker even when cancellation is requested; synchronous services retain the lease until they return
        var result = await Task.Run(() => runner.RunAsync(name, mutating, ct => action(new WorkflowContext(config), ct),
            token, requestId, new OperationProgress(progress)), CancellationToken.None).ConfigureAwait(false);
        return ToToolResult(result);
    }

    public static CallToolResult ToToolResult(OperationResult result) => Content(result, result.Status is "failed" or "cancelled" or "busy");

    public static CallToolResult Content(object data, bool error = false)
    {
        var json = JsonSerializer.SerializeToElement(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new CallToolResult { IsError = error, StructuredContent = json, Content = [new TextContentBlock { Text = json.GetRawText() }] };
    }

    private sealed class OperationProgress(IProgress<ProgressNotificationValue> target) : IProgress<string>
    {
        private int count;
        public void Report(string message) => target.Report(new ProgressNotificationValue { Progress = Interlocked.Increment(ref count), Message = message });
    }
}
