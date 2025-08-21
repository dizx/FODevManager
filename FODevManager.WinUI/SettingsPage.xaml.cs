using FODevManager.Messages;
using FODevManager.WinUI.ViewModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Pickers;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FODevManager.WinUI
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private readonly SettingsViewModel _vm;


        public SettingsPage()
        {
            InitializeComponent();

            _vm = new SettingsViewModel(new SettingsService());
            DataContext = _vm;
        }

        private async void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            var hWnd = WindowNative.GetWindowHandle(App.MainWindow);
            InitializeWithWindow.Initialize(picker, hWnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                _vm.DefaultSourceDirectory = folder.Path;
                MessageLogger.Highlight($"Default source set to: {folder.Path}");
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            _vm.Save();
        }
    }
}
