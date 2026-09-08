using FODevManager.Messages;
using FODevManager.WinUI.Framework;
using FODevManager.WinUI.ViewModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;

namespace FODevManager.WinUI
{
    public sealed partial class MainWindow
    {
        private readonly ModelSelection<ProfileEnvironmentViewModel> _modelSelection = new();
        private bool _modelSelectionInvalidated;

        private List<ProfileEnvironmentViewModel> GetVisibleModels()
            => _groupingVm == null ? new() : _groupingVm.GitGroups
                .Where(group => group.IsExpanded)
                .SelectMany(group => group.Models)
                .Concat(_groupingVm.NonGitModels).ToList();

        private static bool IsModifierDown(VirtualKey key)
            => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

        private static bool IsRowAction(DependencyObject? source, ListViewItem row)
        {
            for (var current = source; current != null && current != row; current = VisualTreeHelper.GetParent(current))
            {
                if (current is ButtonBase)
                    return true;
            }

            return false;
        }

        private void ModelRow_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not ListViewItem row || row.DataContext is not ProfileEnvironmentViewModel model
                || IsRowAction(e.OriginalSource as DependencyObject, row))
                return;

            SelectModel(model);
            row.Focus(FocusState.Pointer);
            e.Handled = true;
        }

        private void ModelRow_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Space && e.Key != VirtualKey.Enter)
                return;

            if (sender is not ListViewItem row || row.DataContext is not ProfileEnvironmentViewModel model
                || IsRowAction(e.OriginalSource as DependencyObject, row))
                return;

            SelectModel(model);
            e.Handled = true;
        }

        private void SelectModel(ProfileEnvironmentViewModel model)
        {
            if (!string.Equals(ProfilesDropdown.SelectedItem as string, model.ProfileName, StringComparison.OrdinalIgnoreCase))
                return;

            _modelSelectionInvalidated = false;
            var previouslySelected = _modelSelection.Selected.ToList();
            _modelSelection.Select(model, GetVisibleModels(), IsModifierDown(VirtualKey.Shift), IsModifierDown(VirtualKey.Control));
            foreach (var item in previouslySelected.Concat(_modelSelection.Selected).Distinct())
                item.IsSelected = _modelSelection.Selected.Contains(item);

            UpdateModelSelectionActions();
        }

        private void ClearModelSelection()
        {
            foreach (var model in _modelSelection.Selected)
                model.IsSelected = false;

            _modelSelection.Clear();
            _modelSelectionInvalidated = false;
            UpdateModelSelectionActions();
        }

        private void ReconcileModelSelection(ModelsGroupingViewModel grouping)
        {
            var hadSelection = _modelSelection.Selected.Count > 0;
            var items = grouping.GitGroups.SelectMany(group => group.Models).Concat(grouping.NonGitModels).ToList();
            _modelSelection.Reconcile(items, (previous, current) =>
                string.Equals(previous.ProfileName, current.ProfileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(previous.ModelName, current.ModelName, StringComparison.OrdinalIgnoreCase)
                && previous.ModelType == current.ModelType
                && string.Equals(previous.MetadataFolder, current.MetadataFolder, StringComparison.OrdinalIgnoreCase)
                && string.Equals(previous.Model.CompiledModelFolder, current.Model.CompiledModelFolder, StringComparison.OrdinalIgnoreCase));

            foreach (var model in _modelSelection.Selected)
                model.IsSelected = true;

            foreach (var group in grouping.GitGroups)
            {
                var previous = _groupingVm?.GitGroups.FirstOrDefault(oldGroup =>
                    string.Equals(oldGroup.RepoRootFolder, group.RepoRootFolder, StringComparison.OrdinalIgnoreCase));
                if (previous != null)
                    group.IsExpanded = previous.IsExpanded;
            }

            // Losing the last selected target must not silently authorize an all-model operation
            _modelSelectionInvalidated |= hadSelection && _modelSelection.Selected.Count == 0;
            UpdateModelSelectionActions();
        }

        private void ClearModelSelection_Click(object sender, RoutedEventArgs e) => ClearModelSelection();

        private void CombinedList_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                ClearModelSelection();
                e.Handled = true;
            }
        }

        private void UpdateModelSelectionActions()
        {
            var count = _modelSelection.Selected.Count;
            var allowProfileActions = count <= 1;
            foreach (var button in new[] { CreateProfileButton, ImportProfileButton, SwitchProfileButton,
                RefreshProfileButton, GitActionsButton, DeleteProfileButton, OpenSolutionButton })
                button.IsEnabled = allowProfileActions;

            DeployProfileButton.IsEnabled = !_modelSelectionInvalidated;
            UnDeployProfileButton.IsEnabled = !_modelSelectionInvalidated;

            ToolTipService.SetToolTip(ClearModelSelectionButton, _modelSelectionInvalidated
                ? "Selected models changed during refresh. Select models again or clear selection to restore profile actions."
                : "Clear model selection (Esc)");
            ClearModelSelectionButton.Visibility = count == 0 && !_modelSelectionInvalidated ? Visibility.Collapsed : Visibility.Visible;
            ToolTipService.SetToolTip(DeployProfileButton, count == 0
                ? "Deploy all undeployed models in this profile" : $"Deploy {count} selected model(s)");
            ToolTipService.SetToolTip(UnDeployProfileButton, count == 0
                ? "Remove all deployed models in all profiles" : $"Undeploy {count} selected model(s)");
        }

        private async Task RunSelectedModelDeploymentAsync(bool deploy)
        {
            // Snapshot before awaiting so a refresh cannot change the operation's targets
            var models = _modelSelection.Selected.ToList();
            if (models.Count == 0 || !string.Equals(ProfilesDropdown.SelectedItem as string, models[0].ProfileName, StringComparison.OrdinalIgnoreCase))
                return;

            var profileName = models[0].ProfileName;
            var operation = deploy ? "Deploy" : "Undeploy";
            await BusyOps.TrySyncAsAsync(() =>
            {
                foreach (var model in models)
                {
                    try
                    {
                        if (deploy)
                        {
                            if (!_deploymentService.IsModelActuallyDeployed(model.Model)
                                && !_deploymentService.DeployModel(profileName, model.ModelName))
                                MessageLogger.Warning($"Could not deploy selected model '{model.ModelName}'");
                        }
                        else
                        {
                            if (_deploymentService.IsModelActuallyDeployed(model.Model))
                                _deploymentService.UnDeployModel(profileName, model.ModelName);
                            else
                                MessageLogger.Info($"Skipping '{model.ModelName}': the selected model's source is not deployed");
                        }
                    }
                    catch (Exception exception)
                    {
                        MessageLogger.Error($"{operation} failed for selected model '{model.ModelName}': {exception.Message}");
                    }
                }
            }, $"{operation} {models.Count} selected model(s)", shutdownServer: true);

            if (ProfilesDropdown.SelectedItem is string currentProfile && currentProfile == profileName)
                await RefreshProfileViewAsync(profileName);
        }
    }
}
