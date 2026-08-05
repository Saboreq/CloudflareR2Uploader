using System;
using System.ComponentModel;
using System.Windows;
using CloudflareR2Uploader.Wpf.Interop;

namespace CloudflareR2Uploader.Wpf.Views
{
    /// <summary>
    /// The application shell window.
    /// <para>
    /// Code-behind is limited to genuine view responsibilities: forcing the dark DWM caption
    /// and turning a close request into either hide-to-tray or a real exit. Everything else
    /// lives in <c>ShellViewModel</c>.
    /// </para>
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Set by the tray host. When it returns false the window hides instead of closing,
        /// which is what keeps the upload queue alive.
        /// </summary>
        public Func<bool>? CloseRequestHandler { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += OnSourceInitialized;
        }

        private void OnSourceInitialized(object? sender, EventArgs e) => ApplyCaptionTheme();

        /// <summary>Called by the theme service after the palette changes.</summary>
        public void ApplyCaptionTheme()
        {
            object? value = TryFindResource("Theme.IsDark");
            WindowChromeInterop.ApplyDarkTitleBar(this, value is not bool isDark || isDark);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (CloseRequestHandler is not null && !CloseRequestHandler())
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnClosing(e);
        }
    }
}
