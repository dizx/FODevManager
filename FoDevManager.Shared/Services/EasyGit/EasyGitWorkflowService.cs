using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace FODevManager.Services.EasyGit
{
    public sealed class EasyGitWorkflowService : IEasyGitWorkflowService
    {
        private readonly FileService _fileService;
        private readonly AppConfig _config;
        private readonly IAiGitAssistant _aiGitAssistant;
        private readonly IAzureDevOpsPrService _azureDevOpsPrService;

        public EasyGitWorkflowService(
            FileService fileService,
            AppConfig config,
            IAiGitAssistant aiGitAssistant,
            IAzureDevOpsPrService azureDevOpsPrService)
        {
            _fileService = fileService;
            _config = config;
            _aiGitAssistant = aiGitAssistant;
            _azureDevOpsPrService = azureDevOpsPrService;
        }

        public IReadOnlyCollection<EasyGitRepoStatus> GetRepositoryStatuses(string profileName, bool includeMainUpdateCheck = true)
        {
            var profile = _fileService.LoadProfile(profileName);
            var statuses = new List<EasyGitRepoStatus>();
            var profileChanged = false;

            foreach (var repository in profile.Repositories ?? new List<RepositoryModel>())
            {
                var originalRepoId = repository.RepoId;
                var originalDisplayName = repository.DisplayName;

                repository.EnsureRepoId();
                repository.EnsureDisplayName();

                if (!string.Equals(originalRepoId, repository.RepoId, StringComparison.Ordinal)
                    || !string.Equals(originalDisplayName, repository.DisplayName, StringComparison.Ordinal))
                {
                    profileChanged = true;
                }

                var repoPath = (repository.RepoRootFolder ?? string.Empty).Trim();
                if (repoPath.IsNullOrEmpty() || !Directory.Exists(repoPath) || !GitHelper.IsGitRepository(repoPath))
                    continue;

                var branch = GitHelper.GetActiveBranch(repoPath) ?? string.Empty;

                var branchChanged = !repository.LastKnownBranch.SameAs(branch);
                if (branchChanged)
                {
                    repository.LastKnownBranch = branch;
                    profileChanged = true;
                }

                var workflowStage = ResolveWorkflowStage(repository, branch);
                var workflowStageString = workflowStage.ToString();
                if (!repository.WorkflowStage.SameAs(workflowStageString))
                {
                    repository.WorkflowStage = workflowStageString;
                    profileChanged = true;
                }

                var changeCounts = GitHelper.GetWorkingTreeChangeCounts(repoPath);

                var status = new EasyGitRepoStatus
                {
                    Repository = repository,
                    Branch = branch,
                    IsDirty = changeCounts.Total > 0,
                    AddedCount = changeCounts.Added,
                    DeletedCount = changeCounts.Deleted,
                    ModifiedCount = changeCounts.Modified,
                    NeedsAttention = GitHelper.RequiresAttention(repoPath),
                    HasMainUpdates = includeMainUpdateCheck
                        ? GitHelper.HasMainChangesWithoutFetch(repoPath, repository.MainBranchName)
                        : false,
                    IsProtectedBranch = GitHelper.IsProtectedBranch(branch, _config.ProtectedBranches),
                    WorkflowStage = workflowStage,
                    WorkflowText = BuildWorkflowText(workflowStage),
                    PullRequestUrl = repository.PullRequestUrl,
                    PullRequestId = repository.PullRequestId
                };

                statuses.Add(status);
            }

            if (profileChanged)
                _fileService.SaveProfile(profile);

            return statuses;
        }

        public async Task<EasyGitOperationResult> AutoSyncRepositoryAsync(string profileName, string repoId, CancellationToken cancellationToken = default)
        {
            var (profile, repository, error) = LoadRepository(profileName, repoId);
            if (!error.IsNullOrEmpty())
                return EasyGitOperationResult.Fail(error);

            if (!await GitHelper.FetchAllAsync(repository.RepoRootFolder, cancellationToken).ConfigureAwait(false))
                return EasyGitOperationResult.Fail($"Fetch failed for '{repository.DisplayName}'.");

            repository.LastKnownBranch = GitHelper.GetActiveBranch(repository.RepoRootFolder) ?? repository.LastKnownBranch;
            _fileService.SaveProfile(profile);

            return EasyGitOperationResult.Success($"Repository '{repository.DisplayName}' synchronized.");
        }

        public async Task<EasyGitOperationResult> CreateFeatureBranchAsync(string profileName, string repoId, string taskId, string comment, CancellationToken cancellationToken = default)
        {
            var (profile, repository, error) = LoadRepository(profileName, repoId);
            if (!error.IsNullOrEmpty())
                return EasyGitOperationResult.Fail(error);

            var repoPath = repository.RepoRootFolder;
            var mainBranch = repository.MainBranchName.IsNullOrEmpty() ? "main" : repository.MainBranchName;

            if (!await GitHelper.FetchAllAsync(repoPath, cancellationToken).ConfigureAwait(false))
                MessageLogger.Warning($"Fetch failed for '{repository.DisplayName}'. Continuing with local refs.");

            if (!GitHelper.ChangeBranch(repoPath, mainBranch, autoStashIfDirty: true, $"EasyGit auto-stash before creating feature branch", createIfMissing: false))
            {
                return EasyGitOperationResult.Fail($"Could not switch to '{mainBranch}' before creating feature branch.");
            }

            GitHelper.Pull(repoPath, "origin", mainBranch);

            var branchPrefix = taskId.IsNullOrEmpty() ? "feature/task" : $"feature/task-{taskId.Trim()}";
            var branchSuffix = Slugify(comment);
            var branchName = branchSuffix.IsNullOrEmpty() ? branchPrefix : $"{branchPrefix}-{branchSuffix}";

            if (!GitHelper.ChangeBranch(repoPath, branchName, autoStashIfDirty: true, $"EasyGit auto-stash before switching to {branchName}", createIfMissing: true))
                return EasyGitOperationResult.Fail($"Could not create/switch to feature branch '{branchName}'.");

            repository.Task = taskId ?? string.Empty;
            repository.TaskComment = comment ?? string.Empty;
            repository.LastKnownBranch = GitHelper.GetActiveBranch(repoPath) ?? branchName;
            repository.FeatureBranchName = branchName;
            repository.WorkflowStage = EasyGitWorkflowStage.Created.ToString();
            repository.PullRequestUrl = null;
            repository.PullRequestId = null;
            _fileService.SaveProfile(profile);

            return EasyGitOperationResult.Success($"Switched to '{branchName}'.");
        }

        public async Task<EasyGitOperationResult> CommitAsync(string profileName, string repoId, CancellationToken cancellationToken = default)
        {
            var (profile, repository, error) = LoadRepository(profileName, repoId);
            if (!error.IsNullOrEmpty())
                return EasyGitOperationResult.Fail(error);

            var repoPath = repository.RepoRootFolder;
            var branch = GitHelper.GetActiveBranch(repoPath) ?? string.Empty;

            if (GitHelper.IsProtectedBranch(branch, _config.ProtectedBranches))
                return EasyGitOperationResult.Fail($"Commit is blocked on protected branch '{branch}'.");

            if (!GitHelper.StageAll(repoPath))
                return EasyGitOperationResult.Fail("Failed to stage changes.");

            if (!GitHelper.HasStagedChanges(repoPath))
                return EasyGitOperationResult.Fail("No staged changes to commit.");

            var stagedDiff = GitHelper.GetStagedDiff(repoPath);
            var aiCommit = await _aiGitAssistant.GenerateCommitMessageAsync(stagedDiff, cancellationToken).ConfigureAwait(false);
            var commitMessage = aiCommit.Succeeded && !aiCommit.CommitMessage.IsNullOrEmpty()
                ? aiCommit.CommitMessage
                : BuildFallbackCommitMessage(repository, branch);

            if (!GitHelper.Commit(repoPath, commitMessage))
                return EasyGitOperationResult.Fail("Git commit failed.");

            if (!GitHelper.PushCurrentBranch(repoPath, setUpstreamWhenMissing: true))
                return EasyGitOperationResult.Fail("Commit succeeded but push failed.");

            repository.LastKnownBranch = branch;
            repository.WorkflowStage = MaxWorkflowStage(repository.WorkflowStage, EasyGitWorkflowStage.Committed).ToString();
            _fileService.SaveProfile(profile);

            return EasyGitOperationResult.Success($"Committed and pushed: {commitMessage}");
        }

        public async Task<EasyGitOperationResult> CreatePullRequestAsync(string profileName, string repoId, CancellationToken cancellationToken = default)
        {
            var (profile, repository, error) = LoadRepository(profileName, repoId);
            if (!error.IsNullOrEmpty())
                return EasyGitOperationResult.Fail(error);

            var repoPath = repository.RepoRootFolder;
            var sourceBranch = GitHelper.GetActiveBranch(repoPath) ?? string.Empty;
            var targetBranch = repository.MainBranchName.IsNullOrEmpty() ? "main" : repository.MainBranchName;

            if (sourceBranch.IsNullOrEmpty())
                return EasyGitOperationResult.Fail("Unable to detect current branch.");

            if (GitHelper.IsProtectedBranch(sourceBranch, _config.ProtectedBranches))
                return EasyGitOperationResult.Fail($"Cannot create PR from protected branch '{sourceBranch}'.");

            if (!GitHelper.PushCurrentBranch(repoPath, setUpstreamWhenMissing: true))
                return EasyGitOperationResult.Fail("Push failed. Cannot create PR.");

            var title = repository.Task.IsNullOrEmpty()
                ? $"{sourceBranch} -> {targetBranch}"
                : $"Task {repository.Task}: {repository.TaskComment}";

            var description =
                $"Created by EasyGit.{Environment.NewLine}" +
                $"Source: {sourceBranch}{Environment.NewLine}" +
                $"Target: {targetBranch}{Environment.NewLine}" +
                $"Repository: {repository.DisplayName}";

            var operation = await _azureDevOpsPrService
                .CreatePullRequestAsync(repository, sourceBranch, targetBranch, title, description, cancellationToken)
                .ConfigureAwait(false);

            if (operation.Succeeded)
            {
                repository.LastKnownBranch = sourceBranch;
                repository.FeatureBranchName = sourceBranch;
                repository.WorkflowStage = EasyGitWorkflowStage.PullRequestCreated.ToString();
                repository.PullRequestUrl = operation.Url;
                repository.PullRequestId = TryExtractPullRequestId(operation.Url);
                _fileService.SaveProfile(profile);
            }

            return operation;
        }

        public Task<EasyGitOperationResult> OpenPullRequestAsync(string profileName, string repoId, CancellationToken cancellationToken = default)
        {
            var (_, repository, error) = LoadRepository(profileName, repoId);
            if (!error.IsNullOrEmpty())
                return Task.FromResult(EasyGitOperationResult.Fail(error));

            var prUrl = (repository.PullRequestUrl ?? string.Empty).Trim();
            if (prUrl.IsNullOrEmpty())
                return Task.FromResult(EasyGitOperationResult.Fail("No pull request URL is stored for this repository."));

            GitHelper.OpenBrowserUrl(prUrl);
            return Task.FromResult(EasyGitOperationResult.Success("Opened pull request in browser.", prUrl));
        }

        public async Task<EasyGitPullRequestState> GetPullRequestStateAsync(string profileName, string repoId, CancellationToken cancellationToken = default)
        {
            var (_, repository, error) = LoadRepository(profileName, repoId);
            if (!error.IsNullOrEmpty())
                return new EasyGitPullRequestState { CanVerify = false, IsMerged = false, Message = error };

            var pullRequestId = repository.PullRequestId ?? TryExtractPullRequestId(repository.PullRequestUrl);
            if (!pullRequestId.HasValue || pullRequestId.Value <= 0)
            {
                return new EasyGitPullRequestState
                {
                    CanVerify = false,
                    IsMerged = false,
                    Message = "Pull request ID is missing."
                };
            }

            return await _azureDevOpsPrService
                .GetPullRequestStateAsync(repository, pullRequestId.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task<EasyGitOperationResult> CompleteWorkflowAsync(
            string profileName,
            string repoId,
            bool allowWhenMergeCannotBeVerified,
            CancellationToken cancellationToken = default)
        {
            var (profile, repository, error) = LoadRepository(profileName, repoId);
            if (!error.IsNullOrEmpty())
                return EasyGitOperationResult.Fail(error);

            var pullRequestState = await GetPullRequestStateAsync(profileName, repoId, cancellationToken).ConfigureAwait(false);
            if (pullRequestState.CanVerify && !pullRequestState.IsMerged)
                return EasyGitOperationResult.Fail("Pull request is not completed yet.");

            if (!pullRequestState.CanVerify && !allowWhenMergeCannotBeVerified)
                return EasyGitOperationResult.Fail(pullRequestState.Message.IsNullOrEmpty() ? "Pull request merge status could not be verified." : pullRequestState.Message);

            var repoPath = repository.RepoRootFolder;
            var mainBranch = repository.MainBranchName.IsNullOrEmpty() ? "main" : repository.MainBranchName;
            var currentBranch = GitHelper.GetActiveBranch(repoPath) ?? string.Empty;
            var featureBranch = (repository.FeatureBranchName ?? currentBranch).Trim();

            if (!GitHelper.ChangeBranch(repoPath, mainBranch, autoStashIfDirty: true, $"EasyGit auto-stash before completing workflow", createIfMissing: false))
                return EasyGitOperationResult.Fail($"Could not switch to '{mainBranch}' to complete workflow.");

            if (!await GitHelper.FetchAllAsync(repoPath, cancellationToken).ConfigureAwait(false))
                MessageLogger.Warning($"Fetch failed for '{repository.DisplayName}'. Continuing completion.");

            GitHelper.Pull(repoPath, "origin", mainBranch);

            if (!featureBranch.IsNullOrEmpty() && !featureBranch.SameAs(mainBranch))
            {
                GitHelper.DeleteRemoteBranch(repoPath, featureBranch);
                if (!GitHelper.DeleteLocalBranch(repoPath, featureBranch, forceDelete: false))
                    GitHelper.DeleteLocalBranch(repoPath, featureBranch, forceDelete: true);
            }

            repository.LastKnownBranch = GitHelper.GetActiveBranch(repoPath) ?? mainBranch;
            ClearWorkflowMetadata(repository);
            _fileService.SaveProfile(profile);

            return EasyGitOperationResult.Success($"Completed workflow for '{repository.DisplayName}'. Switched to '{mainBranch}' and cleaned feature branch.");
        }

        public async Task<EasyGitOperationResult> ResetProfileWorkflowsAsync(string profileName, CancellationToken cancellationToken = default)
        {
            var profile = _fileService.LoadProfile(profileName);
            var repositories = profile.Repositories ?? new List<RepositoryModel>();
            var resetCount = 0;

            foreach (var repository in repositories)
            {
                var repoPath = (repository.RepoRootFolder ?? string.Empty).Trim();
                if (repoPath.IsNullOrEmpty() || !Directory.Exists(repoPath) || !GitHelper.IsGitRepository(repoPath))
                    continue;

                var mainBranch = repository.MainBranchName.IsNullOrEmpty() ? "main" : repository.MainBranchName;
                _ = GitHelper.ResetToMainAndUpdate(repoPath, mainBranch);
                repository.LastKnownBranch = GitHelper.GetActiveBranch(repoPath) ?? mainBranch;
                ClearWorkflowMetadata(repository);
                resetCount++;
            }

            _fileService.SaveProfile(profile);
            await Task.CompletedTask.ConfigureAwait(false);
            return EasyGitOperationResult.Success($"Reset workflows for {resetCount} repositories.");
        }

        public async Task<EasyGitOperationResult> SwitchBranchesForProfileAsync(string sourceProfileName, string targetProfileName, CancellationToken cancellationToken = default)
        {
            if (sourceProfileName.IsNullOrEmpty() || targetProfileName.IsNullOrEmpty() || sourceProfileName.SameAs(targetProfileName))
                return EasyGitOperationResult.Success("Branch sync skipped.");

            var sourceProfile = _fileService.LoadProfile(sourceProfileName);
            var targetProfile = _fileService.LoadProfile(targetProfileName);
            var sourceRepos = sourceProfile.Repositories ?? new List<RepositoryModel>();
            var targetRepos = targetProfile.Repositories ?? new List<RepositoryModel>();

            var sourceBranchByRepoKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sourceRepo in sourceRepos)
            {
                var sourceRepoPath = (sourceRepo.RepoRootFolder ?? string.Empty).Trim();
                if (sourceRepoPath.IsNullOrEmpty() || !Directory.Exists(sourceRepoPath) || !GitHelper.IsGitRepository(sourceRepoPath))
                    continue;

                var branch = GitHelper.GetActiveBranch(sourceRepoPath) ?? sourceRepo.LastKnownBranch ?? string.Empty;
                sourceRepo.LastKnownBranch = branch;

                var repoKey = sourceRepo.GetRepoKey();
                if (!repoKey.IsNullOrEmpty() && !branch.IsNullOrEmpty())
                    sourceBranchByRepoKey[repoKey] = branch;
            }

            var switched = 0;

            foreach (var targetRepo in targetRepos)
            {
                var targetRepoPath = (targetRepo.RepoRootFolder ?? string.Empty).Trim();
                if (targetRepoPath.IsNullOrEmpty() || !Directory.Exists(targetRepoPath) || !GitHelper.IsGitRepository(targetRepoPath))
                    continue;

                var repoKey = targetRepo.GetRepoKey();
                if (repoKey.IsNullOrEmpty() || !sourceBranchByRepoKey.TryGetValue(repoKey, out var sourceBranch))
                {
                    targetRepo.LastKnownBranch = GitHelper.GetActiveBranch(targetRepoPath) ?? targetRepo.LastKnownBranch;
                    continue;
                }

                var currentBranch = GitHelper.GetActiveBranch(targetRepoPath) ?? string.Empty;
                if (!currentBranch.SameAs(sourceBranch))
                {
                    var switchedOk = GitHelper.ChangeBranch(
                        targetRepoPath,
                        sourceBranch,
                        autoStashIfDirty: true,
                        stashMessage: $"EasyGit auto-stash before switching profile branch to {sourceBranch}",
                        createIfMissing: false);

                    if (switchedOk)
                        switched++;
                }

                targetRepo.LastKnownBranch = GitHelper.GetActiveBranch(targetRepoPath) ?? sourceBranch;
            }

            _fileService.SaveProfile(sourceProfile);
            _fileService.SaveProfile(targetProfile);

            await Task.CompletedTask.ConfigureAwait(false);
            return EasyGitOperationResult.Success($"Switched branches for {switched} shared repositories.");
        }

        public async Task<EasyGitOperationResult> MergeMainIntoFeatureAsync(string profileName, string repoId, CancellationToken cancellationToken = default)
        {
            var (_, repository, error) = LoadRepository(profileName, repoId);
            if (!error.IsNullOrEmpty())
                return EasyGitOperationResult.Fail(error);

            var repoPath = repository.RepoRootFolder;
            var currentBranch = GitHelper.GetActiveBranch(repoPath) ?? string.Empty;
            var mainBranch = repository.MainBranchName.IsNullOrEmpty() ? "main" : repository.MainBranchName;

            if (currentBranch.IsNullOrEmpty())
                return EasyGitOperationResult.Fail("Unable to detect current branch.");

            if (GitHelper.IsProtectedBranch(currentBranch, _config.ProtectedBranches))
                return EasyGitOperationResult.Fail("Already on protected branch. Merge into feature skipped.");

            await GitHelper.FetchAllAsync(repoPath, cancellationToken).ConfigureAwait(false);

            if (GitHelper.MergeMainIntoCurrentBranch(repoPath, mainBranch))
                return EasyGitOperationResult.Success($"Merged origin/{mainBranch} into {currentBranch}.");

            var unresolvedFiles = GitHelper.GetUnmergedFiles(repoPath);
            if (unresolvedFiles.Count == 0)
                return EasyGitOperationResult.Fail("Merge failed and no conflict files were detected.");

            var resolution = await TryResolveConflictsWithAiAsync(repoPath, currentBranch, mainBranch, unresolvedFiles, cancellationToken).ConfigureAwait(false);
            if (!resolution.Succeeded)
                return resolution;

            return EasyGitOperationResult.Success("Merge conflicts auto-resolved and committed.");
        }

        private async Task<EasyGitOperationResult> TryResolveConflictsWithAiAsync(
            string repoPath,
            string currentBranch,
            string mainBranch,
            IReadOnlyCollection<string> unresolvedFiles,
            CancellationToken cancellationToken)
        {
            var resolvedFiles = new List<string>();

            foreach (var relativePath in unresolvedFiles)
            {
                var fullPath = Path.Combine(repoPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                    return EasyGitOperationResult.Fail($"Conflict file not found: {relativePath}", requiresManualReview: true);

                var contentWithMarkers = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (!contentWithMarkers.Contains("<<<<<<<", StringComparison.Ordinal))
                    continue;

                var aiResolution = await _aiGitAssistant.ResolveConflictAsync(new AiConflictResolutionRequest
                {
                    RepositoryRootFolder = repoPath,
                    RelativeFilePath = relativePath,
                    CurrentBranch = currentBranch,
                    MainBranchName = mainBranch,
                    FileContentsWithMarkers = contentWithMarkers
                }, cancellationToken).ConfigureAwait(false);

                if (!aiResolution.Succeeded)
                {
                    return EasyGitOperationResult.Fail(
                        $"AI could not resolve '{relativePath}'. {aiResolution.FailureReason}",
                        requiresManualReview: true);
                }

                if (!aiResolution.IsHighConfidence)
                {
                    return EasyGitOperationResult.Fail(
                        $"AI confidence too low ({aiResolution.Confidence:0.00}) for '{relativePath}'.",
                        requiresManualReview: true);
                }

                await File.WriteAllTextAsync(fullPath, aiResolution.ResolvedContent, cancellationToken).ConfigureAwait(false);

                if (GitHelper.HasConflictMarkers(fullPath))
                    return EasyGitOperationResult.Fail($"Conflict markers remain in '{relativePath}'.", requiresManualReview: true);

                if (!ValidateFileSyntax(relativePath, aiResolution.ResolvedContent, out var validationError))
                    return EasyGitOperationResult.Fail($"Validation failed for '{relativePath}': {validationError}", requiresManualReview: true);

                resolvedFiles.Add(relativePath);
            }

            if (resolvedFiles.Count == 0)
                return EasyGitOperationResult.Fail("No conflict files were resolved.", requiresManualReview: true);

            if (!GitHelper.StageAll(repoPath))
                return EasyGitOperationResult.Fail("Failed to stage AI conflict resolution.", requiresManualReview: true);

            if (!GitHelper.Commit(repoPath, "merge: resolve main updates with AI"))
                return EasyGitOperationResult.Fail("Failed to commit AI conflict resolution.", requiresManualReview: true);

            return EasyGitOperationResult.Success("AI conflict resolution committed.");
        }

        private (ProfileModel Profile, RepositoryModel Repository, string Error) LoadRepository(string profileName, string repoId)
        {
            try
            {
                var profile = _fileService.LoadProfile(profileName);

                var repository = (profile.Repositories ?? new List<RepositoryModel>())
                    .FirstOrDefault(repo => repo.RepoId.SameAs(repoId));

                if (repository == null)
                    return (new ProfileModel(), new RepositoryModel(), $"Repository '{repoId}' not found in profile '{profileName}'.");

                repository.EnsureRepoId();
                repository.EnsureDisplayName();

                var repoPath = (repository.RepoRootFolder ?? string.Empty).Trim();
                if (repoPath.IsNullOrEmpty() || !Directory.Exists(repoPath))
                    return (new ProfileModel(), new RepositoryModel(), $"Repository path not found for '{repository.DisplayName}'.");

                if (!GitHelper.IsGitRepository(repoPath))
                    return (new ProfileModel(), new RepositoryModel(), $"'{repoPath}' is not a git repository.");

                return (profile, repository, string.Empty);
            }
            catch (Exception ex)
            {
                return (new ProfileModel(), new RepositoryModel(), ex.Message);
            }
        }

        private static string BuildFallbackCommitMessage(RepositoryModel repository, string branch)
        {
            if (!repository.Task.IsNullOrEmpty())
                return $"feat(task-{repository.Task}): {repository.TaskComment}";

            return $"chore({branch}): update changes";
        }

        private static EasyGitWorkflowStage ParseWorkflowStage(string? stage)
        {
            if (Enum.TryParse<EasyGitWorkflowStage>(stage ?? string.Empty, ignoreCase: true, out var parsed))
                return parsed;

            return EasyGitWorkflowStage.NotStarted;
        }

        private static EasyGitWorkflowStage MaxWorkflowStage(string? currentStage, EasyGitWorkflowStage nextStage)
        {
            var current = ParseWorkflowStage(currentStage);
            return current >= nextStage ? current : nextStage;
        }

        private static EasyGitWorkflowStage MaxWorkflowStage(EasyGitWorkflowStage currentStage, EasyGitWorkflowStage nextStage)
        {
            return currentStage >= nextStage ? currentStage : nextStage;
        }

        private static EasyGitWorkflowStage ResolveWorkflowStage(RepositoryModel repository, string currentBranch)
        {
            var stage = ParseWorkflowStage(repository.WorkflowStage);

            if (!repository.PullRequestUrl.IsNullOrEmpty())
                stage = MaxWorkflowStage(stage, EasyGitWorkflowStage.PullRequestCreated);
            else if (!repository.FeatureBranchName.IsNullOrEmpty() || (!currentBranch.IsNullOrEmpty() && currentBranch.StartsWith("feature/", StringComparison.OrdinalIgnoreCase)))
                stage = MaxWorkflowStage(stage, EasyGitWorkflowStage.Created);

            return stage;
        }

        private static string BuildWorkflowText(EasyGitWorkflowStage stage)
        {
            return stage switch
            {
                EasyGitWorkflowStage.Created => "Next: commit and push your current feature branch changes.",
                EasyGitWorkflowStage.Committed => "Next: create a pull request from your feature branch.",
                EasyGitWorkflowStage.PullRequestCreated => "Next: complete the workflow after the pull request is merged.",
                _ => "Next: create a feature branch to start the workflow."
            };
        }

        private static int? TryExtractPullRequestId(string? pullRequestUrl)
        {
            if (pullRequestUrl.IsNullOrEmpty())
                return null;

            var match = Regex.Match(pullRequestUrl, @"/pullrequest/(\d+)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            return int.TryParse(match.Groups[1].Value, out var id) ? id : null;
        }

        private static void ClearWorkflowMetadata(RepositoryModel repository)
        {
            repository.WorkflowStage = EasyGitWorkflowStage.NotStarted.ToString();
            repository.FeatureBranchName = null;
            repository.PullRequestUrl = null;
            repository.PullRequestId = null;
            repository.Task = string.Empty;
            repository.TaskComment = string.Empty;
        }

        private static string Slugify(string input)
        {
            if (input.IsNullOrEmpty())
                return string.Empty;

            var builder = new StringBuilder(input.Length);
            foreach (var c in input.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                    continue;
                }

                if (char.IsWhiteSpace(c) || c == '-' || c == '_')
                    builder.Append('-');
            }

            var slug = builder.ToString().Trim('-');
            return slug.Length > 72 ? slug.Substring(0, 72) : slug;
        }

        private static bool ValidateFileSyntax(string relativePath, string content, out string error)
        {
            error = string.Empty;
            var extension = Path.GetExtension(relativePath)?.ToLowerInvariant() ?? string.Empty;

            try
            {
                if (extension == ".json")
                {
                    JsonDocument.Parse(content);
                    return true;
                }

                if (extension == ".xml" || extension == ".config")
                {
                    var xmlDocument = new XmlDocument();
                    xmlDocument.LoadXml(content);
                    return true;
                }

                if (extension == ".cs")
                {
                    var braces = 0;
                    foreach (var character in content)
                    {
                        if (character == '{') braces++;
                        if (character == '}') braces--;
                    }

                    if (braces != 0)
                    {
                        error = "Brace balance check failed for C# file.";
                        return false;
                    }

                    return true;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
