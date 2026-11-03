using FODevManager.Models;
using FODevManager.Utils;

namespace FODevManager.Operations;

public sealed class RepositoryOperations(WorkflowContext context)
{
    public async Task<bool> CheckMainUpdates(string profileName, string repoId, CancellationToken token)
    {
        var repo = WorkflowContext.Repository(context.Profile(profileName), repoId);
        token.ThrowIfCancellationRequested();
        var fetched = await GitHelper.FetchAllAsync(repo.RepoRootFolder, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        WorkflowContext.Require(fetched, "Fetch before checking main updates");
        return await GitHelper.HasMainChangesAsync(repo.RepoRootFolder, repo.MainBranchName, token,
            fetchUpdates: false, throwOnFailure: true).ConfigureAwait(false);
    }

    public object List(string profileName) => context.Profile(profileName).Repositories;
    public RepositoryModel Show(string profileName, string repoId) => WorkflowContext.Repository(context.Profile(profileName), repoId);

    public object Update(string profileName, string repoId, string? displayName, string? preferredBranch,
        bool? autoCheckout, bool? autoStash, string? task, string? taskComment)
    {
        var repo = Show(profileName, repoId);
        if (displayName != null) repo.DisplayName = displayName.Trim();
        if (preferredBranch != null) repo.PreferredBranch = preferredBranch.Trim();
        if (autoCheckout.HasValue) repo.AutoCheckoutOnProfileLoad = autoCheckout.Value;
        if (autoStash.HasValue) repo.AutoStashOnDirtyCheckout = autoStash.Value;
        if (task != null) repo.Task = task.Trim();
        if (taskComment != null) repo.TaskComment = taskComment.Trim();
        context.Profiles.UpdateRepositoryProperties(profileName, repo);
        return Show(profileName, repoId);
    }

    private RepositoryModel GitRepository(string profileName, string repoId)
    {
        var repo = Show(profileName, repoId);
        WorkflowContext.Require(GitHelper.IsGitRepository(repo.RepoRootFolder), "Resolve Git repository");
        return repo;
    }

    public async Task<object> Status(string profileName, string repoId, CancellationToken token)
    {
        var repo = GitRepository(profileName, repoId);
        return new { repo.RepoId, Branch = await GitHelper.InspectAsync(repo.RepoRootFolder, "rev-parse --abbrev-ref HEAD", token),
            Dirty = await GitHelper.IsWorkingTreeDirtyStrictAsync(repo.RepoRootFolder, token),
            Health = await GitHelper.GetBranchHealthStrictAsync(repo.RepoRootFolder, token),
            HasMainUpdates = await GitHelper.HasMainChangesAsync(repo.RepoRootFolder, repo.MainBranchName, token, fetchUpdates: false, throwOnFailure: true),
            Commit = await GitHelper.InspectAsync(repo.RepoRootFolder, "rev-parse HEAD", token) };
    }

    public async Task<object> Fetch(string profileName, string? repoId, CancellationToken token)
    {
        var repos = repoId is null ? context.Profile(profileName).Repositories : [GitRepository(profileName, repoId)];
        foreach (var repo in repos)
        {
            token.ThrowIfCancellationRequested();
            WorkflowContext.Require(await GitHelper.FetchAllAsync(repo.RepoRootFolder, token), "Fetch repository");
        }
        return new { Repositories = repos.Select(r => r.RepoId).ToArray() };
    }

    public object Remote(string profileName, string repoId, bool open)
    {
        var repo = Show(profileName, repoId);
        if (open) GitHelper.OpenGitRemoteUrl(GitRepository(profileName, repoId).RepoRootFolder);
        return new { Url = repo.GitUrl };
    }

    public object AssignTask(string profileName, string repoId, string task, string comment, bool switchBranch,
        string? branchName, bool stashDirty, bool createBranch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(task);
        var profile = context.Profile(profileName);
        var repo = WorkflowContext.Repository(profile, repoId);
        if (switchBranch)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
            if (branchName.StartsWith('-') || branchName.Any(c => char.IsWhiteSpace(c) || c is '"' or '\''))
                throw new ArgumentException("Branch name contains unsupported characters");
            GitRepository(profileName, repoId);
            GitHelper.IsWorkingTreeDirtyStrict(repo.RepoRootFolder);
            WorkflowContext.Require(GitHelper.ChangeBranch(repo.RepoRootFolder, branchName, stashDirty,
                $"FO Dev Manager: task {task}", createBranch), "Switch task branch");
            repo.LastKnownBranch = GitHelper.GetActiveBranch(repo.RepoRootFolder);
        }
        repo.Task = task;
        repo.TaskComment = comment;
        context.Files.SaveProfile(profile);
        return repo;
    }

    public object TaskUrl(string profileName, string repoId, bool open)
    {
        var repo = Show(profileName, repoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo.Task);
        var url = context.Config.TaskUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(repo.Task);
        if (open) ServiceHelper.OpenUrl(url);
        return new { Url = url };
    }

    public object MergeMain(string profileName, string repoId)
    {
        var repo = GitRepository(profileName, repoId);
        WorkflowContext.Require(GitHelper.MergeMainIntoCurrentBranch(repo.RepoRootFolder, repo.MainBranchName), "Merge main");
        return new { repo.RepoId, Branch = GitHelper.GetActiveBranch(repo.RepoRootFolder) };
    }

    public object Reset(string profileName)
    {
        var profile = context.Profile(profileName);
        foreach (var repo in profile.Repositories) GitHelper.IsWorkingTreeDirtyStrict(repo.RepoRootFolder);
        WorkflowContext.Require(context.Profiles.GitResetProfile(profile), "Reset profile to main");
        return List(profileName);
    }

    public object Release(string profileName)
    {
        var profile = context.Profile(profileName);
        foreach (var repo in profile.Repositories)
            WorkflowContext.Require(!GitHelper.IsWorkingTreeDirtyStrict(repo.RepoRootFolder), "Check uncommitted changes before release");
        WorkflowContext.Require(context.Profiles.TagReleaseProfile(profile), "Tag release");
        return profile.Repositories.Select(r => new { r.RepoId, Tags = GitHelper.GetTags(r.RepoRootFolder) }).ToArray();
    }
}
