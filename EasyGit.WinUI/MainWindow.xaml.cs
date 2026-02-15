using FODevManager.Messages;
using FODevManager.Services;
using FODevManager.Services.EasyGit;
using FODevManager.Utils;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinRT.Interop;

namespace EasyGit.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly FileService _fileService;
        private readonly AppConfig _config;
        private readonly IEasyGitWorkflowService _workflowService;
        private readonly ObservableCollection<RepoRowViewModel> _rows = new();
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private int _isAutoSyncRunning;

        private DispatcherQueueTimer? _syncTimer;
        private CancellationTokenSource? _profileLoadCancellationTokenSource;
        private string _selectedProfileName = string.Empty;

        public MainWindow(FileService fileService, AppConfig config, IEasyGitWorkflowService workflowService)
        {
            InitializeComponent();
            _fileService = fileService;
            _config = config;
            _workflowService = workflowService;

            SetTitleBar(AppTitleBar);
            var appWindow = GetAppWindowForCurrentWindow();
            appWindow.TitleBar.ExtendsContentIntoTitleBar = true;

            RepositoriesList.ItemsSource = _rows;
            LoadProfiles();
            StartAutoSync();
        }

        private AppWindow GetAppWindowForCurrentWindow()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            return AppWindow.GetFromWindowId(windowId);
        }

        private void LoadProfiles(string preferredProfile = "")
        {
            var profiles = _fileService.GetAllProfiles();
            var names = profiles.Select(profile => profile.ProfileName).OrderBy(name => name).ToList();
            ProfilesDropdown.ItemsSource = names;

            var selected = preferredProfile;
            if (selected.IsNullOrEmpty())
                selected = names.FirstOrDefault() ?? string.Empty;

            if (!selected.IsNullOrEmpty())
                ProfilesDropdown.SelectedItem = selected;
        }

        private async void ProfilesDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProfilesDropdown.SelectedItem is not string profileName)
                return;

            var previousProfile = _selectedProfileName;
            _selectedProfileName = profileName;

            _profileLoadCancellationTokenSource?.Cancel();
            _profileLoadCancellationTokenSource?.Dispose();
            _profileLoadCancellationTokenSource = new CancellationTokenSource();

            var token = _profileLoadCancellationTokenSource.Token;

            try
            {
                await RunBusyAsync($"Loading '{profileName}'...", async () =>
                {
                    if (!previousProfile.IsNullOrEmpty() && !previousProfile.SameAs(profileName))
                    {
                        var branchSync = await _workflowService
                            .SwitchBranchesForProfileAsync(previousProfile, profileName, token)
                            .ConfigureAwait(true);

                        if (!branchSync.Message.IsNullOrEmpty())
                            SetStatus(branchSync.Message);
                    }

                    await RefreshStatusAsync(includeMainUpdateCheck: false, cancellationToken: token).ConfigureAwait(true);
                    _ = RunPostProfileLoadSyncAsync(profileName, token);
                }).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // A new profile selection replaced this load.
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfileName.IsNullOrEmpty())
                return;

            await RunBusyAsync($"Refreshing '{_selectedProfileName}'...", async () =>
            {
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async void GitActions_Reset_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfileName.IsNullOrEmpty())
                return;

            var dialog = new ContentDialog
            {
                Title = "Git Reset",
                Content = "This resets all repositories in the selected profile to main and clears workflow progress. Continue?",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            await RunBusyAsync($"Resetting '{_selectedProfileName}'...", async () =>
            {
                var op = await _workflowService.ResetProfileWorkflowsAsync(_selectedProfileName).ConfigureAwait(true);
                SetStatus(op.Message);
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async void CreateFeature_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string repoId)
                return;

            if (_selectedProfileName.IsNullOrEmpty())
                return;

            var taskBox = new TextBox { PlaceholderText = "Task number (e.g. 12345)" };
            var commentBox = new TextBox { PlaceholderText = "Short task comment" };

            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(taskBox);
            panel.Children.Add(commentBox);

            var dialog = new ContentDialog
            {
                Title = "Create Task/Feature Branch",
                Content = panel,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            await RunBusyAsync("Creating feature branch...", async () =>
            {
                var op = await _workflowService
                    .CreateFeatureBranchAsync(_selectedProfileName, repoId, taskBox.Text.Trim(), commentBox.Text.Trim())
                    .ConfigureAwait(true);

                SetStatus(op.Message);
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async void Commit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string repoId || _selectedProfileName.IsNullOrEmpty())
                return;

            await RunBusyAsync("Committing changes...", async () =>
            {
                var op = await _workflowService.CommitAsync(_selectedProfileName, repoId).ConfigureAwait(true);
                SetStatus(op.Message);
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async void CreatePr_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string repoId || _selectedProfileName.IsNullOrEmpty())
                return;

            await RunBusyAsync("Creating pull request...", async () =>
            {
                var op = await _workflowService.CreatePullRequestAsync(_selectedProfileName, repoId).ConfigureAwait(true);
                SetStatus(op.Message);
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async void ViewPr_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string repoId || _selectedProfileName.IsNullOrEmpty())
                return;

            var op = await _workflowService.OpenPullRequestAsync(_selectedProfileName, repoId).ConfigureAwait(true);
            SetStatus(op.Message);
        }

        private async void Complete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string repoId || _selectedProfileName.IsNullOrEmpty())
                return;

            await RunBusyAsync("Completing workflow...", async () =>
            {
                var prState = await _workflowService.GetPullRequestStateAsync(_selectedProfileName, repoId).ConfigureAwait(true);
                var allowUnverified = false;

                if (!prState.CanVerify)
                {
                    allowUnverified = await ConfirmCompleteWhenPrCannotBeVerifiedAsync(prState.Message).ConfigureAwait(true);
                    if (!allowUnverified)
                    {
                        SetStatus("Complete canceled.");
                        return;
                    }
                }

                var op = await _workflowService
                    .CompleteWorkflowAsync(_selectedProfileName, repoId, allowUnverified)
                    .ConfigureAwait(true);

                SetStatus(op.Message);
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private void StartAutoSync()
        {
            _syncTimer = DispatcherQueue.CreateTimer();
            _syncTimer.Interval = TimeSpan.FromMinutes(Math.Max(1, _config.GitAutoSyncIntervalMinutes));
            _syncTimer.Tick += async (_, _) => await AutoSyncTickAsync().ConfigureAwait(true);
            _syncTimer.Start();
        }

        private async Task AutoSyncTickAsync()
        {
            if (_selectedProfileName.IsNullOrEmpty())
                return;

            if (Interlocked.Exchange(ref _isAutoSyncRunning, 1) == 1)
                return;

            try
            {
                var statuses = await Task.Run(
                        () => _workflowService.GetRepositoryStatuses(_selectedProfileName, includeMainUpdateCheck: false))
                    .ConfigureAwait(true);

                foreach (var status in statuses)
                {
                    await _workflowService.AutoSyncRepositoryAsync(_selectedProfileName, status.Repository.RepoId).ConfigureAwait(true);
                }

                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);

                foreach (var row in _rows.Where(candidate => candidate.StatusText.Contains("main updated", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    await PromptMergeMainAsync(row).ConfigureAwait(true);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _isAutoSyncRunning, 0);
            }
        }

        private async Task PromptMergeMainAsync(RepoRowViewModel row)
        {
            var dialog = new ContentDialog
            {
                Title = "main is updated",
                Content = $"Repository '{row.DisplayName}' has updates in main. Update your feature branch now?",
                PrimaryButtonText = "Update feature branch",
                CloseButtonText = "Later",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            await RunBusyAsync("Merging main into feature...", async () =>
            {
                var op = await _workflowService.MergeMainIntoFeatureAsync(_selectedProfileName, row.RepoId).ConfigureAwait(true);
                SetStatus(op.Message);

                if (op.RequiresManualReview)
                    await ShowConflictReviewNoticeAsync(row).ConfigureAwait(true);

                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async Task ShowConflictReviewNoticeAsync(RepoRowViewModel row)
        {
            var reviewDialog = new ContentDialog
            {
                Title = "Conflict needs review",
                Content = $"EasyGit could not safely auto-resolve conflicts for '{row.DisplayName}'. Open your merge tool and review the diff.",
                PrimaryButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };

            await reviewDialog.ShowAsync();
        }

        private async Task<bool> ConfirmCompleteWhenPrCannotBeVerifiedAsync(string message)
        {
            var detail = message.IsNullOrEmpty()
                ? "PR merge status could not be verified."
                : message;

            var dialog = new ContentDialog
            {
                Title = "Complete without merge verification?",
                Content = $"{detail}\n\nYou can still continue and complete this workflow manually.",
                PrimaryButtonText = "Complete Anyway",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task RefreshStatusAsync(bool includeMainUpdateCheck, CancellationToken cancellationToken = default)
        {
            if (_selectedProfileName.IsNullOrEmpty())
                return;

            await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(true);

            try
            {
                var profileName = _selectedProfileName;

                var statuses = await Task.Run(
                        () => _workflowService.GetRepositoryStatuses(profileName, includeMainUpdateCheck),
                        cancellationToken)
                    .ConfigureAwait(true);

                cancellationToken.ThrowIfCancellationRequested();

                var orderedStatuses = statuses
                    .OrderBy(status => status.Repository.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _rows.Clear();
                foreach (var status in orderedStatuses)
                {
                    var branch = status.Branch.IsNullOrEmpty() ? "(unknown)" : status.Branch;

                    var details = new List<string>();
                    if (status.IsDirty) details.Add("dirty");
                    if (status.NeedsAttention) details.Add("needs attention");
                    if (status.HasMainUpdates) details.Add("main updated");
                    if (status.IsProtectedBranch) details.Add("protected branch");

                    var row = new RepoRowViewModel
                    {
                        RepoId = status.Repository.RepoId,
                        DisplayName = status.Repository.DisplayName,
                        BranchInfo = $"Branch: {branch}",
                        StatusText = details.Count == 0 ? "ready" : string.Join(" | ", details),
                        AddedCount = status.AddedCount,
                        DeletedCount = status.DeletedCount,
                        ModifiedCount = status.ModifiedCount,
                        IsProtectedBranch = status.IsProtectedBranch,
                        WorkflowStage = status.WorkflowStage,
                        WorkflowText = status.WorkflowText,
                        PullRequestUrl = status.PullRequestUrl
                    };

                    _rows.Add(row);
                }
            }
            finally
            {
                _syncLock.Release();
            }
        }

        private void SetStatus(string message)
        {
            StatusTextBlock.Text = string.IsNullOrWhiteSpace(message) ? "Done." : message;
            MessageLogger.Info(StatusTextBlock.Text);
        }

        private async Task RunPostProfileLoadSyncAsync(string profileName, CancellationToken cancellationToken)
        {
            if (profileName.IsNullOrEmpty() || cancellationToken.IsCancellationRequested)
                return;

            try
            {
                await AutoSyncTickAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            catch (Exception exception)
            {
                MessageLogger.Warning($"Background profile sync failed: {exception.Message}");
            }
        }

        private async Task RunBusyAsync(string text, Func<Task> action)
        {
            ShowLoading(text);
            try
            {
                await action().ConfigureAwait(true);
            }
            finally
            {
                HideLoading();
            }
        }

        private void ShowLoading(string text)
        {
            LoadingTextBlock.Text = text;
            LoadingRing.IsActive = true;
            LoadingOverlay.Visibility = Visibility.Visible;
        }

        private void HideLoading()
        {
            LoadingRing.IsActive = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
