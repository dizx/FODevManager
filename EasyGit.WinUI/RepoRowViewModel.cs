using System.ComponentModel;

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
                OnPropertyChanged(nameof(CanCommit));
                OnPropertyChanged(nameof(CanCreatePr));
            }
        }

        public bool CanCommit => !IsProtectedBranch;
        public bool CanCreatePr => !IsProtectedBranch;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
