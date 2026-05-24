using FODevManager.Models;
using FODevManager.Shared.Models;
using FODevManager.Utils;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace FODevManager.WinUI.ViewModel
{
    public sealed class RepoGroupViewModel : INotifyPropertyChanged
    {
        public string ProfileName { get; set; }
        public RepositoryModel Repository { get; }

        public string DisplayName { get; }

        private string? _branch;

        public string? Branch
        {
            get => _branch;
            set
            {
                if (_branch.SameAs(value))
                    return;

                _branch = value;
                OnPropertyChanged(nameof(Branch));
            }
        }

        private RepoBranchHealth _branchHealth;

        public RepoBranchHealth BranchHealth
        {
            get => _branchHealth;
            set
            {
                if (_branchHealth == value)
                    return;

                _branchHealth = value;
                OnPropertyChanged(nameof(BranchHealth));
            }
        }


        public string? GitUrl => Repository.GitUrl;

        public string RepoRootFolder => Repository.RepoRootFolder;

        public string Task => Repository.Task;

        public string TaskComment => Repository.TaskComment;

        public bool HasTask => !Task.IsNullOrEmpty();

        private bool _hasMainUpdates;

        public bool HasMainUpdates
        {
            get => _hasMainUpdates;
            set
            {
                if (_hasMainUpdates == value)
                    return;

                _hasMainUpdates = value;
                OnPropertyChanged(nameof(HasMainUpdates));
            }
        }

        public ReadOnlyCollection<ProfileEnvironmentViewModel> Models { get; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;

                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(IsExpandedVisibility));
                OnPropertyChanged(nameof(ChevronGlyph));
            }
        }

        public Microsoft.UI.Xaml.Visibility IsExpandedVisibility
            => IsExpanded ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public string ChevronGlyph
            => IsExpanded ? "\uE70D" /*chevron down*/ : "\uE76C" /*chevron right*/;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public RepoGroupViewModel(RepositoryModel repository, string displayName, string profileName, ReadOnlyCollection<ProfileEnvironmentViewModel> models)
        {
            ProfileName = profileName;
            Repository = repository;
            Branch = repository.LastKnownBranch;
            DisplayName = displayName;
            Models = models;
        }
    }
}
