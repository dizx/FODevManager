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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Text;
using WinRT;
using static FODevManager.WinUI.ViewModel.RepoGroupViewModel;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace FODevManager.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly UIMessageSubscriber _uiSubscriber;
        private readonly ProfileService _profileService;
        private readonly FileService _fileService;
        private readonly ModelDeploymentService _deploymentService;
        private readonly ModelVersionService _modelVersionService;
        private readonly AppConfig _appConfig;
        private MicaController? _micaController;
        private SystemBackdropConfiguration? _backdropConfig;
        private AppWindow _appWindow;
        
        private ModelsGroupingViewModel? _groupingVm;
        private readonly SemaphoreSlim _mergePromptSemaphore = new(1, 1);
        public BusyOverlayViewModel BusyOverlayVm { get; }
        public ProfileModel? ActiveProfile { get; set; }
        private CancellationTokenSource? _profileMonitorCancellationTokenSource;
        private Task? _profileMonitorTask;

        private BackgroundQueue? _backgroundQueue;
        private UiDispatcher? _uiDispatcher;
        private int _isGitCheckRunning;
        private int _profileLoadRequestId;
        private readonly ConcurrentDictionary<string, byte> _queuedNugetPreparationProfiles = new(StringComparer.OrdinalIgnoreCase);

        private UiDispatcher Ui => _uiDispatcher ?? throw new InvalidOperationException("BusyOps.Initialize must be called before using BusyOps.");

        public MainWindow(ProfileService profileService, FileService fileService, ModelDeploymentService deploymentService, ModelVersionService modelVersionService, AppConfig appConfig)
        {
            this.InitializeComponent();
            this.Activated += MainWindow_Activated;

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
            _modelVersionService = modelVersionService;
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

            // Replace the lambda with a proper handler that includes cancellation
            this.Closed += (_, args) => 
            {
                _profileMonitorCancellationTokenSource?.Cancel();
                BusyOverlayVm.Dispose();
            };
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
        private CancellationTokenSource? _uiHeartbeatCts;

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
            _uiHeartbeatCts?.Cancel();
            _uiHeartbeatCts?.Dispose();
            _uiHeartbeatCts = new CancellationTokenSource();
            var token = _uiHeartbeatCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1000, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    var ageMs = (DateTime.UtcNow - _lastUiTickUtc).TotalMilliseconds;
                    if (ageMs > 1500)
                    {
                        MessageLogger.Error($"UI STALL detected: last tick {ageMs:0} ms ago.");
                    }
                }
            });
        }

        private void LogActiveEnvironmentInfo(ProfileModel? profile)
        {
            if (profile == null)
                return;

            if (profile.IsActive)
                UIMessageHelper.LogToUI($"🔔 Active profile: {profile.ProfileName}");

            var currentDb = WebConfigHelper.GetCurrentDatabaseName();
            if (!currentDb.IsNullOrEmpty())
                UIMessageHelper.LogToUI($"🗄️ Active database: {currentDb}");
            else
                UIMessageHelper.LogToUI("🗄️ Active database: (unknown)", MessageType.Warning);
        }


        private sealed class ProfileLoadResult
        {
            public required ProfileModel Profile { get; init; }
            public required ModelsGroupingViewModel Grouping { get; init; }
            public required List<object> CombinedItems { get; init; }
        }

        private void LoadProfiles(string setProfile = "")
        {
            _ = LoadProfilesAsync(setProfile);
        }

        private async Task LoadProfilesAsync(string setProfile = "")
        {
            var profiles = await Task.Run(() => _fileService.GetAllProfiles());
            ProfilesDropdown.ItemsSource = profiles.Select(x => x.ProfileName).ToList();

            if (profiles.Any())
            {
                UIMessageHelper.LogToUI($"{profiles.Count} profiles loaded");

                var currentProfile = !setProfile.IsNullOrEmpty() ? profiles.FirstOrDefault(x => x.ProfileName == setProfile) : (profiles.FirstOrDefault(x => x.IsActive) ?? profiles.First());

                if (setProfile.IsNullOrEmpty() && currentProfile != null && !currentProfile.IsActive)
                {
                    UIMessageHelper.LogToUI($"No active profile", MessageType.Warning);
                }

                if (currentProfile != null)
                    await SetSelectedProfileAsync(currentProfile.ProfileName);
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

            _ = SetSelectedProfileAsync(profile.ProfileName);
        }

        private async Task SetSelectedProfileAsync(string profileName)
        {
            var requestId = Interlocked.Increment(ref _profileLoadRequestId);
            var loadResult = await Task.Run(() => BuildProfileLoadResult(profileName));
            if (requestId != _profileLoadRequestId || loadResult == null)
                return;

            ProfilesDropdown.SelectionChanged -= ProfilesDropdown_SelectionChanged;
            try
            {
                ProfilesDropdown.SelectedItem = profileName;
            }
            finally
            {
                ProfilesDropdown.SelectionChanged += ProfilesDropdown_SelectionChanged;
            }

            ApplyLoadedProfile(loadResult);
        }

        private ProfileLoadResult? BuildProfileLoadResult(string profileName)
        {
            var profile = _profileService.LoadProfile(profileName);
            if (profile == null)
                return null;

            var modelViewModels = profile.AllModels
                .Select(model => model.ToViewModel(profile.ProfileName))
                .ToList();

            foreach (var viewModel in modelViewModels)
            {
                if (_modelVersionService.TryGetVersionText(viewModel.Model, out var versionText))
                    viewModel.VersionText = versionText;
            }

            var groupingVm = new ModelsGroupingViewModel(profile, modelViewModels);
            var firstRepoGroup = groupingVm.GitGroups.FirstOrDefault();
            if (firstRepoGroup != null)
                firstRepoGroup.IsExpanded = true;

            var combinedItems = new List<object>();
            combinedItems.AddRange(groupingVm.GitGroups);
            combinedItems.AddRange(groupingVm.NonGitModels);

            return new ProfileLoadResult
            {
                Profile = profile,
                Grouping = groupingVm,
                CombinedItems = combinedItems
            };
        }

        private void ApplyLoadedProfile(ProfileLoadResult loadResult)
        {
            ActiveProfile = loadResult.Profile;
            _groupingVm = loadResult.Grouping;
            CombinedList.ItemsSource = loadResult.CombinedItems;
            UpdateProfileFields(loadResult.Profile);

            QueueNugetPreparation(loadResult.Profile);
            StartProfileSyncMonitoring(loadResult.Profile);
            LogActiveEnvironmentInfo(loadResult.Profile);
        }

        private void QueueNugetPreparation(ProfileModel profile)
        {
            if (profile == null || profile.Repositories == null || profile.Repositories.Count == 0)
                return;

            if (!_queuedNugetPreparationProfiles.TryAdd(profile.ProfileName, 0))
                return;

            _backgroundQueue ??= new BackgroundQueue();
            _backgroundQueue.TryEnqueue(async cancellationToken =>
            {
                try
                {
                    var updated = _profileService.PrepareCompiledNugetModels(profile.ProfileName);
                    if (!updated || cancellationToken.IsCancellationRequested)
                        return;

                    await Ui.EnqueueAsync(async () =>
                    {
                        await RefreshProfileViewAsync(profile.ProfileName);
                        return;
                    }).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    MessageLogger.Error($"Background NuGet preparation failed for profile '{profile.ProfileName}': {exception.Message}");
                }
            });
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

                var merged = await RunOperationAsync(() => GitHelper.MergeMainIntoCurrentBranch(repository.RepoRootFolder, repository.MainBranchName), "Merge main into current branch", false);

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
            _uiHeartbeatTimer?.Stop();
            StopProfileSyncMonitoring();
            _uiHeartbeatCts?.Cancel();
            _uiHeartbeatCts?.Dispose();
            _uiHeartbeatCts = null;
            _uiHeartbeatTimer?.Stop();
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
            _ = RefreshProfileViewAsync(profileName);
        }

        private async Task RefreshProfileViewAsync(string profileName)
        {
            var requestId = Interlocked.Increment(ref _profileLoadRequestId);
            var loadResult = await Task.Run(() => BuildProfileLoadResult(profileName));
            if (requestId != _profileLoadRequestId)
                return;

            if (loadResult == null)
            {
                MessageLogger.Error($"LoadModelListViewData: Could not load profile '{profileName}'.");
                CombinedList.ItemsSource = new List<object>();
                return;
            }

            ApplyLoadedProfile(loadResult);
        }

        private async void ProfilesDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
                if (ActiveProfile?.ProfileName == profileName)
                    return;

                await SetSelectedProfileAsync(profileName);
            }
        }

        private void UpdateProfileFields(ProfileModel profile)
        {
            DatabaseNameTextBox.Text = profile.DatabaseName ?? string.Empty;
            SetDatabaseEditingState(false);
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
                XamlRoot = this.Content.XamlRoot
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
                }, "Assign Task", false);

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
                LoadModelListViewData(profileName);
                UpdateStatus($"✅ Deployment complete for '{profileName}'.");
            }
        }

        private async void UnDeployProfile_Click(object sender, RoutedEventArgs e)
        {
            await UnDeployAllModels();
            if (ProfilesDropdown.SelectedItem is string profileName)
            {
               LoadModelListViewData(profileName);
            }

            UpdateStatus($"🧹 Undeployment complete for all models in all profiles.");
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

        private async void PasteModelPath_Click(object sender, RoutedEventArgs e)
        {
            var dataPackageView = Clipboard.GetContent();
            if (dataPackageView == null || !dataPackageView.Contains(StandardDataFormats.Text))
            {
                UpdateStatus("Clipboard does not contain text.");
                return;
            }

            var text = (await dataPackageView.GetTextAsync())?.Trim() ?? string.Empty;
            if (text.IsNullOrEmpty())
            {
                UpdateStatus("Clipboard text is empty.");
                return;
            }

            ModelPathTextBox.Text = text;
        }

        private async void AddModel_Click(object sender, RoutedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is string profileName && !ModelPathTextBox.Text.IsNullOrEmpty())
            {
                var path = ModelPathTextBox.Text;
                if (LooksLikeNugetUrl(path))
                {
                    await AddNugetModelToProfile(profileName, path);
                }
                else
                {
                    await AddModelToProfile(profileName, path);
                }
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
                MinHeight = 500
            };
            var dialog = new ContentDialog
            {
                Title = "Settings",
                Content = settingsPage,
                PrimaryButtonText = "Close",
                XamlRoot = this.Content.XamlRoot
            };

            dialog.MaxWidth = 900;
            dialog.MinWidth = 640;

            await dialog.ShowAsync();
        }

        public static void LogStartupInfo()
        {
            var version = GetDisplayVersion();
            var buildDate = GetBuildDate().ToString("yyyy-MM-dd HH:mm");


            var productName = "FO Dev Manager";

            UIMessageHelper.LogToUI($"ℹ️ {productName} {version} 🛠️ Build date: {buildDate}");
        }

        private async void ShowAboutDialog_Click(object sender, RoutedEventArgs e)
        {
            var version = GetDisplayVersion();
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
                Text = "© 2026 ECIT Peritus AS. All rights reserved.",
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

        private static string GetDisplayVersion()
        {
            var informationalVersion = Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!informationalVersion.IsNullOrEmpty())
                return informationalVersion.Split('+')[0];

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.1.0-beta";
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

        private async Task<bool> AddNugetModelToProfile(string profileName, string packageUrl)
        {
            var profile = LoadProfileByName(profileName);
            if (profile == null)
                return false;

            var repositories = profile.Repositories?
                .Where(repo => repo != null && !repo.RepoId.IsNullOrEmpty())
                .ToList() ?? new List<RepositoryModel>();

            if (repositories.Count == 0)
            {
                UpdateStatus($"No repositories are available in profile '{profileName}' for NuGet package installation.");
                return false;
            }

            var selectedRepository = repositories.Count == 1
                ? repositories[0]
                : await ShowNugetRepositoryPickerAsync(repositories, packageUrl);

            if (selectedRepository == null)
                return false;

            var ret = await RunOperationAsync(
                () => _profileService.AddNugetModel(profileName, selectedRepository.RepoId, string.Empty, packageUrl),
                "Add NuGet model to profile");

            if (!ret) return false;

            LoadModelListViewData(profileName);
            return true;
        }

        private async Task<RepositoryModel?> ShowNugetRepositoryPickerAsync(IReadOnlyList<RepositoryModel> repositories, string packageUrl)
        {
            var repositoryComboBox = new ComboBox
            {
                PlaceholderText = "Select repository",
                DisplayMemberPath = nameof(RepositoryModel.DisplayName),
                ItemsSource = repositories,
                SelectedIndex = 0,
                MinWidth = 320
            };

            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = "Choose which repository should own this package." });
            content.Children.Add(repositoryComboBox);
            content.Children.Add(new TextBlock { Text = "Package URL:" });
            content.Children.Add(new TextBlock
            {
                Text = packageUrl,
                TextWrapping = TextWrapping.Wrap
            });

            var dialog = new ContentDialog
            {
                Title = "Add NuGet package",
                Content = content,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return null;

            return repositoryComboBox.SelectedItem as RepositoryModel;
        }

        private static bool LooksLikeNugetUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return false;

            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
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

        private async Task<bool> UnDeployAllModels()
        {
            return await RunOperationAsync(() => _profileService.UndeployAllModels(), "Undeploy all models");
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

        private void SetDatabaseEditingState(bool isEditing)
        {
            DatabaseNameTextBox.IsReadOnly = !isEditing;
            DatabaseNameEditButton.Visibility = isEditing ? Visibility.Collapsed : Visibility.Visible;
            DatabaseNameApplyButton.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
            DatabaseNameCancelButton.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
            DatabaseNameHintText.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BeginDatabaseNameEdit()
        {
            if (ActiveProfile == null)
            {
                MessageLogger.Warning("Select a profile before editing the database name.");
                return;
            }

            SetDatabaseEditingState(true);
            DatabaseNameTextBox.Focus(FocusState.Programmatic);
            DatabaseNameTextBox.SelectAll();
        }

        private void CancelDatabaseNameEdit()
        {
            DatabaseNameTextBox.Text = ActiveProfile?.DatabaseName ?? string.Empty;
            SetDatabaseEditingState(false);
        }

        private async Task ApplyDatabaseNameChangeAsync()
        {
            if (ActiveProfile == null)
                return;

            var newDbString = (DatabaseNameTextBox.Text ?? string.Empty).Trim();
            if (newDbString.SameAs(ActiveProfile.DatabaseName))
            {
                MessageLogger.Info("Database name unchanged.");
                SetDatabaseEditingState(false);
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
                CancelDatabaseNameEdit();
                MessageLogger.Info("Database change cancelled.");
                return;
            }

            await RunOperationAsync(() =>
            {
                _profileService.SetDatabaseName(ActiveProfile.ProfileName, newDbString);
            }, "Apply database name");

            ActiveProfile.DatabaseName = newDbString;
            SetDatabaseEditingState(false);
        }

        private void DatabaseNameEditButton_Click(object sender, RoutedEventArgs e)
        {
            BeginDatabaseNameEdit();
        }

        private void DatabaseNameCancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelDatabaseNameEdit();
            MessageLogger.Info("Database change cancelled.");
        }

        private async void DatabaseNameApplyButton_Click(object sender, RoutedEventArgs e)
        {
            await ApplyDatabaseNameChangeAsync();
        }

        private async void DatabaseNameTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                CancelDatabaseNameEdit();
                MessageLogger.Info("Database change cancelled.");
                return;
            }

            if (e.Key != VirtualKey.Enter)
                return;

            await ApplyDatabaseNameChangeAsync();
            return;

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

            await RunOperationAsync(() =>
            {
                _profileService.SetDatabaseName(ActiveProfile.ProfileName, newDbString);
            }, "Apply database name");

            ActiveProfile.DatabaseName = newDbString;
            DatabaseNameTextBox.IsReadOnly = true;
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

            await RunOperationAsync(() => _profileService.GitResetProfile(profile), "Reset to Main", false);

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

            await RunOperationAsync(() => _profileService.TagReleaseProfile(profile), "Tag Release", false);

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
                    SetDatabaseEditingState(false);
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
            var maxDialogBodyWidth = Math.Max(440, Math.Min(620, this.Bounds.Width - 220));
            var availableDialogBodyHeight = Math.Max(560, this.Bounds.Height - 80);

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

            static TextBlock CreateFieldLabel(string text) => new()
            {
                Text = text,
                Opacity = 0.72,
                FontSize = 12
            };

            static Border CreateValueContainer(UIElement content) => new()
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(45, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(1),
                Child = content
            };

            static StackPanel CreateEditableField(string label, Control input) => new()
            {
                Spacing = 4,
                Children =
                {
                    CreateFieldLabel(label),
                    CreateValueContainer(input)
                }
            };

            static StackPanel CreateReadOnlyField(string label, string value) => new()
            {
                Spacing = 4,
                Children =
                {
                    CreateFieldLabel(label),
                    CreateValueContainer(new TextBlock
                    {
                        Text = value.IsNullOrEmpty() ? "(empty)" : value,
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeights.SemiBold
                    })
                }
            };

            static Border CreateSectionWithContent(string title, string description, params UIElement[] content)
            {
                var sectionBody = new StackPanel
                {
                    Spacing = 10
                };

                sectionBody.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold
                });

                sectionBody.Children.Add(new TextBlock
                {
                    Text = description,
                    Opacity = 0.72,
                    TextWrapping = TextWrapping.Wrap
                });

                foreach (var element in content)
                {
                    sectionBody.Children.Add(element);
                }

                return new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Child = sectionBody
                };
            }

            var behaviorPanel = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    CreateValueContainer(autoCheckoutToggle),
                    CreateValueContainer(autoStashToggle)
                }
            };

            var contentPanel = new StackPanel
            {
                MaxWidth = maxDialogBodyWidth,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = repositoryModel.RepoId,
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold
                    },
                    CreateSectionWithContent(
                        "Repository Details",
                        "Reference information for this repository.",
                        CreateReadOnlyField("Repository", repositoryModel.RepoId),
                        CreateReadOnlyField("Root", repositoryModel.RepoRootFolder)),
                    CreateSectionWithContent(
                        "General",
                        "Repository naming and branch preferences.",
                        CreateEditableField("Display name", displayNameTextBox),
                        CreateEditableField("Preferred branch", preferredBranchTextBox)),
                    CreateSectionWithContent(
                        "Git Behavior",
                        "Control what should happen when the profile loads or the branch is dirty.",
                        behaviorPanel),
                    CreateSectionWithContent(
                        "Task information",
                        "Current task information for this repository.",
                        CreateEditableField("Task", taskTextBox),
                        CreateEditableField("Task comment", taskCommentTextBox))
                }
            };

            var dialogContent = new Grid
            {
                MaxWidth = maxDialogBodyWidth,
                Padding = new Thickness(4, 0, 18, 0),
                Children =
                {
                    new ScrollViewer
                    {
                        Content = contentPanel,
                        MaxHeight = availableDialogBodyHeight,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                    }
                }
            };

            var dialog = new ContentDialog
            {
                Title = "Repository properties",
                Content = dialogContent,
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
            var canEditVersion = environmentModel.ModelType == ModelType.Source;
            var canChooseMainFoModel = environmentModel.ModelType == ModelType.Source;
            var maxDialogBodyWidth = Math.Max(440, Math.Min(620, this.Bounds.Width - 220));
            var availableDialogBodyHeight = Math.Max(560, this.Bounds.Height - 80);

            _modelVersionService.TryGetVersion(environmentModel, out var currentVersion);

            var mainFoToggle = new ToggleSwitch
            {
                IsOn = environmentModel.IsMainFOModel,
                Header = "Main FO model (solution root)",
                IsEnabled = canChooseMainFoModel
            };

            var versionMajorTextBox = new TextBox
            {
                Text = currentVersion.Major.ToString(CultureInfo.InvariantCulture),
                Width = 72
            };

            var versionMinorTextBox = new TextBox
            {
                Text = currentVersion.Minor.ToString(CultureInfo.InvariantCulture),
                Width = 72
            };

            var versionRevisionTextBox = new TextBox
            {
                Text = currentVersion.Revision.ToString(CultureInfo.InvariantCulture),
                Width = 72
            };

            var readOnlyVersionText = new TextBlock
            {
                Text = environmentViewModel.HasVersion ? environmentViewModel.VersionText : "Unavailable",
                FontWeight = FontWeights.SemiBold
            };

            static TextBlock CreateFieldLabel(string text) => new()
            {
                Text = text,
                Opacity = 0.72,
                FontSize = 12
            };

            static Border CreateValueContainer(UIElement content) => new()
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(45, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(1),
                Child = content
            };

            static StackPanel CreateReadOnlyField(string label, string value) => new()
            {
                Spacing = 4,
                Children =
                {
                    CreateFieldLabel(label),
                    CreateValueContainer(new TextBlock
                    {
                        Text = value.IsNullOrEmpty() ? "(empty)" : value,
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeights.SemiBold
                    })
                }
            };

            static Border CreateSectionWithContent(string title, string description, params UIElement[] content)
            {
                var sectionBody = new StackPanel
                {
                    Spacing = 10
                };

                sectionBody.Children.Add(new TextBlock
                {
                        Text = title,
                        FontSize = 15,
                        FontWeight = FontWeights.SemiBold
                    });

                sectionBody.Children.Add(new TextBlock
                {
                    Text = description,
                    Opacity = 0.72,
                    TextWrapping = TextWrapping.Wrap
                });

                foreach (var element in content)
                {
                    sectionBody.Children.Add(element);
                }

                return new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14),
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255)),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(28, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Child = sectionBody
                };
            }

            UIElement versionEditorOrValue = canEditVersion
                ? CreateValueContainer(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        versionMajorTextBox,
                        new TextBlock { Text = ".", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.72 },
                        versionMinorTextBox,
                        new TextBlock { Text = ".", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.72 },
                        versionRevisionTextBox
                    }
                })
                : CreateValueContainer(readOnlyVersionText);

            var versionSection = CreateSectionWithContent(
                "Version",
                canEditVersion
                    ? "Source model version can be edited here."
                    : "Compiled model details are read-only.",
                versionEditorOrValue);

            var modelDetailsSectionContent = new List<UIElement>
            {
                CreateReadOnlyField("Type", environmentModel.ModelType.ToString()),
                CreateReadOnlyField("Root", environmentModel.ModelRootFolder)
            };

            if (environmentModel.ModelType == ModelType.Source)
            {
                modelDetailsSectionContent.Add(CreateReadOnlyField("Project", environmentModel.ProjectFilePath));
                modelDetailsSectionContent.Add(CreateReadOnlyField("Metadata", environmentModel.MetadataFolder));
            }
            else
            {
                modelDetailsSectionContent.Add(CreateReadOnlyField("Compiled", environmentModel.CompiledModelFolder));
            }

            var contentPanel = new StackPanel
            {
                MaxWidth = maxDialogBodyWidth,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = environmentModel.ModelName,
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold
                    },
                    versionSection
                }
            };

            if (canChooseMainFoModel)
            {
                contentPanel.Children.Add(
                    CreateSectionWithContent(
                        "Solution role",
                        "Control whether this source model should be treated as the main solution root.",
                        CreateValueContainer(mainFoToggle)));
            }

            contentPanel.Children.Add(
                CreateSectionWithContent(
                    "Model Details",
                    "Reference information for this model.",
                    modelDetailsSectionContent.ToArray()));

            UIElement dialogContent = new Grid
            {
                MaxWidth = maxDialogBodyWidth,
                Padding = new Thickness(4, 0, 18, 0),
                Children =
                {
                    new ScrollViewer
                    {
                        Content = contentPanel,
                        MaxHeight = availableDialogBodyHeight,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                    }
                }
            };

            var dialog = new ContentDialog
            {
                Title = "Model properties",
                Content = dialogContent,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                XamlRoot = this.Content.XamlRoot

            };

            var dialogResult = await dialog.ShowAsync();
            if (dialogResult != ContentDialogResult.Primary)
                return;

            ModelVersion? sourceVersion = null;
            if (canEditVersion)
            {
                if (!int.TryParse(versionMajorTextBox.Text?.Trim(), out var major) || major < 0
                    || !int.TryParse(versionMinorTextBox.Text?.Trim(), out var minor) || minor < 0
                    || !int.TryParse(versionRevisionTextBox.Text?.Trim(), out var revision) || revision < 0)
                {
                    await new ContentDialog
                    {
                        Title = "Invalid version",
                        Content = "Version must use non-negative integers in the format major.minor.revision.",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    }.ShowAsync();

                    return;
                }

                sourceVersion = new ModelVersion(major, minor, revision);
            }

            environmentModel.IsMainFOModel = canChooseMainFoModel && mainFoToggle.IsOn;

            _profileService.UpdateModelProperties(environmentViewModel.ProfileName, environmentModel, sourceVersion);

            UIRefresh(environmentViewModel.ProfileName);
        }

        private void UIRefresh(string profileName)
        {
            LoadModelListViewData(profileName);
            CombinedList.UpdateLayout();
        }

    }

}


