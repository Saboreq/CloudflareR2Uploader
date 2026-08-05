using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CloudflareR2Uploader.Wpf.Interop;
using CloudflareR2Uploader.Wpf.ViewModels.Dialogs;

namespace CloudflareR2Uploader.Wpf.Views.Dialogs
{
    /// <summary>
    /// Base window for every modal dialog.
    /// <para>
    /// It owns the three view responsibilities a dialog genuinely has and nothing else:
    /// forcing the dark title bar, trapping Tab inside itself, and translating the view
    /// model's <see cref="IDialogCloseRequester.CloseRequested"/> into
    /// <see cref="Window.DialogResult"/>. No application logic lives here.
    /// </para>
    /// </summary>
    public class ThemedDialogWindow : Window
    {
        private IDialogCloseRequester? _requester;

        public ThemedDialogWindow()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);

            DataContextChanged += OnDataContextChanged;
            SourceInitialized += OnSourceInitialized;
            Closed += OnClosed;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            // Without this the caption stays light while the client area is #08090E.
            WindowChromeInterop.ApplyDarkTitleBar(this, ThemeIsDark());
        }

        private bool ThemeIsDark()
        {
            object? value = TryFindResource("Theme.IsDark");
            return value is not bool isDark || isDark;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_requester is not null) _requester.CloseRequested -= OnCloseRequested;

            _requester = e.NewValue as IDialogCloseRequester;
            if (_requester is not null) _requester.CloseRequested += OnCloseRequested;
        }

        private void OnCloseRequested(object? sender, bool result)
        {
            // DialogResult may only be assigned on a window shown with ShowDialog.
            if (ComponentDispatcher.IsThreadModal) DialogResult = result;
            Close();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_requester is not null) _requester.CloseRequested -= OnCloseRequested;
            _requester = null;
        }
    }
}
