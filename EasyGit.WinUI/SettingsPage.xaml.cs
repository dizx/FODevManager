using EasyGit.WinUI.ViewModel;
using FODevManager.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace EasyGit.WinUI
{
    public sealed partial class SettingsPage : Page
    {
        private readonly SettingsViewModel _viewModel;

        public SettingsPage()
        {
            InitializeComponent();

            var writer = App.Services?.GetRequiredService<AppConfigWriter>()
                ?? throw new InvalidOperationException("App services are not initialized.");

            _viewModel = new SettingsViewModel(writer);
            DataContext = _viewModel;

            AzureDevOpsPatBox.Password = _viewModel.AzureDevOpsPat;
            AzureOpenAiApiKeyBox.Password = _viewModel.AzureOpenAiApiKey;
        }

        private void AzureDevOpsPatBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.AzureDevOpsPat = AzureDevOpsPatBox.Password;
        }

        private void AzureOpenAiApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.AzureOpenAiApiKey = AzureOpenAiApiKeyBox.Password;
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            _viewModel.Save();
        }
    }
}
