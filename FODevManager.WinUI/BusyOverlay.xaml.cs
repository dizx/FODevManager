using FODevManager.WinUI.ViewModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Specialized;

namespace FODevManager.WinUI
{
    public sealed partial class BusyOverlay : UserControl
    {
        private BusyOverlayViewModel? _viewModel;
        private bool _autoScrollEnabled = true;
        public BusyOverlay()
        {
            InitializeComponent();

            Loaded += BusyOverlay_Loaded;
            Unloaded += BusyOverlay_Unloaded;
            DataContextChanged += BusyOverlay_DataContextChanged;
        }

        private void BusyOverlay_Loaded(object sender, RoutedEventArgs eventArgs)
        {
            // Track whether the user is at the bottom (auto-scroll enabled) or has scrolled up (auto-scroll disabled).
            if (LogScrollViewer != null)
            {
                LogScrollViewer.ViewChanged += LogScrollViewer_ViewChanged;
            }
            FocusIfVisible();
        }
        private void BusyOverlay_Unloaded(object sender, RoutedEventArgs eventArgs)
        {
            if (LogScrollViewer != null)
            {
                LogScrollViewer.ViewChanged -= LogScrollViewer_ViewChanged;
            }

            DetachFromViewModel();
        }
        private void BusyOverlay_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            DetachFromViewModel();

            _viewModel = DataContext as BusyOverlayViewModel;
            AttachToViewModel();

            FocusIfVisible();
        }
        private void FocusIfVisible()
        {
            if (OverlayRoot.Visibility == Visibility.Visible)
            {
                // Focus an element inside the overlay to trap tab navigation.
                FocusSink.Focus(FocusState.Programmatic);
            }
        }
        private void AttachToViewModel()
        {
            if (_viewModel == null)
                return;

            _viewModel.LogLines.CollectionChanged += LogLines_CollectionChanged;
        }

        private void DetachFromViewModel()
        {
            if (_viewModel == null)
                return;

            _viewModel.LogLines.CollectionChanged -= LogLines_CollectionChanged;
            _viewModel = null;
        }
        private void LogScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs eventArgs)
        {
            if (LogScrollViewer == null)
                return;

            var distanceToBottom = LogScrollViewer.ScrollableHeight - LogScrollViewer.VerticalOffset;

            // If the user is near the bottom, keep auto-scroll enabled.
            // If they scroll up, stop auto-scroll so they can read older lines.
            _autoScrollEnabled = distanceToBottom <= 16;
        }

        private void LogLines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
        {
            if (LogScrollViewer == null)
                return;

            // If the user scrolled up, do not force-scroll on new log lines.
            // Allow reset (clear) to re-anchor once content returns.
            if (!_autoScrollEnabled && eventArgs.Action != NotifyCollectionChangedAction.Reset)
                return;

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (LogScrollViewer == null)
                    return;

                LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null, disableAnimation: true);
            });
        }

        private void OverlayRoot_PointerPressed(object sender, PointerRoutedEventArgs eventArgs) => eventArgs.Handled = true;
        private void OverlayRoot_PointerReleased(object sender, PointerRoutedEventArgs eventArgs) => eventArgs.Handled = true;
        private void OverlayRoot_Tapped(object sender, TappedRoutedEventArgs eventArgs) => eventArgs.Handled = true;
        private void OverlayRoot_DoubleTapped(object sender, DoubleTappedRoutedEventArgs eventArgs) => eventArgs.Handled = true;
        private void OverlayRoot_RightTapped(object sender, RightTappedRoutedEventArgs eventArgs) => eventArgs.Handled = true;
        private void OverlayRoot_Holding(object sender, HoldingRoutedEventArgs eventArgs) => eventArgs.Handled = true;
        private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs eventArgs) => eventArgs.Handled = true;
    }
}
