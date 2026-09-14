using System.ComponentModel;
using FODevManager.Operations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FODevManager.Mcp;

[McpServerToolType]
public sealed class ProfileTools(ToolExecution execution)
{
    [McpServerTool(Name = "profiles_list", ReadOnly = true), Description("List saved profile names using fresh disk state")]
    public Task<CallToolResult> List(IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("profiles_list", false, (c, _) => new ProfileOperations(c).List(), progress, token, requestId);

    [McpServerTool(Name = "profile_show", ReadOnly = true), Description("Show a saved profile; deployment and Git flags are saved state")]
    public Task<CallToolResult> Show(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("profile_show", false, (c, _) => new ProfileOperations(c).Show(profileName), progress, token, requestId);

    [McpServerTool(Name = "profile_active", ReadOnly = true), Description("Return saved active profile names")]
    public Task<CallToolResult> Active(IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("profile_active", false, (c, _) => new ProfileOperations(c).Active(), progress, token, requestId);

    [McpServerTool(Name = "profile_create", Destructive = false), Description("Create a profile and solution and mark it active, following application creation behavior")]
    public Task<CallToolResult> Create(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("profile_create", true, (c, _) => new ProfileOperations(c).Create(profileName), progress, token, requestId);

    [McpServerTool(Name = "profile_delete", Destructive = true), Description("Validate all owned deployment links, undeploy with service control, then delete the saved profile")]
    public Task<CallToolResult> Delete(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("profile_delete", true, (c, ct) => new ProfileOperations(c).Delete(profileName, ct), progress, token, requestId);

    [McpServerTool(Name = "profile_import_file", Destructive = true), Description("Import a profile file, cloning repositories and preparing models/solution; may overwrite an existing target profile")]
    public Task<CallToolResult> ImportFile(string path, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? targetProfileName = null, string? requestId = null) =>
        execution.Run("profile_import_file", true, (c, ct) => new ProfileOperations(c).ImportFile(path, targetProfileName, ct), progress, token, requestId);

    [McpServerTool(Name = "profile_import_repository", Destructive = true), Description("Clone/import a repository URL containing a profile definition and prepare its models and solution")]
    public Task<CallToolResult> ImportRepository(string repositoryUrl, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("profile_import_repository", true, (c, ct) => new ProfileOperations(c).ImportRepository(repositoryUrl, ct), progress, token, requestId);

    [McpServerTool(Name = "profile_export", Destructive = false), Description("Export a portable profile to an explicit new file; return its artifact path")]
    public Task<CallToolResult> Export(string profileName, string path, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("profile_export", true, (c, _) => new ProfileOperations(c).Export(profileName, path), progress, token, requestId);

    [McpServerTool(Name = "profile_refresh", Destructive = false), Description("Refresh live deployment ownership and Git state locally while preserving model/package membership and linked definitions; use nuget_prepare to reconcile dependencies")]
    public Task<CallToolResult> Refresh(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("profile_refresh", true, (c, _) => new ProfileOperations(c).Refresh(profileName), progress, token, requestId);

    [McpServerTool(Name = "profile_switch", Destructive = true), Description("Switch active environment: validate ownership, undeploy, checkout/stash according to repository preferences, restore/deploy, apply database and control services")]
    public Task<CallToolResult> Switch(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("profile_switch", true, (c, ct) => new ProfileOperations(c).Switch(profileName, ct), progress, token, requestId);

    [McpServerTool(Name = "profile_sync_check", Destructive = false), Description("Check linked definition changes/revision; may bootstrap a missing external export or migrate legacy export")]
    public Task<CallToolResult> SyncCheck(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.RunAsync("profile_sync_check", true, async (c, _) => await new ProfileOperations(c).CheckSync(profileName), progress, token, requestId);

    [McpServerTool(Name = "profile_sync_dismiss", Destructive = false), Description("Dismiss a specific linked definition revision only if it is still current")]
    public Task<CallToolResult> SyncDismiss(string profileName, string revision, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.RunAsync("profile_sync_dismiss", true, async (c, _) => await new ProfileOperations(c).DismissSync(profileName, revision), progress, token, requestId);

    [McpServerTool(Name = "profile_sync_reimport", Destructive = true), Description("Undeploy verified owned links and reimport the linked definition, preserving local profile name and database")]
    public Task<CallToolResult> SyncReimport(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("profile_sync_reimport", true, (c, ct) => new ProfileOperations(c).Reimport(profileName, ct), progress, token, requestId);
}
