using System.ComponentModel;
using FODevManager.Operations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FODevManager.Mcp;

[McpServerToolType]
public sealed class EnvironmentTools(ToolExecution execution)
{
    [McpServerTool(Name = "deployment_inspect", ReadOnly = true), Description("Inspect live deployment targets, ownership verification and ledger records without repairing saved state")]
    public Task<CallToolResult> Inspect(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? modelName = null, string? requestId = null) =>
        execution.Run("deployment_inspect", false, (c, _) => new DeploymentOperations(c).Inspect(profileName, modelName), progress, token, requestId);

    [McpServerTool(Name = "deployment_deploy", Destructive = true), Description("Deploy one model or the entire profile using source/compiled symlinks; controls W3SVC and verifies live ownership")]
    public Task<CallToolResult> Deploy(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? modelName = null, string? requestId = null) =>
        execution.Run("deployment_deploy", true, (c, ct) => new DeploymentOperations(c).Deploy(profileName, modelName, ct), progress, token, requestId);

    [McpServerTool(Name = "deployment_undeploy", Destructive = true), Description("Validate the entire selected model/profile set then remove verified owned links with service control")]
    public Task<CallToolResult> Undeploy(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? modelName = null, string? requestId = null) =>
        execution.Run("deployment_undeploy", true, (c, ct) => new DeploymentOperations(c).Undeploy(profileName, modelName, ct), progress, token, requestId);

    [McpServerTool(Name = "deployment_undeploy_all", Destructive = true), Description("Validate deployments across all saved profiles before undeploying their verified owned links; controls services")]
    public Task<CallToolResult> UndeployAll(IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("deployment_undeploy_all", true, (c, ct) => new DeploymentOperations(c).UndeployAll(ct), progress, token, requestId);

    [McpServerTool(Name = "solution_path", ReadOnly = true), Description("Resolve saved profile solution location")]
    public Task<CallToolResult> SolutionPath(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("solution_path", false, (c, _) => new EnvironmentOperations(c).SolutionPath(profileName), progress, token, requestId);

    [McpServerTool(Name = "solution_ensure", Destructive = true), Description("Create/update solution and include all source model projects; return solution artifact path")]
    public Task<CallToolResult> EnsureSolution(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("solution_ensure", true, (c, ct) => new EnvironmentOperations(c).EnsureSolution(profileName, ct), progress, token, requestId);

    [McpServerTool(Name = "solution_open", Destructive = false), Description("Open the resolved existing solution in Visual Studio")]
    public Task<CallToolResult> OpenSolution(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("solution_open", true, (c, _) => new EnvironmentOperations(c).OpenSolution(profileName), progress, token, requestId);

    [McpServerTool(Name = "database_get", ReadOnly = true), Description("Read saved profile database name")]
    public Task<CallToolResult> Database(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("database_get", false, (c, _) => new EnvironmentOperations(c).Database(profileName), progress, token, requestId);

    [McpServerTool(Name = "database_set", Destructive = true), Description("Set saved database; active profiles also apply it to web configuration with service control")]
    public Task<CallToolResult> SetDatabase(string profileName, string databaseName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("database_set", true, (c, _) => new EnvironmentOperations(c).SetDatabase(profileName, databaseName), progress, token, requestId);

    [McpServerTool(Name = "database_apply", Destructive = true), Description("Apply the profile database to the local environment web configuration; controls services")]
    public Task<CallToolResult> ApplyDatabase(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("database_apply", true, (c, _) => new EnvironmentOperations(c).ApplyDatabase(profileName), progress, token, requestId);
}
