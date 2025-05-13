using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FODevManager.Services;
using FODevManager.Models;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using WinRT;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using FODevManager.Messages;
using FODevManager.Shared.Utils;
using FODevManager.Utils;
using System.Reflection;
using FODevManager.WinUI.ViewModel;
using Microsoft.UI.Text;
using Windows.UI.Text;
using System.IO;
using FODevManager.Logging;
using Serilog;


namespace FODevManager.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly UIMessageSubscriber _uiSubscriber = new();

        private readonly ProfileService _profileService;
        private readonly FileService _fileService;
        private readonly ModelDeploymentService _deploymentService;

        private MicaController? _micaController;
        private SystemBackdropConfiguration? _backdropConfig;
        private AppWindow _appWindow;

        public MainWindow(ProfileService profileService, FileService fileService, ModelDeploymentService deploymentService)
        {
            this.InitializeComponent();
            this.Activated += MainWindow_Activated;

            Singleton<Engine>.Instance.EnvironmentType = EnvironmentType.WinUi;

            _uiSubscriber = new UIMessageSubscriber();
            var serilogSubscriber = new SerilogSubscriber();

            LogPreviewList.ItemsSource = _uiSubscriber.RecentMessages;

            _profileService = profileService;
            _fileService = fileService;
            _deploymentService = deploymentService;

            // Initialize Mica + TitleBar
            ApplyMicaEffect();
            SetTitleBar(AppTitleBar);

            // Store AppWindow reference
            _appWindow = GetAppWindowForCurrentWindow();

            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;

            LoadProfiles();

            UIMessageHelper.LogToUI($"READY...");
        }
        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.SetIcon(@"Assets\FODev.ico");
        }

        private void ApplyMicaEffect()
        {
            if (!MicaController.IsSupported()) return;

            _backdropConfig = new SystemBackdropConfiguration
            {
                IsInputActive = true,
                Theme = SystemBackdropTheme.Default
            };

            _micaController = new MicaController();
            _micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            _micaController.SetSystemBackdropConfiguration(_backdropConfig);
        }

        private AppWindow GetAppWindowForCurrentWindow()
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }

        private void LoadProfiles()
        {
            var profiles = _fileService.GetAllProfiles();
            ProfilesDropdown.ItemsSource = profiles.Select(x => x.ProfileName).ToList();

            if (profiles.Any())
            {
                UIMessageHelper.LogToUI($"🔔 {profiles.Count} profiles loaded");

                var activeProfile = profiles.FirstOrDefault(x => x.IsActive) ?? profiles.First();

                if (activeProfile.IsActive)
                {
                    UIMessageHelper.LogToUI($"🔔 Active profile: {activeProfile.ProfileName} ");
                }

                if (!activeProfile.IsActive)
                {
                    UIMessageHelper.LogToUI($"No active profile", MessageType.Warning);
                }

                SetProfile(activeProfile);

            }
        }

        private ProfileModel? GetActiveProfile()
        {
            var profiles = GetAllProfiles();
            if (profiles.Any())
            {
                var activeProfile = profiles.FirstOrDefault(x => x.IsActive) ?? profiles.First();
                return activeProfile;
            }
            return null;
        }

        private void LoadProfile(string profileName)
        {
            SetProfile(LoadProfileByName(profileName));
        }

        private void SetProfile(ProfileModel? profile)
        {
            if (profile == null)
                return;

            ProfilesDropdown.SelectedItem = profile.ProfileName;
            LoadModelListViewData(profile.ProfileName);
            UpdateProfileFields(profile);
        }

        private void LoadModelListViewData(string profileName)
        {
            var profileEnvironmentViewModelList = new List<ProfileEnvironmentViewModel>();

            var models = GetModelsInProfile(profileName);

            foreach (var model in models)
            {
                var activeBranch = GetActiveGitBranch(profileName, model.ModelName);
                profileEnvironmentViewModelList.Add(model.ToViewModel(activeBranch ?? ""));
            }

            ModelsListView.ItemsSource = profileEnvironmentViewModelList;

        }

        private void ProfilesDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                LoadModelListViewData(profileName);

                var profile = _fileService.LoadProfile(profileName);
                if (profile != null)
                {
                    UpdateProfileFields(profile);
                }
            }
        }

        private void UpdateProfileFields(ProfileModel profile)
        {
            DatabaseNameTextBox.Text = profile.DatabaseName ?? string.Empty;
            IsActiveCheckBox.IsChecked = profile.IsActive;

        }

        private void OpenGit_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName)
                return;

            if (sender is Button button && button.Tag is string modelName)
            {
                var model = GetModel(profileName, modelName);
                if (model != null && IsGitRepo(profileName, modelName))
                {
                    OpenGitRepo(profileName, modelName);
                }
                else
                {
                    UIMessageHelper.LogToUI($"Git repository not found for model '{modelName}'.", MessageType.Warning);
                }
            }
        }

        private void DeployModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName)
                return;

            if (sender is Button button && button.Tag is string modelName)
            {
                DeployModel(profileName, modelName);
                LoadModelListViewData(profileName);
                UIMessageHelper.LogToUI($"🚀 Deployed model '{modelName}'");
            }
        }

        private void UnDeployModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName)
                return;

            if (sender is Button button && button.Tag is string modelName)
            {
                UnDeployModel(profileName, modelName);
                LoadModelListViewData(profileName);
                UIMessageHelper.LogToUI($"🧯 Undeployed model '{modelName}'");
            }
        }

        private void RefreshGitButton()
        {
            foreach (var item in ModelsListView.Items)
            {
                var container = ModelsListView.ContainerFromItem(item) as ListViewItem;
                if (container == null)
                    continue;

                var gitButton = FindVisualChild<Button>(container, "GitButton");

                if (item is ProfileEnvironmentViewModel model && gitButton != null)
                {
                    gitButton.IsEnabled = !(model.GitUrl.IsNullOrEmpty());
                }

            }
        }

        public static T? FindVisualChild<T>(DependencyObject parent, string? name = null) where T : DependencyObject
        {
            if (parent == null) return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                {
                    if (name == null)
                        return typedChild;

                    if (typedChild is FrameworkElement fe && fe.Name == name)
                        return typedChild;
                }

                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }

            return null;
        }


        private void OpenSolution_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                var profile = LoadProfileByName(profileName);
                if (profile == null)
                    return;

                var solutionPath = profile.SolutionFilePath;

                if (!string.IsNullOrWhiteSpace(solutionPath) && System.IO.File.Exists(solutionPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"\"{solutionPath}\"");
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"❌ Could not open solution: {ex.Message}");
                    }
                }
                else
                {
                    UpdateStatus("❗ Solution file not found or path not set.");
                }
            }
        }


        private void UpdateStatus(string message)
        {
            UIMessageHelper.LogToUI(message);
        }

        private async void CreateProfile_Click(object sender, RoutedEventArgs e)
        {
            var inputTextBox = new TextBox { PlaceholderText = "Enter profile name" };

            var dialog = new ContentDialog
            {
                Title = "Create Profile",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot,
                Content = inputTextBox
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                CreateProfile(inputTextBox.Text);
                LoadProfiles();
            }
        }

        private async void ImportProfile_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".json");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    var importPath = file.Path;
                    ImportProfile(importPath);

                    MessageLogger.Highlight($"✅ Profile imported: {Path.GetFileName(importPath)}");

                    // Refresh UI
                    LoadProfiles();
                }
                catch (Exception ex)
                {
                    MessageLogger.Error($"❌ Failed to import profile: {ex.Message}");
                }
            }
        }


        private void DeployProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                UpdateStatus($"Deploying profile '{profileName}'...");
                DeployAllModels(profileName);
                UpdateStatus($"✅ Deployment complete for '{profileName}'.");
            }
        }

        private void UnDeployProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                UnDeployAllModels(profileName);
                UpdateStatus($"🧹 Undeployment complete for '{profileName}'.");
            }
        }

        private void RefreshProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                CheckProfile(profileName);
                LoadModelListViewData(profileName);
            }
        }

        private async void SwitchProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is not string newProfile)
                return;

            var result = await new ContentDialog
            {
                Title = "Switch Profile",
                Content = "Switching profiles will undeploy the current one and deploy the selected profile. Continue?",
                PrimaryButtonText = "Switch",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            }.ShowAsync();

            if (result != ContentDialogResult.Primary)
                return;

            try
            {
                //UIMessageHelper.LogToUI($"🔄 Switching to profile '{newProfile}'...");
                if (SwitchProfile(newProfile))
                {
                    UIMessageHelper.LogToUI($"✅ Switched to profile '{newProfile}'");
                    LoadModelListViewData(newProfile);
                }
                else
                {
                    SetProfile(GetActiveProfile());
                }


            }
            catch (Exception ex)
            {
                UIMessageHelper.LogToUI($"❌ Failed to switch profile: {ex.Message}", MessageType.Error);
            }
        }

        private async void BrowseModel_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*"); // Required for folder picker to work

            // Attach picker to current window
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                ModelPathTextBox.Text = folder.Path;
            }
        }

        private void AddModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName && !string.IsNullOrWhiteSpace(ModelPathTextBox.Text))
            {
                var path = ModelPathTextBox.Text;
                AddEnvironmentToProfile(profileName, path);
                LoadModelListViewData(profileName);
                ModelPathTextBox.Text = string.Empty;
            }
        }
        private async void RemoveModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName && sender is Button button && button.Tag is string modelName)
            {

                var result = await new ContentDialog
                {
                    Title = "Remove model",
                    Content = "Remove model from the selected profile. Continue?",
                    PrimaryButtonText = "Remove",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.Content.XamlRoot
                }.ShowAsync();

                if (result != ContentDialogResult.Primary)
                    return;

                RemoveModelFromProfile(profileName, modelName);
                LoadModelListViewData(profileName);
                UpdateStatus($"🗑️ Model '{modelName}' removed from '{profileName}'.");
            }
        }



        private static DateTime GetBuildDate()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var filePath = assembly.Location;

            if (!File.Exists(filePath))
                return DateTime.MinValue;

            return File.GetLastWriteTime(filePath);
        }


        private async void ShowAboutDialog_Click(object sender, RoutedEventArgs e)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "v1.0.0";
            var buildDate = GetBuildDate().ToString("yyyy-MM-dd HH:mm");

            var contentPanel = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(4)
            };

            contentPanel.Children.Add(new TextBlock
            {
                Text = "FO Dev Manager",
                FontSize = 20,
                FontWeight = FontWeights.Bold
            });

            contentPanel.Children.Add(new TextBlock { Text = $"Version: {version}" });
            contentPanel.Children.Add(new TextBlock { Text = $"Build Date: {buildDate}" });

            contentPanel.Children.Add(new TextBlock
            {
                Text = "Developed by: Morten Aasheim"
            });

            contentPanel.Children.Add(new TextBlock
            {
                Text = "© 2025 ECIT Peritus AS. All rights reserved.",
                FontStyle = FontStyle.Italic
            });

            contentPanel.Children.Add(new HyperlinkButton
            {
                Content = "Visit peritus.no",
                NavigateUri = new Uri("https://peritus.no"),
                HorizontalAlignment = HorizontalAlignment.Left
            });

            var dialog = new ContentDialog
            {
                Title = "About",
                Content = contentPanel,
                PrimaryButtonText = "Close",
                XamlRoot = this.Content.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private static void TryCatch(Action asyncFunc)
        {
            try
            {
                asyncFunc();
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ {ex.Message}");
                Log.Error(ex.ToString());
            }
        }

        // -------- Service Calls --------

        private void RemoveModelFromProfile(string profileName, string modelName)
        {
            TryCatch(() => _profileService.RemoveModelFromProfile(profileName, modelName));
        }

        private void AddEnvironmentToProfile(string profileName, string path)
        {
            TryCatch(() => _profileService.AddEnvironment(profileName, string.Empty, path));
        }

        private void CreateProfile(string profileName)
        {
            TryCatch(() => _profileService.CreateProfile(profileName));
        }

        private void ImportProfile(string importPath)
        {
            TryCatch(() => _profileService.ImportProfile(importPath));
        }

        private void CheckProfile(string profileName)
        {
            TryCatch(() => _profileService.CheckProfile(profileName));
        }

        private List<ProfileEnvironmentModel> GetModelsInProfile(string profileName)
        {
            List<ProfileEnvironmentModel> models = new();
            TryCatch(() => models = _profileService.GetModelsInProfile(profileName));
            return models;
        }

        private ProfileEnvironmentModel? GetModel(string profileName, string modelName)
        {
            ProfileEnvironmentModel? model = null;
            TryCatch(() => model = _profileService.GetModel(profileName, modelName));
            return model;
        }

        private bool SwitchProfile(string profileName)
        {
            bool success = false;
            TryCatch(() => success = _profileService.SwitchProfile(profileName));
            return success;
        }

        private void DeployModel(string profileName, string modelName)
        {
            TryCatch(() => _deploymentService.DeployModel(profileName, modelName));
        }

        private void UnDeployModel(string profileName, string modelName)
        {
            TryCatch(() => _deploymentService.UnDeployModel(profileName, modelName));
        }

        private void DeployAllModels(string profileName)
        {
            TryCatch(() => _deploymentService.DeployAllUndeployedModels(profileName));
        }

        private void UnDeployAllModels(string profileName)
        {
            TryCatch(() => _deploymentService.UnDeployAllModels(profileName));
        }


        private string? GetActiveGitBranch(string profileName, string modelName)
        {
            string? branch = null;
            TryCatch(() => branch = _deploymentService.GetActiveGitBranch(profileName, modelName));
            return branch;
        }

        private bool IsGitRepo(string profileName, string modelName)
        {
            bool result = false;
            TryCatch(() => result = _deploymentService.CheckIfGitRepository(profileName, modelName));
            return result;
        }

        private void OpenGitRepo(string profileName, string modelName)
        {
            TryCatch(() => _deploymentService.OpenGitRepositoryUrl(profileName, modelName));
        }

        private List<ProfileModel> GetAllProfiles()
        {
            List<ProfileModel> profiles = new();
            TryCatch(() => profiles = _fileService.GetAllProfiles());
            return profiles;
        }

        private ProfileModel? LoadProfileByName(string profileName)
        {
            ProfileModel? profile = null;
            TryCatch(() => profile = _fileService.LoadProfile(profileName));
            return profile;
        }

    }
}


