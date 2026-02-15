using System.ComponentModel;
using FODevManager.Services.EasyGit;

namespace EasyGit.WinUI
{
    public sealed class RepoRowViewModel : INotifyPropertyChanged
    {
        public string RepoId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;

        private string _branchInfo = string.Empty;
        public string BranchInfo
        {
            get => _branchInfo;
            set
            {
                if (_branchInfo == value)
                    return;

                _branchInfo = value;
                OnPropertyChanged(nameof(BranchInfo));
            }
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText == value)
                    return;

                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        private bool _isProtectedBranch;
        public bool IsProtectedBranch
        {
            get => _isProtectedBranch;
            set
            {
                if (_isProtectedBranch == value)
                    return;

                _isProtectedBranch = value;
                OnPropertyChanged(nameof(IsProtectedBranch));
                OnPropertyChanged(nameof(CanCreateFeature));
                OnPropertyChanged(nameof(CanCommit));
                OnPropertyChanged(nameof(CanCreatePr));
                OnPropertyChanged(nameof(CanComplete));
            }
        }

        private EasyGitWorkflowStage _workflowStage;
        public EasyGitWorkflowStage WorkflowStage
        {
            get => _workflowStage;
            set
            {
                if (_workflowStage == value)
                    return;

                _workflowStage = value;
                OnPropertyChanged(nameof(WorkflowStage));
                OnPropertyChanged(nameof(CanCreateFeature));
                OnPropertyChanged(nameof(CanCommit));
                OnPropertyChanged(nameof(CanCreatePr));
                OnPropertyChanged(nameof(CanComplete));
            }
        }

        private string _workflowText = string.Empty;
        public string WorkflowText
        {
            get => _workflowText;
            set
            {
                if (_workflowText == value)
                    return;

                _workflowText = value;
                OnPropertyChanged(nameof(WorkflowText));
            }
        }

        private int _addedCount;
        public int AddedCount
        {
            get => _addedCount;
            set
            {
                if (_addedCount == value)
                    return;

                _addedCount = value;
                OnPropertyChanged(nameof(AddedCount));
                OnPropertyChanged(nameof(AddedDisplay));
                OnPropertyChanged(nameof(HasChanges));
                OnPropertyChanged(nameof(CanCommit));
            }
        }

        private int _deletedCount;
        public int DeletedCount
        {
            get => _deletedCount;
            set
            {
                if (_deletedCount == value)
                    return;

                _deletedCount = value;
                OnPropertyChanged(nameof(DeletedCount));
                OnPropertyChanged(nameof(DeletedDisplay));
                OnPropertyChanged(nameof(HasChanges));
                OnPropertyChanged(nameof(CanCommit));
            }
        }

        private int _modifiedCount;
        public int ModifiedCount
        {
            get => _modifiedCount;
            set
            {
                if (_modifiedCount == value)
                    return;

                _modifiedCount = value;
                OnPropertyChanged(nameof(ModifiedCount));
                OnPropertyChanged(nameof(ModifiedDisplay));
                OnPropertyChanged(nameof(HasChanges));
                OnPropertyChanged(nameof(CanCommit));
            }
        }

        private string? _pullRequestUrl;
        public string? PullRequestUrl
        {
            get => _pullRequestUrl;
            set
            {
                if (_pullRequestUrl == value)
                    return;

                _pullRequestUrl = value;
                OnPropertyChanged(nameof(PullRequestUrl));
                OnPropertyChanged(nameof(HasPullRequest));
                OnPropertyChanged(nameof(CanViewPr));
            }
        }

        public bool HasPullRequest => !string.IsNullOrWhiteSpace(PullRequestUrl);
        public bool HasChanges => AddedCount + DeletedCount + ModifiedCount > 0;

        public string AddedDisplay => $"+ {AddedCount}";
        public string DeletedDisplay => $"- {DeletedCount}";
        public string ModifiedDisplay => ModifiedCount.ToString();

        public bool CanCreateFeature => !IsProtectedBranch && WorkflowStage == EasyGitWorkflowStage.NotStarted;
        public bool CanCommit => !IsProtectedBranch && WorkflowStage >= EasyGitWorkflowStage.Created && HasChanges;
        public bool CanCreatePr => !IsProtectedBranch && WorkflowStage >= EasyGitWorkflowStage.Created && WorkflowStage < EasyGitWorkflowStage.PullRequestCreated;
        public bool CanViewPr => !string.IsNullOrWhiteSpace(PullRequestUrl);
        public bool CanComplete => WorkflowStage >= EasyGitWorkflowStage.PullRequestCreated;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
