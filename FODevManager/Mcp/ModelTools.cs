using System.ComponentModel;
using FODevManager.Operations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FODevManager.Mcp;

[McpServerToolType]
public sealed class ModelTools(ToolExecution execution)
{
    [McpServerTool(Name = "models_list", ReadOnly = true), Description("List saved source, compiled and NuGet models across repositories and standalone models")]
    public Task<CallToolResult> List(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("models_list", false, (c, _) => new ModelOperations(c).List(profileName), progress, token, requestId);

    [McpServerTool(Name = "model_show", ReadOnly = true), Description("Show saved model properties and paths")]
    public Task<CallToolResult> Show(string profileName, string modelName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("model_show", false, (c, _) => new ModelOperations(c).Show(profileName, modelName), progress, token, requestId);

    [McpServerTool(Name = "models_add_path", Destructive = true), Description("Preflight all discovered source/compiled models and add existing paths to the profile/solution. Physical installed conversion requires exact path/name agreement and a new output destination; verifies the copy before deleting the original. Foreign/unmanaged links are refused")]
    public Task<CallToolResult> Add(string profileName, string path, string modelName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("models_add_path", true, (c, _) => new ModelOperations(c).Add(profileName, path, modelName), progress, token, requestId);

    [McpServerTool(Name = "model_create", Destructive = false), Description("Create a source model/project in a new destination and include it in the profile solution; existing destinations are rejected, use models_add_path for existing models")]
    public Task<CallToolResult> Create(string profileName, string modelName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("model_create", true, (c, _) => new ModelOperations(c).Create(profileName, modelName), progress, token, requestId);

    [McpServerTool(Name = "model_remove", Destructive = true), Description("Remove model membership and solution project after verified undeployment; NuGet models remove all package members and reconcile the repository")]
    public Task<CallToolResult> Remove(string profileName, string modelName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("model_remove", true, (c, ct) => new ModelOperations(c).Remove(profileName, modelName, ct), progress, token, requestId);

    [McpServerTool(Name = "model_properties_update", Destructive = true), Description("Update a source model's main FO solution-root role; selecting it clears the role on other models")]
    public Task<CallToolResult> Properties(string profileName, string modelName, bool isMainFoModel, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("model_properties_update", true, (c, _) => new ModelOperations(c).UpdateProperties(profileName, modelName, isMainFoModel), progress, token, requestId);

    [McpServerTool(Name = "model_version", ReadOnly = true), Description("Read live source/compiled model version or saved NuGet version")]
    public Task<CallToolResult> Version(string profileName, string modelName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("model_version", false, (c, _) => new ModelOperations(c).Version(profileName, modelName), progress, token, requestId);

    [McpServerTool(Name = "model_version_update", Destructive = true), Description("Update source descriptor version using non-negative major/minor/revision components")]
    public Task<CallToolResult> UpdateVersion(string profileName, string modelName, int major, int minor, int revision, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("model_version_update", true, (c, _) => new ModelOperations(c).UpdateVersion(profileName, modelName, major, minor, revision), progress, token, requestId);
}
