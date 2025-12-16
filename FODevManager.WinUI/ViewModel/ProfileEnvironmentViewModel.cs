using FODevManager.Models;
using System.ComponentModel;

namespace FODevManager.WinUI.ViewModel
{
    public sealed class ProfileEnvironmentViewModel : INotifyPropertyChanged
    {
        public string ModelName { get; set; } = string.Empty;
        public string ModelRootFolder { get; set; } = string.Empty;
        public string ProjectFilePath { get; set; } = string.Empty;
        public string MetadataFolder { get; set; } = string.Empty;
        public bool IsDeployed { get; set; }

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

        public bool IsCompiled => ModelType == ModelType.Compiled;
        public bool IsSource => ModelType == ModelType.Source;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static class ProfileEnvironmentViewModelExtensions
    {
        public static ProfileEnvironmentViewModel ToViewModel(this ProfileEnvironmentModel model)
        {
            return new ProfileEnvironmentViewModel
            {
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
