using FODevManager.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.WinUI.ViewModel
{
    public class ProfileEnvironmentViewModel : INotifyPropertyChanged
    {
        public string ModelName { get; set; } = "";

        public string ModelRootFolder { get; set; } = "";

        public string ProjectFilePath { get; set; } = "";

        public string MetadataFolder { get; set; } = "";

        public string GitUrl { get; set; } = "";

        public string PeriTask { get; set; } = "";

        public bool HasPeriTask => !string.IsNullOrWhiteSpace(PeriTask);

        public bool HasGit => !string.IsNullOrWhiteSpace(GitUrl);

        public string GitBranch { get; set; } = "";

        public bool IsDeployed { get; set; } = false;

        private ModelType _modelType = ModelType.Source;
        public ModelType ModelType
        {
            get => _modelType;
            set
            {
                if (_modelType == value) return;
                _modelType = value;
                OnPropertyChanged(nameof(ModelType));
                OnPropertyChanged(nameof(IsCompiled));
                OnPropertyChanged(nameof(IsSource));
            }
        }

        public bool IsCompiled => ModelType == ModelType.Compiled;
        public bool IsSource => ModelType == ModelType.Source;

        
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class ProfileEnvironmentViewModelExtensions
    {
        public static ProfileEnvironmentViewModel ToViewModel(this Models.ProfileEnvironmentModel model, string gitBranch)
        {
            var viewModel = new ProfileEnvironmentViewModel();
            viewModel.ModelName = model.ModelName;
            viewModel.IsDeployed = model.IsDeployed;
            viewModel.ProjectFilePath = model.ProjectFilePath;
            viewModel.MetadataFolder = model.MetadataFolder;
            viewModel.GitUrl = model.GitUrl;
            viewModel.PeriTask = model.PeriTask;
            viewModel.GitBranch = gitBranch;
            viewModel.ModelType = model.ModelType;
            return viewModel;
        }
    }
}
