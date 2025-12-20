using FODevManager.Messages;
using FODevManager.Services;
using FODevManager.Shared.Utils; 
using FODevManager.WinUI.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FODevManager.WinUI
{
    public sealed partial class SettingsPage : Page
    {
        private readonly SettingsViewModel _vm;
            
        public SettingsPage()
        {
            InitializeComponent();

            var writer = App.Services.GetRequiredService<AppConfigWriter>();
            _vm = new SettingsViewModel(writer);

            DataContext = _vm; // Page supports DataContext in WinUI 3
        }

        private async void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");

            var hWnd = WindowNative.GetWindowHandle(App.MainWindow); // ✅ correct
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
