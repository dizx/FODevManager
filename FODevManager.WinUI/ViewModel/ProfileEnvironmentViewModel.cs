using FODevManager.Models;
using System;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace FODevManager.WinUI.ViewModel
{
    public sealed class ProfileEnvironmentViewModel : INotifyPropertyChanged
    {
        private static readonly Regex TrailingVersionPattern = new(@"(?:[-\s]+)?\d+\.\d+\.\d+(?:\.\d+)?$", RegexOptions.Compiled);

        public ProfileEnvironmentModel Model { get; set; }

        public string ProfileName { get; set; }

        public string ModelName { get; set; } = string.Empty;
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        private string _versionText = string.Empty;
        public string VersionText
        {
            get => _versionText;
            set
            {
                if (string.Equals(_versionText, value, StringComparison.Ordinal))
                    return;

                _versionText = value ?? string.Empty;
                OnPropertyChanged(nameof(VersionText));
                OnPropertyChanged(nameof(HasVersion));
                OnPropertyChanged(nameof(DisplayNameWithVersion));
            }
        }

        public bool HasVersion => !string.IsNullOrWhiteSpace(VersionText);
        public string DisplayName => BuildDisplayName();
        public string DisplayNameWithVersion
        {
            get
            {
                if (!HasVersion)
                    return DisplayName;

                var normalizedModelName = DisplayName;
                var normalizedVersion = VersionText.Trim();

                if (normalizedModelName.EndsWith(normalizedVersion, StringComparison.OrdinalIgnoreCase))
                    return normalizedModelName;

                if (TrailingVersionPattern.IsMatch(normalizedModelName))
                    return normalizedModelName;

                return $"{normalizedModelName} {normalizedVersion}".Trim();
            }
        }
        public string ModelRootFolder { get; set; } = string.Empty;
        public string ProjectFilePath { get; set; } = string.Empty;
        public string MetadataFolder { get; set; } = string.Empty;
        private bool _isDeployed;
        public bool IsDeployed
        {
            get => _isDeployed;
            set
            {
                if (_isDeployed == value)
                    return;

                _isDeployed = value;
                OnPropertyChanged(nameof(IsDeployed));
            }
        }

        private ModelType _modelType = ModelType.Source;
        public ModelType ModelType
        {
            get => _modelType;
            set
            {
                if (_modelType == value)
                    return;

                _modelType = value;
                OnPropertyChanged(nameof(ModelType));
                OnPropertyChanged(nameof(IsCompiled));
                OnPropertyChanged(nameof(IsSource));
            }
        }

        public bool IsCompiled => ModelType == ModelType.Compiled || ModelType == ModelType.CompiledNuget;
        public bool IsSource => ModelType == ModelType.Source;

        public event PropertyChangedEventHandler? PropertyChanged;

        private string BuildDisplayName()
        {
            var normalizedModelName = (ModelName ?? string.Empty).Trim();
            if (normalizedModelName.Length == 0)
                return string.Empty;

            if (ModelType != ModelType.CompiledNuget)
                return normalizedModelName;

            return TrailingVersionPattern.Replace(normalizedModelName, string.Empty).TrimEnd('-', ' ');
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static class ProfileEnvironmentViewModelExtensions
    {
        public static ProfileEnvironmentViewModel ToViewModel(this ProfileEnvironmentModel model, string profileName)
        {
            return new ProfileEnvironmentViewModel
            {
                ProfileName = profileName,
                Model = model,
                ModelName = model.ModelName,
                ModelRootFolder = model.ModelRootFolder,
                IsDeployed = model.IsDeployed,
                ProjectFilePath = model.ProjectFilePath,
                MetadataFolder = model.MetadataFolder,
                ModelType = model.ModelType
            };
        }
    }
}
