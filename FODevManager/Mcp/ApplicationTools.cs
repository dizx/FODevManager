using System.ComponentModel;
using FODevManager.Operations;
using FODevManager.Utils;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FODevManager.Mcp;

[McpServerToolType]
public sealed class ApplicationTools(ToolExecution execution, OperationRunner runner, SettingsOperations settings, AppConfig config)
{
    [McpServerTool(Name = "application_about", ReadOnly = true), Description("Return application version and MCP execution/history semantics")]
    public Task<CallToolResult> About(IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("application_about", false, (_, _) => new
        {
            Application = "FO Dev Manager", Version = typeof(McpHost).Assembly.GetName().Version?.ToString(),
            Environment = "Console", History = "Bounded in-memory history; lost on server restart",
            Cancellation = "Cooperative; synchronous work is awaited until it actually completes", Transactional = false
        }, progress, token, requestId);

    [McpServerTool(Name = "settings_read", ReadOnly = true), Description("Read effective non-secret settings; credential values are excluded")]
    public Task<CallToolResult> ReadSettings(IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("settings_read", false, (_, _) => settings.Read(), progress, token, requestId);

    [McpServerTool(Name = "settings_update", Destructive = true), Description("Update supported typed settings in installed appsettings.json; reload effective settings for subsequent operations. Environment overrides still take precedence. Credentials are input-only")]
    public Task<CallToolResult> UpdateSettings(SettingsUpdate settingsUpdate, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("settings_update", true, (_, _) => settings.Update(settingsUpdate), progress, token, requestId);

    [McpServerTool(Name = "operation_status", ReadOnly = true), Description("Query running/queued/completed operations WITHOUT waiting for serialization. Filter by operationId or caller requestId after reconnecting; request IDs correlate, not deduplicate. History is lost on restart")]
    public CallToolResult Status(string? operationId = null, string? requestId = null)
    {
        var redactor = new SecretRedactor(config);
        var matches = runner.ListOperations().Where(r => (operationId == null || r.OperationId == operationId)
            && (requestId == null || r.RequestId == redactor.Redact(requestId))).ToArray();
        // Listing summaries avoids duplicating every potentially large retained result into one protocol response
        object data = operationId != null
            ? new { Operations = matches.Select(r => r with
                { Name = redactor.Redact(r.Name), RequestId = r.RequestId is null ? null : redactor.Redact(r.RequestId),
                    Data = r.Data is null ? null : redactor.RedactData(r.Data), Diagnostics = r.Diagnostics.Select(redactor.Redact).ToArray() }).ToArray() }
            : new { Operations = matches.OrderByDescending(r => r.CreatedAt).Take(100).Select(r => new
                { r.OperationId, RequestId = r.RequestId is null ? null : redactor.Redact(r.RequestId), Name = redactor.Redact(r.Name),
                    r.Status, r.CreatedAt, r.StartedAt, r.CompletedAt, r.PartialChangesPossible }).ToArray(),
                TotalMatches = matches.Length, History = "Memory-only; lost on restart; newest 100 summaries; query operationId for full result" };
        return ToolExecution.Content(data,
            matches.Length == 0 && (operationId != null || requestId != null));
    }
}
