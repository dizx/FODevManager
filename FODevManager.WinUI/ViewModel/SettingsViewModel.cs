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

        private bool _useEasyGit;
        public bool UseEasyGit
        {
            get => _useEasyGit;
            set => SetProperty(ref _useEasyGit, value);
        }

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

        public SettingsViewModel(AppConfigWriter configWriter)
        {
            _configWriter = configWriter ?? throw new ArgumentNullException(nameof(configWriter));

            // hydrate from the one-and-only AppConfig
            var cfg = _configWriter.GetConfig();
            _checkUncommittedBeforeSwitch = cfg.CheckUncommittedBeforeSwitch;
            _defaultSourceDirectory = cfg.DefaultSourceDirectory;
            _useEasyGit = cfg.UseEasyGit;
            _gitAutoSyncIntervalMinutes = cfg.GitAutoSyncIntervalMinutes;
            _protectedBranches = cfg.ProtectedBranches;
        }

        public void Save()
        {
            try
            {
                // Persist both settings to the SAME appsettings.json
                _configWriter.UpdateSetting(nameof(AppConfig.CheckUncommittedBeforeSwitch), CheckUncommittedBeforeSwitch);
                _configWriter.UpdateSetting(nameof(AppConfig.DefaultSourceDirectory), DefaultSourceDirectory);
                _configWriter.UpdateSetting(nameof(AppConfig.UseEasyGit), UseEasyGit);
                _configWriter.UpdateSetting(nameof(AppConfig.GitAutoSyncIntervalMinutes), GitAutoSyncIntervalMinutes);
                _configWriter.UpdateSetting(nameof(AppConfig.ProtectedBranches), ProtectedBranches);

                // keep in-memory AppConfig aligned, if writer exposes it via GetConfig()
                var cfg = _configWriter.GetConfig();
                cfg.CheckUncommittedBeforeSwitch = CheckUncommittedBeforeSwitch;
                cfg.DefaultSourceDirectory = DefaultSourceDirectory;
                cfg.UseEasyGit = UseEasyGit;
                cfg.GitAutoSyncIntervalMinutes = GitAutoSyncIntervalMinutes;
                cfg.ProtectedBranches = ProtectedBranches;

                MessageLogger.Highlight("Settings saved.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
