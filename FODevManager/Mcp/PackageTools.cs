using System.ComponentModel;
using FODevManager.Operations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FODevManager.Mcp;

[McpServerToolType]
public sealed class PackageTools(ToolExecution execution)
{
    [McpServerTool(Name = "nuget_versions", ReadOnly = true), Description("List package versions from the selected repository's configured NuGet feed")]
    public Task<CallToolResult> Versions(string profileName, string repoId, string packageId, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("nuget_versions", false, (c, _) => new PackageOperations(c).Versions(profileName, repoId, packageId), progress, token, requestId);

    [McpServerTool(Name = "nuget_add", Destructive = true), Description("Add package URL to a selected repository's Build/isv.config and restore/reconcile compiled models; temporarily undeploy verified links and restore surviving deployments")]
    public Task<CallToolResult> Add(string profileName, string repoId, string packageUrl, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("nuget_add", true, (c, ct) => new PackageOperations(c).Add(profileName, repoId, packageUrl, ct), progress, token, requestId);

    [McpServerTool(Name = "nuget_update", Destructive = true), Description("Update repository package version and reconcile all compiled package membership with verified deployment handling")]
    public Task<CallToolResult> Update(string profileName, string repoId, string packageId, string version, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("nuget_update", true, (c, ct) => new PackageOperations(c).Update(profileName, repoId, packageId, version, ct), progress, token, requestId);

    [McpServerTool(Name = "nuget_remove", Destructive = true), Description("Remove a repository package reference and ALL package models after validating the full repository synchronization/deployment set")]
    public Task<CallToolResult> Remove(string profileName, string repoId, string packageId, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("nuget_remove", true, (c, ct) => new PackageOperations(c).Remove(profileName, repoId, packageId, ct), progress, token, requestId);

    [McpServerTool(Name = "nuget_prepare", Destructive = true), Description("Restore and reconcile all repositories' compiled NuGet dependencies, retaining verified deployments for surviving models")]
    public Task<CallToolResult> Prepare(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("nuget_prepare", true, (c, ct) => new PackageOperations(c).Prepare(profileName, ct), progress, token, requestId);

    [McpServerTool(Name = "package_build", Destructive = true), Description("Build source or package compiled payload. Explicit publish=true pushes using configured feed/credentials; false builds locally. Returns actual generated package/nuspec paths")]
    public Task<CallToolResult> Build(string profileName, string modelName, bool publish, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("package_build", true, (c, ct) => new PackageOperations(c).Build(profileName, modelName, publish, ct), progress, token, requestId);
}
