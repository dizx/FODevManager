using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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

                var status = new EasyGitRepoStatus
                {
                    Repository = repository,
                    Branch = branch,
                    IsDirty = GitHelper.IsWorkingTreeDirty(repoPath),
                    NeedsAttention = GitHelper.RequiresAttention(repoPath),
                    HasMainUpdates = includeMainUpdateCheck
                        ? GitHelper.HasMainChangesWithoutFetch(repoPath, repository.MainBranchName)
                        : false,
                    IsProtectedBranch = GitHelper.IsProtectedBranch(branch, _config.ProtectedBranches)
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
            _fileService.SaveProfile(profile);

            return EasyGitOperationResult.Success($"Committed and pushed: {commitMessage}");
        }

        public async Task<EasyGitOperationResult> CreatePullRequestAsync(string profileName, string repoId, CancellationToken cancellationToken = default)
        {
            var (_, repository, error) = LoadRepository(profileName, repoId);
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

            return await _azureDevOpsPrService
                .CreatePullRequestAsync(repository, sourceBranch, targetBranch, title, description, cancellationToken)
                .ConfigureAwait(false);
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
