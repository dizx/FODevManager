using FODevManager.Messages;
using FODevManager.Operations;
using FODevManager.Shared.Utils; // AppConfigWriter
using FODevManager.Utils;
using System;

namespace FODevManager.WinUI.ViewModel
{
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly AppConfigWriter _configWriter;
        private SettingsUpdate _savedValues;

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

        private string _nuGetExecutablePath = string.Empty;
        public string NuGetExecutablePath
        {
            get => _nuGetExecutablePath;
            set => SetProperty(ref _nuGetExecutablePath, value ?? string.Empty);
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
            _nuGetExecutablePath = cfg.NuGetExecutablePath;
            _pushDeployablePackageOnBuild = cfg.PushDeployablePackageOnBuild;
            _pushDeployablePackageSource = cfg.PushDeployablePackageSource;
            _savedValues = Snapshot();
        }

        private SettingsUpdate Snapshot() => new()
        {
            CheckUncommittedBeforeSwitch = CheckUncommittedBeforeSwitch,
            DefaultSourceDirectory = DefaultSourceDirectory,
            AzureArtifactsUsername = AzureArtifactsUsername,
            AzureArtifactsPat = AzureArtifactsPat,
            AzureArtifactsApiKey = AzureArtifactsApiKey,
            NuGetExecutablePath = NuGetExecutablePath,
            PushDeployablePackageOnBuild = PushDeployablePackageOnBuild,
            PushDeployablePackageSource = PushDeployablePackageSource
        };

        public void Save()
        {
            try
            {
                var edited = Snapshot();
                var edits = new PropertyEdits<SettingsUpdate>(_savedValues, edited,
                    nameof(AppConfig.CheckUncommittedBeforeSwitch), nameof(AppConfig.DefaultSourceDirectory),
                    nameof(AppConfig.AzureArtifactsUsername), nameof(AppConfig.AzureArtifactsPat), nameof(AppConfig.AzureArtifactsApiKey),
                    nameof(AppConfig.NuGetExecutablePath), nameof(AppConfig.PushDeployablePackageOnBuild), nameof(AppConfig.PushDeployablePackageSource));
                new HostOperationBoundary().Run("Save settings", () =>
                {
                    App.ReloadConfiguration();
                    edits.Write((key, value) => _configWriter.UpdateSetting(key, value!));
                    App.ReloadConfiguration();
                    return true;
                });
                _savedValues = edited;

                MessageLogger.Highlight("Settings saved");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to save settings: {ex.Message}");
            }
        }
    }
}

