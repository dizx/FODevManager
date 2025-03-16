using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FODevManager.Services;
using FODevManager.Models;
using System.Collections.Generic;
using System.Linq;
using System;

namespace FODevManager.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly ProfileService _profileService;
        private readonly ModelDeploymentService _deploymentService;

        public MainWindow(ProfileService profileService, ModelDeploymentService deploymentService)
        {
            this.InitializeComponent();
            _profileService = profileService;
            _deploymentService = deploymentService;
            LoadProfiles();
        }

        private void LoadProfiles()
        {
            ProfilesDropdown.ItemsSource = _profileService.GetAllProfiles();
        }

        private void ProfilesDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                ModelsListView.ItemsSource = _profileService.GetModelsInProfile(profileName);
            }
        }

        private void UpdateStatus(string message)
        {
            StatusBar.Text = message;
        }

        private async void CreateProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Create Profile",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var inputTextBox = new TextBox { PlaceholderText = "Enter profile name" };
            dialog.Content = inputTextBox;

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                _profileService.CreateProfile(inputTextBox.Text);
                LoadProfiles();
            }
        }


        private void DeployProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                UpdateStatus($"Deploying profile '{profileName}'...");
                _deploymentService.DeployAllUndeployedModels(profileName);
                UpdateStatus($"✅ Deployment complete for '{profileName}'.");
            }
        }

        private void UnDeployProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                _deploymentService.UnDeployAllModels(profileName);
            }
        }

        private void AddModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName && !string.IsNullOrWhiteSpace(ModelNameTextBox.Text))
            {
                _profileService.AddEnvironment(profileName, ModelNameTextBox.Text, "C:\\Path\\to\\model.rnrproj");
                ModelsListView.ItemsSource = _profileService.GetModelsInProfile(profileName);
            }
        }

        private void RemoveModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName && sender is Button button && button.Tag is string modelName)
            {
                _profileService.RemoveModelFromProfile(profileName, modelName);
                ModelsListView.ItemsSource = _profileService.GetModelsInProfile(profileName);
            }
        }
    }
}
