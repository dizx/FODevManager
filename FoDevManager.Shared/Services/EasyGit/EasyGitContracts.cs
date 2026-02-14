using FODevManager.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FODevManager.Services.EasyGit
{
    public sealed class EasyGitRepoStatus
    {
        public RepositoryModel Repository { get; init; } = new();
        public string Branch { get; init; } = string.Empty;
        public bool IsDirty { get; init; }
        public bool NeedsAttention { get; init; }
        public bool HasMainUpdates { get; init; }
        public bool IsProtectedBranch { get; init; }
    }

    public sealed class EasyGitOperationResult
    {
        public bool Succeeded { get; init; }
        public bool RequiresManualReview { get; init; }
        public string Message { get; init; } = string.Empty;
        public string? Url { get; init; }

        public static EasyGitOperationResult Success(string message, string? url = null)
            => new() { Succeeded = true, Message = message, Url = url };

        public static EasyGitOperationResult Fail(string message, bool requiresManualReview = false)
            => new() { Succeeded = false, Message = message, RequiresManualReview = requiresManualReview };
    }

    public sealed class AiCommitSuggestionResult
    {
        public bool Succeeded { get; init; }
        public string CommitMessage { get; init; } = string.Empty;
        public string? FailureReason { get; init; }
    }

    public sealed class AiConflictResolutionRequest
    {
        public string RepositoryRootFolder { get; init; } = string.Empty;
        public string RelativeFilePath { get; init; } = string.Empty;
        public string CurrentBranch { get; init; } = string.Empty;
        public string MainBranchName { get; init; } = "main";
        public string FileContentsWithMarkers { get; init; } = string.Empty;
    }

    public sealed class AiConflictResolutionResult
    {
        public bool Succeeded { get; init; }
        public bool IsHighConfidence { get; init; }
        public double Confidence { get; init; }
        public string ResolvedContent { get; init; } = string.Empty;
        public string? FailureReason { get; init; }
    }

    public interface IAiGitAssistant
    {
        Task<AiCommitSuggestionResult> GenerateCommitMessageAsync(string diffText, CancellationToken cancellationToken = default);
        Task<AiConflictResolutionResult> ResolveConflictAsync(AiConflictResolutionRequest request, CancellationToken cancellationToken = default);
    }

    public interface IAzureDevOpsPrService
    {
        Task<EasyGitOperationResult> CreatePullRequestAsync(
            RepositoryModel repository,
            string sourceBranch,
            string targetBranch,
            string title,
            string description,
            CancellationToken cancellationToken = default);
    }

    public interface IEasyGitWorkflowService
    {
        IReadOnlyCollection<EasyGitRepoStatus> GetRepositoryStatuses(string profileName);
        Task<EasyGitOperationResult> AutoSyncRepositoryAsync(string profileName, string repoId, CancellationToken cancellationToken = default);
        Task<EasyGitOperationResult> CreateFeatureBranchAsync(string profileName, string repoId, string taskId, string comment, CancellationToken cancellationToken = default);
        Task<EasyGitOperationResult> CommitAsync(string profileName, string repoId, CancellationToken cancellationToken = default);
        Task<EasyGitOperationResult> CreatePullRequestAsync(string profileName, string repoId, CancellationToken cancellationToken = default);
        Task<EasyGitOperationResult> MergeMainIntoFeatureAsync(string profileName, string repoId, CancellationToken cancellationToken = default);
    }
}
