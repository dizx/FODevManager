using FODevManager.Messages;
using FODevManager.Shared.Utils;
using FODevManager.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace FODevManager.WinUI.ViewModel
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly AppConfigWriter _configWriter;

        public SettingsViewModel(AppConfigWriter configWriter)
        {
            _configWriter = configWriter;
            var config = configWriter.GetConfig();

            _defaultSourceDirectory = config.DefaultSourceDirectory;
            _checkGitChangesBeforeSwitch = true; // or load from config if added
        }

        private string _defaultSourceDirectory;
        public string DefaultSourceDirectory
        {
            get => _defaultSourceDirectory;
            set
            {
                SetProperty(ref _defaultSourceDirectory, value);
                _configWriter.UpdateSetting("DefaultSourceDirectory", value);
            }
        }

        private bool _checkGitChangesBeforeSwitch;
        public bool CheckGitChangesBeforeSwitch
        {
            get => _checkGitChangesBeforeSwitch;
            set
            {
                SetProperty(ref _checkGitChangesBeforeSwitch, value);
                _configWriter.UpdateSetting("CheckGitChangesBeforeSwitch", value);
            }
        }
    }

}
