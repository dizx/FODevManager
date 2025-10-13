using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace FODevManager.WinUI.ViewModel
{
    public sealed class RepoGroupViewModel
    {
        public string DisplayName { get; }
        public string? Branch { get; }
        public string GitUrl { get; } // group key (non-empty)
        public ReadOnlyCollection<ProfileEnvironmentViewModel> Models { get; }

        public bool HasPeriTask => Models.Any(m => m.HasPeriTask);
        public string? FirstPeriTask => Models.FirstOrDefault(m => m.HasPeriTask)?.PeriTask;

        public string? FirstModelRoot => Models.FirstOrDefault()?.ModelRootFolder;

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
