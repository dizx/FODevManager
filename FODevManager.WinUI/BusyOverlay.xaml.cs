using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FODevManager.WinUI
{
    public sealed partial class BusyOverlay : UserControl
    {
        public BusyOverlay()
        {
            InitializeComponent();
            this.DataContextChanged += BusyOverlay_DataContextChanged;
        }

        private void BusyOverlay_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            FocusIfVisible();
        }

        private void FocusIfVisible()
        {
            if (OverlayRoot.Visibility == Visibility.Visible)
            {
                // Focus an element inside the overlay to trap tab navigation
                FocusSink.Focus(FocusState.Programmatic);
            }
        }

        private void OverlayRoot_PointerPressed(object sender, PointerRoutedEventArgs e) { e.Handled = true; }
        private void OverlayRoot_PointerReleased(object sender, PointerRoutedEventArgs e) { e.Handled = true; }

        // Gesture events
        private void OverlayRoot_Tapped(object sender, TappedRoutedEventArgs e) { e.Handled = true; }
        private void OverlayRoot_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) { e.Handled = true; }
        private void OverlayRoot_RightTapped(object sender, RightTappedRoutedEventArgs e) { e.Handled = true; }
        private void OverlayRoot_Holding(object sender, HoldingRoutedEventArgs e) { e.Handled = true; }

        // Keyboard
        private void OverlayRoot_KeyDown(object sender, KeyRoutedEventArgs e) { e.Handled = true; }
    }
}
