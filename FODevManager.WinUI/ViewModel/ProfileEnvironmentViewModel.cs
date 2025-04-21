using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.WinUI.ViewModel
{
    public class ProfileEnvironmentViewModel
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
            return viewModel;
        }
    }
}
