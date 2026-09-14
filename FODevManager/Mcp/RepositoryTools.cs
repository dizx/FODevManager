using System.ComponentModel;
using FODevManager.Operations;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FODevManager.Mcp;

[McpServerToolType]
public sealed class RepositoryTools(ToolExecution execution)
{
    [McpServerTool(Name = "repositories_list", ReadOnly = true), Description("List saved repositories in a profile")]
    public Task<CallToolResult> List(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("repositories_list", false, (c, _) => new RepositoryOperations(c).List(profileName), progress, token, requestId);

    [McpServerTool(Name = "repository_show", ReadOnly = true), Description("Show saved repository properties")]
    public Task<CallToolResult> Show(string profileName, string repoId, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("repository_show", false, (c, _) => new RepositoryOperations(c).Show(profileName, repoId), progress, token, requestId);

    [McpServerTool(Name = "repository_properties_update", Destructive = true), Description("Update desktop-editable repository properties; omitted values remain as saved")]
    public Task<CallToolResult> Update(string profileName, string repoId, IProgress<ProgressNotificationValue> progress, CancellationToken token,
        string? displayName = null, string? preferredBranch = null, bool? autoCheckout = null, bool? autoStash = null, string? task = null, string? taskComment = null, string? requestId = null) =>
        execution.Run("repository_properties_update", true, (c, _) => new RepositoryOperations(c).Update(profileName, repoId, displayName, preferredBranch, autoCheckout, autoStash, task, taskComment), progress, token, requestId);

    [McpServerTool(Name = "repository_status", ReadOnly = true), Description("Query live local Git branch, dirty state, upstream health, main updates and HEAD without fetching")]
    public Task<CallToolResult> Status(string profileName, string repoId, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.RunAsync("repository_status", false, async (c, ct) => await new RepositoryOperations(c).Status(profileName, repoId, ct), progress, token, requestId);

    [McpServerTool(Name = "git_fetch", Destructive = false), Description("Fetch/prune all remotes for a selected repository or all profile repositories; no interactive authentication")]
    public Task<CallToolResult> Fetch(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? repoId = null, string? requestId = null) =>
        execution.RunAsync("git_fetch", true, async (c, ct) => await new RepositoryOperations(c).Fetch(profileName, repoId, ct), progress, token, requestId);

    [McpServerTool(Name = "repository_remote", Destructive = false), Description("Return the repository remote URL and optionally open it in the system browser")]
    public Task<CallToolResult> Remote(string profileName, string repoId, bool open, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("repository_remote", open, (c, _) => new RepositoryOperations(c).Remote(profileName, repoId, open), progress, token, requestId);

    [McpServerTool(Name = "git_assign_task", Destructive = true), Description("Assign repository task with explicit branch, stash-dirty and branch-creation choices; branchName required when switching")]
    public Task<CallToolResult> AssignTask(string profileName, string repoId, string task, string comment, bool switchBranch, bool stashDirty, bool createBranch,
        IProgress<ProgressNotificationValue> progress, CancellationToken token, string? branchName = null, string? requestId = null) =>
        execution.Run("git_assign_task", true, (c, _) => new RepositoryOperations(c).AssignTask(profileName, repoId, task, comment, switchBranch, branchName, stashDirty, createBranch), progress, token, requestId);

    [McpServerTool(Name = "repository_task_url", Destructive = false), Description("Return assigned task URL and optionally open it in the system browser")]
    public Task<CallToolResult> TaskUrl(string profileName, string repoId, bool open, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("repository_task_url", open, (c, _) => new RepositoryOperations(c).TaskUrl(profileName, repoId, open), progress, token, requestId);

    [McpServerTool(Name = "git_merge_main", Destructive = true), Description("Merge configured main into the current branch; may leave merge conflicts requiring resolution")]
    public Task<CallToolResult> Merge(string profileName, string repoId, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("git_merge_main", true, (c, _) => new RepositoryOperations(c).MergeMain(profileName, repoId), progress, token, requestId);

    [McpServerTool(Name = "git_reset_profile", Destructive = true), Description("Stash dirty repositories, checkout configured main, fetch and pull for the entire profile")]
    public Task<CallToolResult> Reset(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("git_reset_profile", true, (c, _) => new RepositoryOperations(c).Reset(profileName), progress, token, requestId);

    [McpServerTool(Name = "git_tag_release", Destructive = true), Description("Bump changed source model revisions, commit descriptors, create release tags and PUSH tags; requires clean main/release branches")]
    public Task<CallToolResult> Release(string profileName, IProgress<ProgressNotificationValue> progress, CancellationToken token, string? requestId = null) =>
        execution.Run("git_tag_release", true, (c, _) => new RepositoryOperations(c).Release(profileName), progress, token, requestId);
}
