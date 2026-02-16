using FODevManager.Messages;
using FODevManager.Shared.Utils;
using FODevManager.Utils;
using System;

namespace EasyGit.WinUI.ViewModel
{
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly AppConfigWriter _configWriter;

        private int _gitAutoSyncIntervalMinutes = 5;
        public int GitAutoSyncIntervalMinutes
        {
            get => _gitAutoSyncIntervalMinutes;
            set => SetProperty(ref _gitAutoSyncIntervalMinutes, value < 1 ? 1 : value);
        }

        private string _protectedBranches = "main,master";
        public string ProtectedBranches
        {
            get => _protectedBranches;
            set => SetProperty(ref _protectedBranches, value ?? "main,master");
        }

        private string _azureDevOpsOrganizationUrl = string.Empty;
        public string AzureDevOpsOrganizationUrl
        {
            get => _azureDevOpsOrganizationUrl;
            set => SetProperty(ref _azureDevOpsOrganizationUrl, value ?? string.Empty);
        }

        private string _azureDevOpsPat = string.Empty;
        public string AzureDevOpsPat
        {
            get => _azureDevOpsPat;
            set => SetProperty(ref _azureDevOpsPat, value ?? string.Empty);
        }

        private string _azureOpenAiEndpoint = string.Empty;
        public string AzureOpenAiEndpoint
        {
            get => _azureOpenAiEndpoint;
            set => SetProperty(ref _azureOpenAiEndpoint, value ?? string.Empty);
        }

        private string _azureOpenAiDeployment = string.Empty;
        public string AzureOpenAiDeployment
        {
            get => _azureOpenAiDeployment;
            set => SetProperty(ref _azureOpenAiDeployment, value ?? string.Empty);
        }

        private string _azureOpenAiApiKey = string.Empty;
        public string AzureOpenAiApiKey
        {
            get => _azureOpenAiApiKey;
            set => SetProperty(ref _azureOpenAiApiKey, value ?? string.Empty);
        }

        public SettingsViewModel(AppConfigWriter configWriter)
        {
            _configWriter = configWriter ?? throw new ArgumentNullException(nameof(configWriter));

            var config = _configWriter.GetConfig();
            _gitAutoSyncIntervalMinutes = config.GitAutoSyncIntervalMinutes;
            _protectedBranches = config.ProtectedBranches;
            _azureDevOpsOrganizationUrl = config.AzureDevOpsOrganizationUrl;
            _azureDevOpsPat = config.AzureDevOpsPat;
            _azureOpenAiEndpoint = config.AzureOpenAiEndpoint;
            _azureOpenAiDeployment = config.AzureOpenAiDeployment;
            _azureOpenAiApiKey = config.AzureOpenAiApiKey;
        }

        public void Save()
        {
            try
            {
                _configWriter.UpdateSetting(nameof(AppConfig.GitAutoSyncIntervalMinutes), GitAutoSyncIntervalMinutes);
                _configWriter.UpdateSetting(nameof(AppConfig.ProtectedBranches), ProtectedBranches);
                _configWriter.UpdateSetting(nameof(AppConfig.AzureDevOpsOrganizationUrl), AzureDevOpsOrganizationUrl);
                _configWriter.UpdateSetting(nameof(AppConfig.AzureDevOpsPat), AzureDevOpsPat);
                _configWriter.UpdateSetting(nameof(AppConfig.AzureOpenAiEndpoint), AzureOpenAiEndpoint);
                _configWriter.UpdateSetting(nameof(AppConfig.AzureOpenAiDeployment), AzureOpenAiDeployment);
                _configWriter.UpdateSetting(nameof(AppConfig.AzureOpenAiApiKey), AzureOpenAiApiKey);

                MessageLogger.Highlight("EasyGit options saved.");
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"Failed to save options: {exception.Message}");
            }
        }
    }
}
