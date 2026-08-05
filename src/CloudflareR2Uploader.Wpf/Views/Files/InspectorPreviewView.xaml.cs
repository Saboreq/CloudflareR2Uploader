using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CloudflareR2Uploader.Utilities;
using CloudflareR2Uploader.Wpf.ViewModels.Files;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CloudflareR2Uploader.Wpf.Views.Files
{
    /// <summary>
    /// The preview viewport.
    /// <para>
    /// Code-behind exists here for one reason: WebView2 has a real initialisation and
    /// disposal lifetime that cannot be expressed as a binding, and it must be created
    /// lazily so a user who never previews a PDF never pays for a browser process.
    /// </para>
    /// <para>
    /// Security posture, unchanged from the WinForms build: the control is pointed at a
    /// local cached file only, its user-data folder is the application's own, and every
    /// navigation to anything other than that exact file is cancelled. It never sees a
    /// signed or public R2 URL.
    /// </para>
    /// </summary>
    public partial class InspectorPreviewView : UserControl
    {
        private WebView2? _webView;
        private PreviewViewModel? _viewModel;
        private string? _pendingDocument;
        private bool _initializing;
        private bool _initializationFailed;

        public InspectorPreviewView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Unloaded += OnUnloaded;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _viewModel = e.NewValue as PreviewViewModel;
            if (_viewModel is null) return;

            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _ = ShowDocumentAsync(_viewModel.DocumentPath);
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PreviewViewModel.DocumentPath)) return;
            _ = ShowDocumentAsync(_viewModel?.DocumentPath);
        }

        private async Task ShowDocumentAsync(string? path)
        {
            _pendingDocument = path;

            if (string.IsNullOrEmpty(path))
            {
                if (_webView?.CoreWebView2 is not null) _webView.CoreWebView2.Navigate("about:blank");
                return;
            }

            if (_initializationFailed)
            {
                ShowRuntimeMissing();
                return;
            }

            if (_webView is null) CreateWebView();
            if (_webView is null) return;

            if (_webView.CoreWebView2 is null)
            {
                if (_initializing) return;

                _initializing = true;
                try
                {
                    // A dedicated user-data folder keeps cache, cookies and crash dumps out of
                    // the user's Edge profile and inside the application's own data folder.
                    AppPaths.EnsureDirectory(AppPaths.WebView2DataDirectory);

                    CoreWebView2Environment environment = await CoreWebView2Environment
                        .CreateAsync(browserExecutableFolder: null, userDataFolder: AppPaths.WebView2DataDirectory)
                        .ConfigureAwait(true);

                    await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
                    HardenWebView();
                }
                catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    _initializationFailed = true;
                    ShowRuntimeMissing();
                    return;
                }
                finally
                {
                    _initializing = false;
                }
            }

            // The selection may have moved on while the runtime was starting.
            if (!string.Equals(_pendingDocument, path, StringComparison.Ordinal)) return;
            if (!File.Exists(path)) return;

            _webView.CoreWebView2!.Navigate(new Uri(path).AbsoluteUri);
        }

        private void CreateWebView()
        {
            _webView = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.Transparent,
                Focusable = true
            };

            DocumentHost.Content = _webView;
        }

        private void HardenWebView()
        {
            if (_webView?.CoreWebView2 is null) return;

            CoreWebView2Settings settings = _webView.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = true;
            settings.AreHostObjectsAllowed = false;
            settings.IsWebMessageEnabled = false;

            // A PDF can contain links. Following one would take the embedded browser to an
            // arbitrary remote origin inside the application's own window, so every
            // navigation away from the cached file is refused.
            _webView.CoreWebView2.NavigationStarting += (sender, args) =>
            {
                if (IsAllowed(args.Uri)) return;
                args.Cancel = true;
            };

            _webView.CoreWebView2.NewWindowRequested += (sender, args) => args.Handled = true;
            _webView.CoreWebView2.DownloadStarting += (sender, args) => args.Cancel = true;
        }

        private bool IsAllowed(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return false;
            if (uri.StartsWith("about:blank", StringComparison.OrdinalIgnoreCase)) return true;

            string? expected = _pendingDocument;
            if (string.IsNullOrEmpty(expected)) return false;

            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed) || !parsed.IsFile) return false;

            return string.Equals(
                Path.GetFullPath(parsed.LocalPath),
                Path.GetFullPath(expected),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// WebView2 is optional. Without it the rest of the application keeps working and the
        /// document preview degrades to an honest message, exactly as the WinForms build did.
        /// </summary>
        private void ShowRuntimeMissing()
        {
            _viewModel?.ShowUnsupported(
                "PDF preview needs the WebView2 runtime",
                "Install the Microsoft Edge WebView2 Evergreen Runtime, or download the file to open it locally.");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;

            if (_webView is null) return;

            DocumentHost.Content = null;
            _webView.Dispose();
            _webView = null;
        }
    }
}
