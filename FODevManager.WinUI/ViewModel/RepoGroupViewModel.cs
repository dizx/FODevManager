using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace FODevManager.WinUI.ViewModel
{
    public sealed class RepoGroupViewModel : INotifyPropertyChanged
    {
        public string DisplayName { get; }
        public string? Branch { get; }
        public string GitUrl { get; } // group key (non-empty)
        public ReadOnlyCollection<ProfileEnvironmentViewModel> Models { get; }

        public bool HasPeriTask => Models.Any(m => m.HasPeriTask);
        public string? FirstPeriTask => Models.FirstOrDefault(m => m.HasPeriTask)?.PeriTask;

        public string? FirstModelRoot => Models.FirstOrDefault()?.ModelRootFolder;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(IsExpandedVisibility));
                OnPropertyChanged(nameof(ChevronGlyph));
            }
        }

        public Microsoft.UI.Xaml.Visibility IsExpandedVisibility
                => IsExpanded ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public string ChevronGlyph => IsExpanded ? "\uE70D" /*chevron down*/ : "\uE76C" /*chevron right*/;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));


        public RepoGroupViewModel(string gitUrl, string displayName, string? branch,
                                  ReadOnlyCollection<ProfileEnvironmentViewModel> models)
        {
            GitUrl = gitUrl;
            DisplayName = displayName;
            Branch = branch;
            Models = models;
        }
    }
}
