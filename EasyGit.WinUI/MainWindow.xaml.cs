using EasyGit.WinUI.Framework;
using FODevManager.Messages;
using FODevManager.Services;
using FODevManager.Services.EasyGit;
using FODevManager.Utils;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
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
        private StatusMessageSubscriber? _statusSubscriber;
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

            _statusSubscriber = new StatusMessageSubscriber(DispatcherQueue, message =>
            {
                StatusTextBlock.Text = string.IsNullOrWhiteSpace(message) ? "Ready." : message;
            });

            Closed += MainWindow_Closed;
            RepositoriesList.ItemsSource = _rows;
            LoadProfiles();
            StartAutoSync();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            _syncTimer?.Stop();
            _profileLoadCancellationTokenSource?.Cancel();
            _profileLoadCancellationTokenSource?.Dispose();
            _statusSubscriber?.Dispose();
            _syncLock.Dispose();
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
                            SetOperationStatus(branchSync);
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
                SetOperationStatus(op);
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

                SetOperationStatus(op);
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async void Commit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string repoId || _selectedProfileName.IsNullOrEmpty())
                return;

            await ReviewAndCommitAsync(repoId).ConfigureAwait(true);
        }

        private async Task ReviewAndCommitAsync(string repoId)
        {
            if (_selectedProfileName.IsNullOrEmpty())
                return;

            EasyGitCommitPreview preview = new();
            await RunBusyAsync("Preparing commit preview...", async () =>
            {
                preview = await _workflowService.GetCommitPreviewAsync(_selectedProfileName, repoId).ConfigureAwait(true);
            }).ConfigureAwait(true);

            if (!preview.CanCommit)
            {
                SetStatus(preview.Message, MessageType.Warning);
                return;
            }

            var panel = BuildCommitReviewPanel(preview, out var commitMessageBox);

            var dialog = new ContentDialog
            {
                Title = "Review and Commit",
                Content = panel,
                PrimaryButtonText = "Commit and Push",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var decision = await dialog.ShowAsync();
            if (decision != ContentDialogResult.Primary)
                return;

            await RunBusyAsync("Committing changes...", async () =>
            {
                var op = await _workflowService
                    .CommitWithMessageAsync(_selectedProfileName, repoId, commitMessageBox.Text?.Trim())
                    .ConfigureAwait(true);
                SetOperationStatus(op);
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async Task CreatePrAsync(string repoId)
        {
            if (_selectedProfileName.IsNullOrEmpty())
                return;

            await RunBusyAsync("Creating pull request...", async () =>
            {
                var op = await _workflowService.CreatePullRequestAsync(_selectedProfileName, repoId).ConfigureAwait(true);
                SetOperationStatus(op);
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async Task ViewPrAsync(string repoId)
        {
            if (_selectedProfileName.IsNullOrEmpty())
                return;

            var op = await _workflowService.OpenPullRequestAsync(_selectedProfileName, repoId).ConfigureAwait(true);
            SetOperationStatus(op);
        }

        private async void PrimaryPrAction_Click(SplitButton sender, SplitButtonClickEventArgs e)
        {
            if (!TryGetRepoIdFromElement(sender, out var repoId) || _selectedProfileName.IsNullOrEmpty())
                return;

            var row = _rows.FirstOrDefault(candidate => candidate.RepoId.SameAs(repoId));
            if (row?.HasPullRequest == true)
                await ViewPrAsync(repoId).ConfigureAwait(true);
            else
                await CreatePrAsync(repoId).ConfigureAwait(true);
        }

        private void OpenDevOps_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetRepoId(sender, out var repoId) || _selectedProfileName.IsNullOrEmpty())
                return;

            var profile = _fileService.LoadProfile(_selectedProfileName);
            var repository = (profile.Repositories ?? new List<FODevManager.Models.RepositoryModel>())
                .FirstOrDefault(candidate => candidate.RepoId.SameAs(repoId));

            var url = NormalizeDevOpsUrl(repository?.GitUrl);
            if (url.IsNullOrEmpty())
            {
                SetStatus("Could not resolve DevOps URL for this repository.", MessageType.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                SetStatus("Opened repository in DevOps.", MessageType.Highlight);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not open DevOps URL: {ex.Message}", MessageType.Error);
            }
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
                        SetStatus("Complete canceled.", MessageType.Info);
                        return;
                    }
                }

                var op = await _workflowService
                    .CompleteWorkflowAsync(_selectedProfileName, repoId, allowUnverified)
                    .ConfigureAwait(true);

                SetOperationStatus(op);
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private void GitActions_OpenSolution_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfileName.IsNullOrEmpty())
                return;

            var profile = _fileService.LoadProfile(_selectedProfileName);
            var solutionPath = (profile.SolutionFilePath ?? string.Empty).Trim();
            if (solutionPath.IsNullOrEmpty() || !File.Exists(solutionPath))
            {
                SetStatus($"Solution not found for profile '{_selectedProfileName}'.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(solutionPath) { UseShellExecute = true });
                SetStatus("Opened profile solution.", MessageType.Highlight);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not open solution: {ex.Message}", MessageType.Error);
            }
        }

        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsPage = new SettingsPage
            {
                MinWidth = 760,
                MinHeight = 520
            };

            var dialog = new ContentDialog
            {
                Title = "Options",
                Content = settingsPage,
                PrimaryButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            dialog.MaxWidth = 1100;
            dialog.MinWidth = 760;
            await dialog.ShowAsync();
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            var version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";

            var dialog = new ContentDialog
            {
                Title = "About EasyGit",
                Content = $"EasyGit helps manage your FO repositories and workflow stages.\nVersion: {version}",
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async void Advanced_UpdateBranch_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetRepoId(sender, out var repoId) || _selectedProfileName.IsNullOrEmpty())
                return;

            await RunBusyAsync("Updating feature branch...", async () =>
            {
                var op = await _workflowService.MergeMainIntoFeatureAsync(_selectedProfileName, repoId).ConfigureAwait(true);
                SetOperationStatus(op);

                var row = _rows.FirstOrDefault(candidate => candidate.RepoId.SameAs(repoId));
                if (op.RequiresManualReview && row != null)
                    await ShowConflictReviewNoticeAsync(row).ConfigureAwait(true);

                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private async void Advanced_ViewChangedFiles_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetRepoId(sender, out var repoId) || _selectedProfileName.IsNullOrEmpty())
                return;

            var row = _rows.FirstOrDefault(candidate => candidate.RepoId.SameAs(repoId));
            if (row == null || !row.CanViewChangedFiles)
                return;

            var changedFiles = _workflowService.GetChangedFiles(_selectedProfileName, repoId);
            var content = BuildChangedFilesPanel(changedFiles, minHeight: 420);

            var dialog = new ContentDialog
            {
                Title = $"Changed files - {row.DisplayName}",
                Content = content,
                PrimaryButtonText = "Commit...",
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                await ReviewAndCommitAsync(repoId).ConfigureAwait(true);
        }

        private FrameworkElement BuildCommitReviewPanel(EasyGitCommitPreview preview, out TextBox commitMessageBox)
        {
            var panel = new StackPanel
            {
                Spacing = 12,
                MinWidth = 760,
                MaxWidth = 920
            };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = "\uE70F", FontSize = 14, Opacity = 0.85 });
            header.Children.Add(new TextBlock
            {
                Text = "Review files and fine-tune the commit message before pushing.",
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.9
            });
            panel.Children.Add(header);

            panel.Children.Add(BuildChangeSummaryBadges(preview.ChangedFiles));
            panel.Children.Add(BuildChangedFilesPanel(preview.ChangedFiles, minHeight: 290));

            commitMessageBox = new TextBox
            {
                Header = "Commit message",
                Text = preview.ProposedCommitMessage,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 96
            };
            panel.Children.Add(commitMessageBox);

            return panel;
        }

        private FrameworkElement BuildChangedFilesPanel(IReadOnlyCollection<EasyGitChangedFile> changedFiles, double minHeight)
        {
            var container = new StackPanel { Spacing = 10 };

            if (changedFiles.Count == 0)
            {
                container.Children.Add(new TextBlock
                {
                    Text = "No modified files were detected.",
                    Opacity = 0.75
                });
            }
            else
            {
                AddChangedFilesSection(container, "Added", "\uE710", "Added", Color.FromArgb(255, 76, 175, 80), changedFiles);
                AddChangedFilesSection(container, "Deleted", "\uE74D", "Deleted", Color.FromArgb(255, 229, 57, 53), changedFiles);
                AddChangedFilesSection(container, "Changed", "\uE70F", "Changed", Color.FromArgb(255, 37, 99, 235), changedFiles);
            }

            var scroll = new ScrollViewer
            {
                Content = container,
                MinHeight = minHeight,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            return new Border
            {
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                Child = scroll
            };
        }

        private static FrameworkElement BuildChangeSummaryBadges(IReadOnlyCollection<EasyGitChangedFile> changedFiles)
        {
            var added = changedFiles.Count(file => file.ChangeType.SameAs("Added"));
            var deleted = changedFiles.Count(file => file.ChangeType.SameAs("Deleted"));
            var changed = changedFiles.Count(file => file.ChangeType.SameAs("Changed"));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(CreateBadge($"Added {added}", Color.FromArgb(255, 76, 175, 80)));
            row.Children.Add(CreateBadge($"Deleted {deleted}", Color.FromArgb(255, 229, 57, 53)));
            row.Children.Add(CreateBadge($"Changed {changed}", Color.FromArgb(255, 37, 99, 235)));
            row.Children.Add(CreateBadge($"Total {changedFiles.Count}", Color.FromArgb(255, 99, 102, 241)));
            return row;
        }

        private static Border CreateBadge(string text, Color color)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 3, 10, 3),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }
            };
        }

        private static void AddChangedFilesSection(
            Panel container,
            string title,
            string glyph,
            string changeType,
            Color color,
            IReadOnlyCollection<EasyGitChangedFile> changedFiles)
        {
            var files = changedFiles
                .Where(file => file.ChangeType.SameAs(changeType))
                .Select(file => file.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
                return;

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            header.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13, Foreground = new SolidColorBrush(color) });
            header.Children.Add(new TextBlock
            {
                Text = $"{title} ({files.Count})",
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color)
            });
            container.Children.Add(header);

            foreach (var path in files)
            {
                container.Children.Add(BuildChangedFileLine(path));
            }
        }

        private static TextBlock BuildChangedFileLine(string fullPath)
        {
            var (directoryPath, fileName) = SplitPathForDisplay(fullPath);

            var line = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                Opacity = 0.95
            };

            line.Inlines.Add(new Run { Text = "  - " });

            if (!directoryPath.IsNullOrEmpty())
            {
                line.Inlines.Add(new Run
                {
                    Text = directoryPath,
                    Foreground = new SolidColorBrush(Color.FromArgb(160, 120, 120, 120))
                });
            }

            line.Inlines.Add(new Run
            {
                Text = fileName,
                FontWeight = FontWeights.SemiBold
            });

            return line;
        }

        private static (string DirectoryPath, string FileName) SplitPathForDisplay(string fullPath)
        {
            if (fullPath.IsNullOrEmpty())
                return (string.Empty, string.Empty);

            var lastSeparator = Math.Max(fullPath.LastIndexOf('/'), fullPath.LastIndexOf('\\'));
            if (lastSeparator < 0)
                return (string.Empty, fullPath);

            var directoryPath = fullPath.Substring(0, lastSeparator + 1);
            var fileName = fullPath[(lastSeparator + 1)..];
            return (directoryPath, fileName);
        }

        private async void Advanced_ResetWorkflow_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetRepoId(sender, out var repoId) || _selectedProfileName.IsNullOrEmpty())
                return;

            var row = _rows.FirstOrDefault(candidate => candidate.RepoId.SameAs(repoId));
            var displayName = row?.DisplayName ?? repoId;

            var dialog = new ContentDialog
            {
                Title = "Reset workflow",
                Content = $"Reset workflow for '{displayName}'? Uncommitted changes will be stashed, PR metadata will be cleared, and repository will switch to main and pull latest.",
                PrimaryButtonText = "Reset",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;

            await RunBusyAsync("Resetting repository workflow...", async () =>
            {
                var op = await _workflowService.ResetRepositoryWorkflowAsync(_selectedProfileName, repoId).ConfigureAwait(true);
                SetOperationStatus(op);
                await RefreshStatusAsync(includeMainUpdateCheck: true).ConfigureAwait(true);
            }).ConfigureAwait(true);
        }

        private void Advanced_BrowseRepo_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetRepoId(sender, out var repoId) || _selectedProfileName.IsNullOrEmpty())
                return;

            if (!TryGetRepositoryPath(repoId, out var repoPath))
            {
                SetStatus("Repository path not found.", MessageType.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{repoPath}\"") { UseShellExecute = true });
                SetStatus("Opened repository folder.", MessageType.Highlight);
            }
            catch (Exception ex)
            {
                SetStatus($"Could not open repository folder: {ex.Message}", MessageType.Error);
            }
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
                    if (status.IsDirty) details.Add("changes pending");
                    if (status.NeedsAttention) details.Add("needs attention");
                    if (status.HasMainUpdates) details.Add("main updated");
                    if (status.IsProtectedBranch) details.Add("protected branch");

                    var row = new RepoRowViewModel
                    {
                        RepoId = status.Repository.RepoId,
                        DisplayName = status.Repository.DisplayName,
                        BranchInfo = $"{branch}",
                        StatusText = details.Count == 0 ? "ready" : string.Join(" | ", details),
                        AddedCount = status.AddedCount,
                        DeletedCount = status.DeletedCount,
                        ModifiedCount = status.ModifiedCount,
                        IsProtectedBranch = status.IsProtectedBranch,
                        HasMainUpdates = status.HasMainUpdates,
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

        private void SetStatus(string message, MessageType type = MessageType.Info)
        {
            var safeMessage = string.IsNullOrWhiteSpace(message) ? "Done." : message.Trim();

            switch (type)
            {
                case MessageType.Error:
                    MessageLogger.Error(safeMessage);
                    break;
                case MessageType.Warning:
                    MessageLogger.Warning(safeMessage);
                    break;
                case MessageType.Highlight:
                    MessageLogger.Highlight(safeMessage);
                    break;
                case MessageType.LogOnly:
                    MessageLogger.LogOnly(safeMessage);
                    break;
                default:
                    MessageLogger.Info(safeMessage);
                    break;
            }
        }

        private void SetOperationStatus(EasyGitOperationResult operation)
        {
            if (operation == null)
                return;

            if (operation.Succeeded)
            {
                SetStatus(operation.Message, MessageType.Highlight);
                return;
            }

            var level = operation.RequiresManualReview ? MessageType.Warning : MessageType.Error;
            SetStatus(operation.Message, level);
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

        private bool TryGetRepoId(object sender, out string repoId)
        {
            repoId = string.Empty;

            if (sender is not MenuFlyoutItem menuItem)
                return false;

            var value = menuItem.Tag?.ToString() ?? string.Empty;
            if (value.IsNullOrEmpty())
                return false;

            repoId = value;
            return true;
        }

        private static bool TryGetRepoIdFromElement(object sender, out string repoId)
        {
            repoId = string.Empty;

            if (sender is not FrameworkElement element)
                return false;

            var value = element.Tag?.ToString() ?? string.Empty;
            if (value.IsNullOrEmpty())
                return false;

            repoId = value;
            return true;
        }

        private bool TryGetRepositoryPath(string repoId, out string repoPath)
        {
            repoPath = string.Empty;

            var profile = _fileService.LoadProfile(_selectedProfileName);
            var repository = (profile.Repositories ?? new List<FODevManager.Models.RepositoryModel>())
                .FirstOrDefault(candidate => candidate.RepoId.SameAs(repoId));
            if (repository == null)
                return false;

            repoPath = (repository.RepoRootFolder ?? string.Empty).Trim();
            return !repoPath.IsNullOrEmpty() && Directory.Exists(repoPath);
        }

        private static string NormalizeDevOpsUrl(string? gitUrl)
        {
            var url = (gitUrl ?? string.Empty).Trim();
            if (url.IsNullOrEmpty())
                return string.Empty;

            if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                url = url[..^4];

            return url;
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
