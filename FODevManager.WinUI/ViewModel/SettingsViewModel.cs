using FODevManager.Messages;
using FODevManager.Shared.Utils; // AppConfigWriter
using FODevManager.Utils;
using System;

namespace FODevManager.WinUI.ViewModel
{
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly AppConfigWriter _configWriter;

        private bool _checkUncommittedBeforeSwitch = true;
        public bool CheckUncommittedBeforeSwitch
        {
            get => _checkUncommittedBeforeSwitch;
            set => SetProperty(ref _checkUncommittedBeforeSwitch, value);
        }

        private string _defaultSourceDirectory = string.Empty;
        public string DefaultSourceDirectory
        {
            get => _defaultSourceDirectory;
            set => SetProperty(ref _defaultSourceDirectory, value ?? string.Empty);
        }

        private string _azureArtifactsUsername = string.Empty;
        public string AzureArtifactsUsername
        {
            get => _azureArtifactsUsername;
            set => SetProperty(ref _azureArtifactsUsername, value ?? string.Empty);
        }

        private string _azureArtifactsPat = string.Empty;
        public string AzureArtifactsPat
        {
            get => _azureArtifactsPat;
            set => SetProperty(ref _azureArtifactsPat, value ?? string.Empty);
        }

        private string _azureArtifactsApiKey = string.Empty;
        public string AzureArtifactsApiKey
        {
            get => _azureArtifactsApiKey;
            set => SetProperty(ref _azureArtifactsApiKey, value ?? string.Empty);
        }

        private bool _pushDeployablePackageOnBuild;
        public bool PushDeployablePackageOnBuild
        {
            get => _pushDeployablePackageOnBuild;
            set => SetProperty(ref _pushDeployablePackageOnBuild, value);
        }

        private string _pushDeployablePackageSource = string.Empty;
        public string PushDeployablePackageSource
        {
            get => _pushDeployablePackageSource;
            set => SetProperty(ref _pushDeployablePackageSource, value ?? string.Empty);
        }

        public SettingsViewModel(AppConfigWriter configWriter)
        {
            _configWriter = configWriter ?? throw new ArgumentNullException(nameof(configWriter));

            // hydrate from the one-and-only AppConfig
            var cfg = _configWriter.GetConfig();
            _checkUncommittedBeforeSwitch = cfg.CheckUncommittedBeforeSwitch;
            _defaultSourceDirectory = cfg.DefaultSourceDirectory;
            _azureArtifactsUsername = cfg.AzureArtifactsUsername;
            _azureArtifactsPat = cfg.AzureArtifactsPat;
            _azureArtifactsApiKey = cfg.AzureArtifactsApiKey;
            _pushDeployablePackageOnBuild = cfg.PushDeployablePackageOnBuild;
            _pushDeployablePackageSource = cfg.PushDeployablePackageSource;
        }

        public void Save()
        {
            try
            {
                // Persist both settings to the SAME appsettings.json
                _configWriter.UpdateSetting(nameof(AppConfig.CheckUncommittedBeforeSwitch), CheckUncommittedBeforeSwitch);
                _configWriter.UpdateSetting(nameof(AppConfig.DefaultSourceDirectory), DefaultSourceDirectory);
                _configWriter.UpdateSetting(nameof(AppConfig.AzureArtifactsUsername), AzureArtifactsUsername);
                _configWriter.UpdateSetting(nameof(AppConfig.AzureArtifactsPat), AzureArtifactsPat);
                _configWriter.UpdateSetting(nameof(AppConfig.AzureArtifactsApiKey), AzureArtifactsApiKey);
                _configWriter.UpdateSetting(nameof(AppConfig.PushDeployablePackageOnBuild), PushDeployablePackageOnBuild);
                _configWriter.UpdateSetting(nameof(AppConfig.PushDeployablePackageSource), PushDeployablePackageSource);

                // keep in-memory AppConfig aligned, if writer exposes it via GetConfig()
                var cfg = _configWriter.GetConfig();
                cfg.CheckUncommittedBeforeSwitch = CheckUncommittedBeforeSwitch;
                cfg.DefaultSourceDirectory = DefaultSourceDirectory;
                cfg.AzureArtifactsUsername = AzureArtifactsUsername;
                cfg.AzureArtifactsPat = AzureArtifactsPat;
                cfg.AzureArtifactsApiKey = AzureArtifactsApiKey;
                cfg.PushDeployablePackageOnBuild = PushDeployablePackageOnBuild;
                cfg.PushDeployablePackageSource = PushDeployablePackageSource;

                MessageLogger.Highlight("Settings saved");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to save settings: {ex.Message}");
            }
        }
    }
}

