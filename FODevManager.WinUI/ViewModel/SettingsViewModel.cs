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

        public SettingsViewModel(AppConfigWriter configWriter)
        {
            _configWriter = configWriter ?? throw new ArgumentNullException(nameof(configWriter));

            // hydrate from the one-and-only AppConfig
            var cfg = _configWriter.GetConfig();
            _checkUncommittedBeforeSwitch = cfg.CheckUncommittedBeforeSwitch;
            _defaultSourceDirectory = cfg.DefaultSourceDirectory;
        }

        public void Save()
        {
            try
            {
                // Persist both settings to the SAME appsettings.json
                _configWriter.UpdateSetting(nameof(AppConfig.CheckUncommittedBeforeSwitch), CheckUncommittedBeforeSwitch);
                _configWriter.UpdateSetting(nameof(AppConfig.DefaultSourceDirectory), DefaultSourceDirectory);

                // keep in-memory AppConfig aligned, if writer exposes it via GetConfig()
                var cfg = _configWriter.GetConfig();
                cfg.CheckUncommittedBeforeSwitch = CheckUncommittedBeforeSwitch;
                cfg.DefaultSourceDirectory = DefaultSourceDirectory;

                MessageLogger.Highlight("Settings saved.");
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Failed to save settings: {ex.Message}");
            }
        }
    }
}
