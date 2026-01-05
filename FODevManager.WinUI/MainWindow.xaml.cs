using FODevManager.Logging;
using FODevManager.Messages;
using FODevManager.Models;
using FODevManager.Services;
using FODevManager.Shared.Models;
using FODevManager.Shared.Utils;
using FODevManager.Utils;
using FODevManager.WinUI.Framework;
using FODevManager.WinUI.ViewModel;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Text;
using WinRT;
using static FODevManager.WinUI.ViewModel.RepoGroupViewModel;


namespace FODevManager.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly UIMessageSubscriber _uiSubscriber;
        private readonly ProfileService _profileService;
        private readonly FileService _fileService;
        private readonly ModelDeploymentService _deploymentService;
        private readonly AppConfig _appConfig;
        private MicaController? _micaController;
        private SystemBackdropConfiguration? _backdropConfig;
        private AppWindow _appWindow;
        private CancellationTokenSource? _profileSyncCts;
        private ModelsGroupingViewModel? _groupingVm;
        private readonly SemaphoreSlim _mergePromptSemaphore = new(1, 1);
        public BusyOverlayViewModel BusyOverlayVm { get; }
        public ProfileModel ActiveProfile { get; set; }
        private CancellationTokenSource? _profileMonitorCancellationTokenSource;
        private Task? _profileMonitorTask;

        private BackgroundQueue? _backgroundQueue;
        private UiDispatcher? _uiDispatcher;
        private int _isGitCheckRunning;

        private UiDispatcher Ui => _uiDispatcher ?? throw new InvalidOperationException("BusyOps.Initialize must be called before using BusyOps.");

        public MainWindow(ProfileService profileService, FileService fileService, ModelDeploymentService deploymentService, AppConfig appConfig)
        {
            this.InitializeComponent();
            
            BusyOverlayVm = new BusyOverlayViewModel();
            
            this.Activated += MainWindow_Activated;
            this.Closed += MainWindow_Closed;

            Singleton<Engine>.Instance.EnvironmentType = EnvironmentType.WinUi;

            _uiDispatcher = new UiDispatcher(DispatcherQueue);
            BusyOps.Initialize(_uiDispatcher);

            _uiSubscriber = new UIMessageSubscriber(this.DispatcherQueue)
            {
                LogPreviewList = this.LogPreviewList
            };
            LogPreviewList.ItemsSource = _uiSubscriber.RecentMessages;

            var serilogSubscriber = new SerilogSubscriber();

            _profileService = profileService;
            _fileService = fileService;
            _deploymentService = deploymentService;
            _appConfig = appConfig;


            // Initialize Mica + TitleBar
            ApplyMicaEffect();
            SetTitleBar(AppTitleBar);

            // Store AppWindow reference
            _appWindow = GetAppWindowForCurrentWindow();

            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;

            StartUiHeartbeat();

            LogStartupInfo();

            LoadProfiles();

            UIMessageHelper.LogToUI($"READY...");

            this.Closed += (_, __) => BusyOverlayVm.Dispose();
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

        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _uiHeartbeatTimer;

        private long _uiHeartbeatTicks;
        private DateTime _lastUiTickUtc;


         private void StartUiHeartbeat()
        {
            _lastUiTickUtc = DateTime.UtcNow;

            _uiHeartbeatTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            _uiHeartbeatTimer.Interval = TimeSpan.FromMilliseconds(250);

            _uiHeartbeatTimer.Tick += (_, __) =>
            {
                Interlocked.Increment(ref _uiHeartbeatTicks);
                _lastUiTickUtc = DateTime.UtcNow;
            };

            _uiHeartbeatTimer.Start();

            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(1000).ConfigureAwait(false);

                    var ageMs = (DateTime.UtcNow - _lastUiTickUtc).TotalMilliseconds;
                    if (ageMs > 1500)
                    {
                        MessageLogger.Error($"UI STALL detected: last tick {ageMs:0} ms ago.");
                    }
                }
            });
        }


        private void LoadProfiles(string setProfile = "")
        {
            var profiles = _fileService.GetAllProfiles();
            ProfilesDropdown.ItemsSource = profiles.Select(x => x.ProfileName).ToList();

            if (profiles.Any())
            {
                UIMessageHelper.LogToUI($"🔔 {profiles.Count} profiles loaded");

                var currentProfile = !setProfile.IsNullOrEmpty() ? profiles.FirstOrDefault(x => x.ProfileName == setProfile) : (profiles.FirstOrDefault(x => x.IsActive) ?? profiles.First());

                if (currentProfile != null && currentProfile.IsActive)
                {
                    UIMessageHelper.LogToUI($"🔔 Active profile: {currentProfile.ProfileName} ");
                }

                if (setProfile.IsNullOrEmpty() && currentProfile != null && !currentProfile.IsActive)
                {
                    UIMessageHelper.LogToUI($"No active profile", MessageType.Warning);
                }

                SetSelectedProfile(currentProfile);

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


        private void SetSelectedProfile(ProfileModel? profile)
        {
            if (profile == null)
                return;

            ProfilesDropdown.SelectionChanged -= ProfilesDropdown_SelectionChanged;
            try
            {
                ProfilesDropdown.SelectedItem = profile.ProfileName;
            }
            finally
            {
                ProfilesDropdown.SelectionChanged += ProfilesDropdown_SelectionChanged;
            }

            SetActiveProfile(profile);
        }

        private void SetActiveProfile(ProfileModel? profile)
        {
            if (profile == null)
                return;

            ActiveProfile = profile;
            LoadModelListViewData(profile.ProfileName);
            UpdateProfileFields(profile);

            StartProfileSyncMonitoring(profile);

        }


        private void StartProfileSyncMonitoring(ProfileModel profile)
        {
            StopProfileSyncMonitoring();

            _backgroundQueue ??= new BackgroundQueue();
            _uiDispatcher ??= new UiDispatcher(DispatcherQueue);

            _profileMonitorCancellationTokenSource = new CancellationTokenSource();
            _profileMonitorTask = Task.Run(() => MonitorLoopAsync(profile, _profileMonitorCancellationTokenSource.Token));
        }

        private void StopProfileSyncMonitoring()
        {
            if (_profileMonitorCancellationTokenSource == null)
                return;

            try
            {
                _profileMonitorCancellationTokenSource.Cancel();
            }
            finally
            {
                _profileMonitorCancellationTokenSource.Dispose();
                _profileMonitorCancellationTokenSource = null;
                _profileMonitorTask = null;
            }
        }


        private async Task MonitorLoopAsync(ProfileModel profile, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);

                using var periodicTimer = new PeriodicTimer(TimeSpan.FromMinutes(1));

                var lastModelSyncUtc = DateTime.MinValue;
                var modelSyncInterval = TimeSpan.FromMinutes(15);

                while (await periodicTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!ShouldRunBackgroundTask())
                        continue;

                    _backgroundQueue!.TryEnqueue(ct => RunGitHealthCheckAndUpdates(profile, ct));

                    
                    if (DateTime.UtcNow - lastModelSyncUtc >= modelSyncInterval)
                    {
                        lastModelSyncUtc = DateTime.UtcNow;
                        _backgroundQueue!.TryEnqueue(ct => RunModelSyncCheckAsync(profile, ct));
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                MessageLogger.Error($"MonitorLoopAsync failed: {exception.Message}");
            }
        }


        private async Task RunGitHealthCheckAndUpdates(ProfileModel profile, CancellationToken token)
        {
            if (Interlocked.Exchange(ref _isGitCheckRunning, 1) == 1)
            {
                return;
            }

            try
            {
                var busyHandler = Singleton<BusyHandler>.Instance;

                if (busyHandler.IsBusy)
                    return;

                foreach (var repository in profile.Repositories ?? Enumerable.Empty<RepositoryModel>())
                {
                    token.ThrowIfCancellationRequested();

                    if (busyHandler.IsBusy)
                        return;

                    await RefreshRepositoryHealthAsync(repository, token);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isGitCheckRunning, 0);
            }
            
        }

        private async Task RefreshRepositoryHealthAsync(RepositoryModel repository, CancellationToken cancellationToken)
        {
            if (repository?.RepoRootFolder.IsNullOrEmpty() != false)
                return;

            
            cancellationToken.ThrowIfCancellationRequested();

            var repoRootFolder = repository.RepoRootFolder;
            var mainBranchName = repository.MainBranchName.IsNullOrEmpty()
                ? "main"
                : repository.MainBranchName;

            var stopwatch = Stopwatch.StartNew();
            MessageLogger.LogOnly($"Git health start: {repository.DisplayName}");

            try
            {
                var hasUpdates = await GitHelper.HasMainChangesAsync(repoRootFolder, mainBranchName, cancellationToken).ConfigureAwait(false);
                var currentBranch =  await GitHelper.GetActiveBranchAsync(repoRootFolder, cancellationToken).ConfigureAwait(false) ?? string.Empty;
                var branchHealth = await GitHelper.GetBranchHealthAsync(repoRootFolder, cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                await Ui.EnqueueAsync(() =>
                {
                    var groupingViewModel = _groupingVm;
                    if (groupingViewModel?.GitGroups == null)
                        return;

                    var repoGroupVm = groupingViewModel.GitGroups.FirstOrDefault(group =>
                        group.Repository?.RepoRootFolder.SameAs(repoRootFolder) == true);

                    if (repoGroupVm == null)
                        return;

                    repoGroupVm.Branch = currentBranch;
                    repoGroupVm.HasMainUpdates = hasUpdates;
                    repoGroupVm.BranchHealth = branchHealth;

                    if (repoGroupVm.Repository != null)
                        repoGroupVm.Repository.LastKnownBranch = currentBranch;
                });
                
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown / profile switch
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"RefreshRepositoryHealthAsync failed for '{repoRootFolder}': {exception.Message}");
            }

            finally
            {
                stopwatch.Stop();
                MessageLogger.LogOnly($"Git health end: {repository.DisplayName} ({stopwatch.ElapsedMilliseconds} ms)");
            }
        }


        private async Task<bool> EnsureMergedWithMainAsync(RepositoryModel repository)
        {
            if (!GitHelper.HasMainChanges(repository.RepoRootFolder, repository.MainBranchName))
                return true;

            await _mergePromptSemaphore.WaitAsync();

            try
            {
                var confirm = await DialogHelper.ConfirmAsync(this, "Git update available", $"{repository.DisplayName}\nChanges detected in main.\n\nMerge main into your current branch?");

                if (!confirm)
                    return false;

                var merged = await RunOperationAsync(() => GitHelper.MergeMainIntoCurrentBranch(repository.RepoRootFolder, repository.MainBranchName), "Merge main into current branch");

                if (!merged)
                    return false;

                await RefreshRepositoryHealthAsync(repository, CancellationToken.None);

                return true;
            }
            finally
            {
                _mergePromptSemaphore.Release();
            }

        }

        private bool ShouldRunBackgroundTask()
        {
            var busyHandler = Singleton<BusyHandler>.Instance;

            if (busyHandler.IsBusy)
                return false;

            var timeSinceBusyEnded = DateTime.UtcNow - busyHandler.LastBusyEndedUtc;
            return timeSinceBusyEnded >= TimeSpan.FromSeconds(30);
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _profileSyncCts?.Cancel();
            BusyOverlayVm.Dispose();
        }


        private static readonly SemaphoreSlim _dialogGate = new(1, 1);
        private async Task<T> ShowDialogSingleFlightAsync<T>(Func<Task<T>> showFunc, CancellationToken token)
        {
            await _dialogGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await showFunc().ConfigureAwait(false);
            }
            finally
            {
                _dialogGate.Release();
            }
        }


        private async Task RunModelSyncCheckAsync(ProfileModel currentProfile, CancellationToken token)
        {
            var stopwatch = Stopwatch.StartNew();
            MessageLogger.LogOnly($"Run ModelSync start: {currentProfile.ProfileName}");

            try
            {
                token.ThrowIfCancellationRequested();

                if (currentProfile == null)
                    return;

                var syncResult = await _profileService.CheckProfileModelChangesAsync(currentProfile).ConfigureAwait(false);

                if (!syncResult.HasChanges)
                    return;

                var added = syncResult.AddedModels.Any()
                    ? $"Added: {string.Join(", ", syncResult.AddedModels)}\n"
                    : string.Empty;

                var removed = syncResult.RemovedModels.Any()
                    ? $"Removed: {string.Join(", ", syncResult.RemovedModels)}\n"
                    : string.Empty;

                var message = $"The {currentProfile.ProfileName} profile definition has changed (models were added or removed).\n\n" +
                    added + removed + "\nDo you want to re-import the profile now?";

                var userWantsImport = await Ui.EnqueueAsync(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Profile changes detected",
                        Content = message,
                        PrimaryButtonText = "Re-import",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = Content.XamlRoot
                    };

                    var result = await dialog.ShowAsync();
                    return result == ContentDialogResult.Primary;
                }).ConfigureAwait(false);

                if (!userWantsImport)
                    return;

                if (currentProfile.ProfileFilePath.IsNullOrEmpty())
                    return;

                var (ok, updatedProfile) = await BusyOps.TrySyncAsAsync(() => _profileService.ImportProfile(currentProfile.ProfileFilePath), "Import profile");

                if (!ok || updatedProfile == null)
                    return;

                await Ui.EnqueueAsync(() =>
                {
                    SetSelectedProfile(updatedProfile);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown/profile switch
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"Run ModelSync failed: {ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                MessageLogger.LogOnly($"Run ModelSync end: {currentProfile.ProfileName} ({stopwatch.ElapsedMilliseconds} ms)");
            }
        }


        private void LoadModelListViewData(string profileName)
        {
            var profile = _profileService.LoadProfile(profileName);
            if (profile == null)
            {
                MessageLogger.Error($"LoadModelListViewData: Could not load profile '{profileName}'.");
                CombinedList.ItemsSource = new List<object>();
                return;
            }

            var modelViewModels = _profileService
                .GetModelsInProfile(profileName)
                .Select(model => model.ToViewModel(profile.ProfileName))
                .ToList();

            _groupingVm = new ModelsGroupingViewModel(profile, modelViewModels);

            var firstRepoGroup = _groupingVm.GitGroups.FirstOrDefault();
            if (firstRepoGroup != null)
                firstRepoGroup.IsExpanded = true;

            var combinedItems = new List<object>();
            combinedItems.AddRange(_groupingVm.GitGroups);
            combinedItems.AddRange(_groupingVm.NonGitModels);

            CombinedList.ItemsSource = combinedItems;
        }

        private void ProfilesDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                if (ActiveProfile?.ProfileName == profileName)
                    return;

                var profile = LoadProfileByName(profileName);
                if (profile != null)
                {
                    SetActiveProfile(profile);
                }
            }
        }

        private void UpdateProfileFields(ProfileModel profile)
        {
            DatabaseNameTextBox.Text = profile.DatabaseName ?? string.Empty;
            IsActiveCheckBox.IsChecked = profile.IsActive;

        }

        private void OpenGitForRepo_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName) return;
            if (sender is not Button btn) return;
            if (btn.DataContext is not RepoGroupViewModel group) return;

            var anchorModel = group.Models.FirstOrDefault();
            if (anchorModel is null || anchorModel.ModelName.IsNullOrEmpty())
            {
                MessageLogger.Warning("⚠️ No model found in this repo group to open Git.");
                return;
            }

            OpenGitRepo(profileName, anchorModel.ModelName);
        }

        private async void AssignTask_ForRepo_Click(object sender, RoutedEventArgs eventArgs)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName)
                return;

            if (sender is not Button button)
                return;

            var group = button.DataContext as RepoGroupViewModel
                        ?? button.Tag as RepoGroupViewModel;

            if (group == null)
                return;

            var repository = group.Repository;
            if (repository == null)
                return;

            var dialog = new ContentDialog
            {
                Title = $"Assign Task to repository ({group.DisplayName})",
                PrimaryButtonText = "Assign",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var taskIdTextBox = new TextBox { PlaceholderText = "Enter Task ID (e.g. 2145)" };
            var commentTextBox = new TextBox { PlaceholderText = "Enter optional comment (e.g. fix performance)" };
            dialog.Content = new StackPanel { Spacing = 8, Children = { taskIdTextBox, commentTextBox } };

            var dialogResult = await dialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary)
                return;

            var taskId = taskIdTextBox.Text?.Trim() ?? string.Empty;

            var comment = commentTextBox.Text?.Trim() ?? string.Empty;

            try
            {
                await RunOperationAsync(() =>
                {
                    _deploymentService.AssignTaskToRepository(
                        profileName: profileName,
                        repoId: repository.RepoId,
                        task: taskId,
                        comment: comment,
                        switchBranch: true);
                }, "Assign Task");

                UIMessageHelper.LogToUI($"✅ Assigned Task '{taskId}' to repo '{group.DisplayName}'.");
            }
            catch (Exception exception)
            {
                UIMessageHelper.LogToUI($"❌ Failed to assign Task: {exception.Message}", MessageType.Error);
            }

            LoadModelListViewData(profileName);
        }

        private void OpenTask_ForRepo_Click(object sender, RoutedEventArgs eventArgs)
        {
            if (sender is not Button button)
                return;

            if (button.DataContext is not RepoGroupViewModel repoGroup)
                return;

            var taskId = repoGroup.Repository?.Task;

            if (taskId.IsNullOrEmpty())
                return;

            var url = $"{_appConfig.TaskUrl}/{taskId}";
            ServiceHelper.OpenUrl(url);
        }

        private async void DeployModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName)
                return;

            if (sender is Button button && button.Tag is string modelName)
            {
                if (await DeployModel(profileName, modelName))
                {
                    LoadModelListViewData(profileName);
                    UIMessageHelper.LogToUI($"🚀 Deployed model '{modelName}'");
                }
            }
        }

        private async void UnDeployModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName)
                return;

            if (sender is Button button && button.Tag is string modelName)
            {
                if (await UnDeployModel(profileName, modelName))
                {
                    LoadModelListViewData(profileName);
                    UIMessageHelper.LogToUI($"🧯 Undeployed model '{modelName}'");
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

        private async void CreateModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName)
                return;

            var inputDialog = new ContentDialog
            {
                Title = "Create New Model",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var inputBox = new TextBox
            {
                PlaceholderText = "Enter model name (e.g. MyNewModel)"
            };

            inputDialog.Content = inputBox;

            var result = await inputDialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !inputBox.Text.IsNullOrEmpty())
            {
                var modelName = inputBox.Text.Trim();

                await CreateModel(profileName, modelName);

                UIMessageHelper.LogToUI($"📦 Created new model '{modelName}' under profile '{profileName}'");
                LoadModelListViewData(profileName);
            }
        }

        private void OpenSolution_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                var profile = LoadProfileByName(profileName);
                if (profile == null)
                    return;

                var solutionPath = profile.SolutionFilePath;

                if (!solutionPath.IsNullOrEmpty() && System.IO.File.Exists(solutionPath))
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

            if (result == ContentDialogResult.Primary && !inputTextBox.Text.IsNullOrEmpty())
            {
                await CreateProfile(inputTextBox.Text);
                LoadProfiles();
            }
        }

        private async void ImportFromFile_Click(object sender, RoutedEventArgs e)
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
                    var importedProfileName = await ImportProfile(importPath);

                    MessageLogger.Highlight($"✅ Profile imported: {Path.GetFileName(importPath)}");

                    // Refresh UI
                    LoadProfiles(importedProfileName);
                }
                catch (Exception ex)
                {
                    MessageLogger.Error($"❌ Failed to import profile: {ex.Message}");
                }
            }
        }

        private async void ImportFromRepo_Click(object sender, RoutedEventArgs eventArgs)
        {
            var urlTextBox = new TextBox
            {
                PlaceholderText = "https://dev.azure.com/org/repo (or similar URL)",
                MinWidth = 420
            };

            var repoDialog = new ContentDialog
            {
                Title = "Import profile from repository",
                Content = urlTextBox,
                PrimaryButtonText = "Clone & Import",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var dialogResult = await repoDialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary)
                return;

            var repoUrl = urlTextBox.Text?.Trim() ?? string.Empty;
            if (repoUrl.IsNullOrEmpty())
            {
                MessageLogger.Warning("Import cancelled: URL was empty.");
                return;
            }

            var (ok, importedProfile) = await BusyOps.TrySyncAsAsync<ProfileModel>(() => _profileService.ImportProfileFromRepoUrl(repoUrl), "Import Profile From Repo Url");

            if (!ok)
            {
                MessageLogger.Error("Import failed due to an error during cloning or importing.");
                return;
            }
            
            if (importedProfile == null)
            {
                MessageLogger.Error("Import failed. No profile was imported.");
                return;
            }

            MessageLogger.Highlight($"✅ Imported profile: {importedProfile.ProfileName}");
            // Refresh UI
            LoadProfiles(importedProfile.ProfileName);

        }

        private async void DeployProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                UpdateStatus($"Deploying profile '{profileName}'...");
                await DeployAllModels(profileName);
                UpdateStatus($"✅ Deployment complete for '{profileName}'.");
            }
        }

        private async void UnDeployProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                await UnDeployAllModels(profileName);
                UpdateStatus($"🧹 Undeployment complete for '{profileName}'.");
            }
        }

        private async void RefreshProfile_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                await CheckProfile(profileName);
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

            if (await SwitchProfile(newProfile))
            {
                SetSelectedProfile(LoadProfileByName(newProfile));
            }
            else
            {
                SetSelectedProfile(GetActiveProfile());
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

        private async void AddModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName && !ModelPathTextBox.Text.IsNullOrEmpty())
            {
                var path = ModelPathTextBox.Text;
                await AddModelToProfile(profileName, path);
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

                await RemoveModelFromProfile(profileName, modelName);
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

        private async void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var settingsPage = new SettingsPage
            {
                MinWidth = 800,
                MinHeight = 500
            };
            var dialog = new ContentDialog
            {
                Title = "Settings",
                Content = settingsPage,
                PrimaryButtonText = "Close",
                XamlRoot = this.Content.XamlRoot
            };

            dialog.MaxWidth = 1200;
            dialog.MinWidth = 800;

            await dialog.ShowAsync();
        }

        public static void LogStartupInfo()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "v1.0.0";
            var buildDate = GetBuildDate().ToString("yyyy-MM-dd HH:mm");


            var productName = "FO Dev Manager";

            UIMessageHelper.LogToUI($"ℹ️ {productName} {version} 🛠️ Build date: {buildDate}");
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

        private static async Task<bool> RunOperationAsync(Action action, string operationName, bool shutdownServer = true)
        {
            return await BusyOps.TrySyncAsAsync(action, operationName, shutdownServer);

        }

        private static void TryCatch(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"❌ {ex.Message}");
                Log.Error(ex.ToString());
            }
        }

        // -------- Service Calls --------

        private async Task<bool> CreateModel(string profileName, string modelName)
        {
            return await RunOperationAsync(() => _profileService.CreateModel(profileName, modelName), "Create model");
        }

        private async Task<bool> RemoveModelFromProfile(string profileName, string modelName)
        {
            return await RunOperationAsync(() => _profileService.RemoveModelFromProfile(profileName, modelName), "Remove Model From Profile");
        }

        private async Task<bool> AddModelToProfile(string profileName, string path)
        {
            var ret = await RunOperationAsync(() => _profileService.AddModel(profileName, string.Empty, path), "Add Model to profile");
            if (!ret) return false;
            //await CheckProfile(profileName);
            LoadModelListViewData(profileName);

            return true;

        }

        private async Task<bool> CreateProfile(string profileName)
        {
            return await RunOperationAsync(() => _profileService.CreateProfile(profileName), "Create profile");
        }

        private async Task<string> ImportProfile(string importPath)
        {
            var (ok, importedProfile) = await BusyOps.TrySyncAsAsync(() => _profileService.ImportProfile(importPath), "Import profile");

            if (ok && importedProfile != null && !importedProfile.ProfileName.IsNullOrEmpty())
                return importedProfile.ProfileName;

            return string.Empty;
        }


        private async Task<bool> CheckProfile(string profileName)
        {
            return await RunOperationAsync(() => _profileService.CheckProfile(profileName), "Check profile", shutdownServer: false); 
        }


        private async Task<bool> SwitchProfile(string profileName)
        {
            var (ok, success) = await BusyOps.TrySyncAsAsync(() => _profileService.SwitchProfile(profileName), "Switch profile");
            return ok && success;
        }

        private async Task<bool> DeployModel(string profileName, string modelName)
        {
            return await RunOperationAsync(() => _deploymentService.DeployModel(profileName, modelName), "Deploy model");
        }

        private async Task<bool> UnDeployModel(string profileName, string modelName)
        {
            return await RunOperationAsync(() => _deploymentService.UnDeployModel(profileName, modelName), "Undeploy model");
        }

        private async Task<bool> DeployAllModels(string profileName)
        {
            var (ok, success) = await BusyOps.TrySyncAsAsync(() => _deploymentService.DeployAllUndeployedModels(profileName), "Deploy all models");
            return ok && success;
        }

        private async Task<bool> UnDeployAllModels(string profileName)
        {
            return await RunOperationAsync(() => _deploymentService.UnDeployAllModels(profileName), "Undeploy all models");
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

        private void DatabaseNameTextBox_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            DatabaseNameTextBox.IsReadOnly = false;
        }

        private async void DatabaseNameTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;

            if (ActiveProfile == null) return;

            var newDbString = (DatabaseNameTextBox.Text ?? string.Empty).Trim();
            if (newDbString.SameAs(ActiveProfile.DatabaseName))
            {
                // nothing changed—do nothing
                MessageLogger.Info("Database name unchanged.");
                return;
            }

            if (newDbString.IsNullOrEmpty())
            {
                MessageLogger.Warning("Database name cannot be empty.");
                DatabaseNameTextBox.Text = ActiveProfile.DatabaseName;
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Apply database change?",
                Content = $"Change database for profile '{ActiveProfile.ProfileName}' to:\n\n“{newDbString}”\n\nApply now?",
                PrimaryButtonText = "Yes",
                CloseButtonText = "No",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                // revert if user says No
                DatabaseNameTextBox.Text = ActiveProfile.DatabaseName;
                DatabaseNameTextBox.IsReadOnly = true;
                MessageLogger.Info("Database change cancelled.");
                return;
            }

            TryCatch(() =>
            {
                _profileService.SetDatabaseName(ActiveProfile.ProfileName, newDbString);
                ActiveProfile.DatabaseName = newDbString;
                DatabaseNameTextBox.IsReadOnly = true;
                MessageLogger.Highlight($"✅ Database name updated to: {newDbString}");
            });
        }
        
        private void RepoHeader_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var frameworkElement = e.OriginalSource as FrameworkElement ?? sender as FrameworkElement;
            if (frameworkElement?.DataContext is RepoGroupViewModel repoGroup)
            {
                repoGroup.IsExpanded = !repoGroup.IsExpanded;
                e.Handled = true;

                if (!repoGroup.IsExpanded)
                    return;
                
            }
        }

        private async void RepoHeader_GitMerge_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement frameworkElement)
                return;

            if (frameworkElement.DataContext is not RepoGroupViewModel repoGroup)
                return;

            if (!repoGroup.HasMainUpdates)
                return;

            if (repoGroup.Repository == null)
                return;

            await EnsureMergedWithMainAsync(repoGroup.Repository);
        }

        private async void GitActions_ResetToMain_Click(object sender, RoutedEventArgs routedEventArgs)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName)
                return;

            var profile = LoadProfileByName(profileName);
            if (profile == null)
                return;

            var confirm = await DialogHelper.ConfirmAsync(
                this,
                "Reset to Main",
                "This will go through all repositories in the profile.\n\n" +
                "• Stash any uncommitted changes\n" +
                "• Checkout main\n" +
                "• Fetch and pull\n\n" +
                "Continue?");

            if (!confirm)
                return;

            await RunOperationAsync(() => _profileService.GitResetProfile(profile), "Reset to Main");

            // Reload view models (branch info, grouping, etc.)
            UIRefresh(profileName);
        }

        private async void GitActions_TagRelease_Click(object sender, RoutedEventArgs routedEventArgs)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName)
                return;

            var profile = LoadProfileByName(profileName);
            if (profile == null)
                return;

            var nowLocal = DateTime.Now;
            
            var confirm = await DialogHelper.ConfirmAsync(
                this,
                "Tag Release",
                "This will tag *all* repositories in the profile.\n\n" +                
                "Rules:\n" +
                "• Must be on main or a release branch\n" +
                "• Must have no uncommitted changes\n\n" +
                "Continue?");

            if (!confirm)
                return;

            await RunOperationAsync(() => _profileService.TagReleaseProfile(profile), "Tag Release");

            UIRefresh(profileName);
        }


        private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            string selectedProfileName = string.Empty;

            try
            {

                if (ProfilesDropdown.SelectedItem is string profileName)
                {
                    selectedProfileName = profileName;
                    var profile = LoadProfileByName(profileName);
                    if (profile == null)
                    {
                        MessageLogger.Error($"✖ Profile '{profileName}' could not be loaded.");
                        return;
                    }
                }
                // Confirm
                var dlg = new ContentDialog
                {
                    Title = "Delete profile?",
                    Content = $"This will permanently delete the profile '{selectedProfileName}'\n\nThis cannot be undone.",
                    PrimaryButtonText = "Delete",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.Content.XamlRoot
                };

                var result = await dlg.ShowAsync();
                if (result != ContentDialogResult.Primary)
                {
                    MessageLogger.Info("ℹ Delete profile cancelled.");
                    return;
                }


                try
                {

                    _profileService.DeleteProfile(selectedProfileName);

                    MessageLogger.Highlight($"✅ Deleted profile: {selectedProfileName}");


                    ProfilesDropdown.SelectedItem = null;
                    DatabaseNameTextBox.Text = string.Empty;
                    IsActiveCheckBox.IsChecked = false;

                    LoadProfiles();

                }
                catch (Exception ex)
                {
                    MessageLogger.Error($"✖ Failed to remove profile '{selectedProfileName}': {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                MessageLogger.Error($"✖ DeleteProfile error: {ex.Message}");
            }
        }

        private void CombinedList_RightTapped(object sender, RightTappedRoutedEventArgs eventArgs)
        {
            if (sender is not ListView listView)
                return;

            var frameworkElementSource = eventArgs.OriginalSource as FrameworkElement;
            var clickedItem = frameworkElementSource?.DataContext;

            if (clickedItem == null)
                return;

            var flyout = new MenuFlyout();

            var propertiesMenuItem = new MenuFlyoutItem
            {
                Text = "Properties…"
            };

            propertiesMenuItem.Click += async (_, _) =>
            {
                switch (clickedItem)
                {
                    case RepoGroupViewModel repoGroupViewModel:
                        await ShowRepositoryPropertiesAsync(repoGroupViewModel).ConfigureAwait(true);
                        break;

                    case ProfileEnvironmentViewModel environmentViewModel:
                        await ShowModelPropertiesAsync(environmentViewModel).ConfigureAwait(true);
                        break;
                }
            };

            flyout.Items.Add(propertiesMenuItem);

            flyout.ShowAt(listView, eventArgs.GetPosition(listView));
            eventArgs.Handled = true;
        }

        private async Task ShowRepositoryPropertiesAsync(RepoGroupViewModel repoGroupViewModel)
        {
            RepositoryModel repositoryModel = repoGroupViewModel.Repository;

            var displayNameTextBox = new TextBox { Text = repositoryModel.DisplayName ?? string.Empty };
            var preferredBranchTextBox = new TextBox { Text = repositoryModel.PreferredBranch ?? string.Empty };

            var autoCheckoutToggle = new ToggleSwitch
            {
                IsOn = repositoryModel.AutoCheckoutOnProfileLoad,
                Header = "Auto checkout on profile load"
            };

            var autoStashToggle = new ToggleSwitch
            {
                IsOn = repositoryModel.AutoStashOnDirtyCheckout,
                Header = "Auto stash on dirty checkout"
            };

            var taskTextBox = new TextBox { Text = repositoryModel.Task ?? string.Empty };
            var taskCommentTextBox = new TextBox { Text = repositoryModel.TaskComment ?? string.Empty };

            var contentPanel = new StackPanel
            {
                MinWidth = 400,
                Spacing = 10,
                Children =
            {
                new TextBlock { Text = $"Repo: {repositoryModel.RepoId}" },
                new TextBlock { Text = $"Root: {repositoryModel.RepoRootFolder}" },

                new TextBlock { Text = "Display name" },
                displayNameTextBox,

                new TextBlock { Text = "Preferred branch" },
                preferredBranchTextBox,

                autoCheckoutToggle,
                autoStashToggle,

                new TextBlock { Text = "Task" },
                taskTextBox,

                new TextBlock { Text = "Task comment" },
                taskCommentTextBox
            }
            };

            var dialog = new ContentDialog
            {
                Title = "Repository properties",
                Content = contentPanel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot
            };

            var dialogResult = await dialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary)
                return;

            repositoryModel.DisplayName = displayNameTextBox.Text?.Trim() ?? string.Empty;
            repositoryModel.PreferredBranch = preferredBranchTextBox.Text?.Trim();
            repositoryModel.AutoCheckoutOnProfileLoad = autoCheckoutToggle.IsOn;
            repositoryModel.AutoStashOnDirtyCheckout = autoStashToggle.IsOn;
            repositoryModel.Task = taskTextBox.Text?.Trim() ?? string.Empty;
            repositoryModel.TaskComment = taskCommentTextBox.Text?.Trim() ?? string.Empty;

            _profileService.UpdateRepositoryProperties(repoGroupViewModel.ProfileName, repositoryModel);

            UIRefresh(repoGroupViewModel.ProfileName);
        }
        private async Task ShowModelPropertiesAsync(ProfileEnvironmentViewModel environmentViewModel)
        {
            ProfileEnvironmentModel environmentModel = environmentViewModel.Model;

            var mainFoToggle = new ToggleSwitch
            {
                IsOn = environmentModel.IsMainFOModel,
                Header = "Main FO model (solution root)"
            };

            var contentPanel = new StackPanel
            {
                MinWidth = 400,
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = $"Model: {environmentModel.ModelName}" },
                    mainFoToggle,

                    new TextBlock { Text = $"Type: {environmentModel.ModelType}" },
                    new TextBlock { Text = $"Root: {environmentModel.ModelRootFolder}" },
                    new TextBlock { Text = $"Project: {environmentModel.ProjectFilePath}" },
                    new TextBlock { Text = $"Metadata: {environmentModel.MetadataFolder}" },
                    new TextBlock { Text = $"Compiled: {environmentModel.CompiledModelFolder}" }


                }
            };

            var dialog = new ContentDialog
            {
                Title = "Model properties",
                Content = contentPanel,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot

            };

            var dialogResult = await dialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary)
                return;

            environmentModel.IsMainFOModel = mainFoToggle.IsOn;

            _profileService.UpdateModelProperties(environmentViewModel.ProfileName, environmentModel);

            UIRefresh(environmentViewModel.ProfileName);
        }

        private void UIRefresh(string profileName)
        {
            LoadModelListViewData(profileName);
            CombinedList.UpdateLayout();
        }

    }

}


