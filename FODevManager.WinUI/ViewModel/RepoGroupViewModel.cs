using FODevManager.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace FODevManager.WinUI.ViewModel
{
    public sealed class RepoGroupViewModel : INotifyPropertyChanged
    {
        public RepositoryModel Repository { get; }

        public string DisplayName { get; }

        public string? Branch => Repository.LastKnownBranch;

        public string? GitUrl => Repository.GitUrl;

        public string RepoRootFolder => Repository.RepoRootFolder;

        public string PeriTask => Repository.PeriTask;

        public string PeriTaskComment => Repository.PeriTaskComment;

        public bool HasPeriTask => !string.IsNullOrWhiteSpace(PeriTask);

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

        public RepoGroupViewModel(RepositoryModel repository, string displayName, ReadOnlyCollection<ProfileEnvironmentViewModel> models)
        {
            Repository = repository;
            DisplayName = displayName;
            Models = models;
        }
    }
}
