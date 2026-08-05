using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using CloudflareR2Uploader.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudflareR2Uploader.Wpf.ViewModels.Files
{
    /// <summary>
    /// Which placeholder or content the preview viewport is showing. The box keeps its size
    /// in every state, as the reference requires, so the inspector never jumps.
    /// </summary>
    public enum PreviewState
    {
        Empty = 0,
        Loading = 1,
        Image = 2,
        Text = 3,
        Pdf = 4,
        Unsupported = 5,
        Error = 6
    }

    /// <summary>
    /// The inspector's preview viewport, implementing the INSPECTOR PREVIEW block of
    /// Images/09-design-system-components.png.
    /// <para>
    /// Preview content is fetched through the authenticated SDK into a randomised
    /// session directory by the shared <see cref="R2PreviewService"/>. Nothing here ever
    /// navigates to a signed or public R2 URL, and HTML and SVG are shown as source text,
    /// never executed.
    /// </para>
    /// </summary>
    public sealed partial class PreviewViewModel : ObservableObject, IDisposable
    {
        /// <summary>
        /// Decoding a 20 MP photograph at full resolution to fill a 340 px box wastes tens of
        /// megabytes. WPF can decode straight to a bounded width instead.
        /// </summary>
        private const int MaxDecodeWidth = 1600;

        private static readonly double[] ZoomSteps = { 0.25, 0.5, 0.75, 1.0, 1.5, 2.0, 3.0, 4.0 };

        private readonly R2PreviewService _preview;
        private readonly IProcessLauncher _launcher;
        private readonly IToastService _toasts;
        private readonly IAppSession _session;
        private readonly ILoggingService _log;

        private int _version;
        private bool _disposed;

        public PreviewViewModel(
            R2PreviewService preview,
            IProcessLauncher launcher,
            IToastService toasts,
            IAppSession session,
            ILoggingService log)
        {
            _preview = preview;
            _launcher = launcher;
            _toasts = toasts;
            _session = session;
            _log = log;
        }

        [ObservableProperty]
        private PreviewState _state = PreviewState.Empty;

        [ObservableProperty]
        private string _placeholderTitle = "No object selected";

        [ObservableProperty]
        private string _placeholderCaption = "Pick a file to see its preview and metadata";

        [ObservableProperty]
        private BitmapSource? _image;

        [ObservableProperty]
        private string? _text;

        [ObservableProperty]
        private bool _isTruncated;

        /// <summary>Local file the PDF host navigates to. Never a remote URL.</summary>
        [ObservableProperty]
        private string? _documentPath;

        [ObservableProperty]
        private double _zoom = 1.0;

        [ObservableProperty]
        private bool _isFitToBox = true;

        [ObservableProperty]
        private string _dimensionsText = string.Empty;

        public int PixelWidth { get; private set; }

        public int PixelHeight { get; private set; }

        /// <summary>The object currently previewed, so Open and Download know their subject.</summary>
        private R2BrowserItem? _item;

        public bool CanZoom => State == PreviewState.Image;

        public bool CanOpen => _item is not null && !_item.IsFolder;

        /// <summary>Asks the Files view model to download the previewed object.</summary>
        public event EventHandler? DownloadRequested;

        /// <summary>Asks the inspector to retry the failed load.</summary>
        public event EventHandler? RetryRequested;

        // -------------------------------------------------------------------------- states

        public void ShowNothingSelected(
            string title = "No object selected",
            string caption = "Pick a file to see its preview and metadata")
        {
            Interlocked.Increment(ref _version);
            Reset();
            State = PreviewState.Empty;
            PlaceholderTitle = title;
            PlaceholderCaption = caption;
        }

        public void ShowUnsupported(string title, string caption)
        {
            Reset();
            State = PreviewState.Unsupported;
            PlaceholderTitle = title;
            PlaceholderCaption = caption;
        }

        public void ShowError(string message)
        {
            Reset();
            State = PreviewState.Error;
            PlaceholderTitle = "Preview failed";
            PlaceholderCaption = message;
        }

        // ------------------------------------------------------------------------ loading

        /// <summary>
        /// Loads a preview for <paramref name="item"/>.
        /// <paramref name="requestVersion"/> comes from the inspector, so a slow response for
        /// a row the user has already moved past is discarded instead of painting.
        /// </summary>
        public async Task LoadAsync(
            R2BrowserItem item, string? contentType, int requestVersion, CancellationToken cancellationToken)
        {
            _item = item;
            int localVersion = Interlocked.Increment(ref _version);

            Reset();
            State = PreviewState.Loading;
            PlaceholderTitle = "Fetching preview…";
            PlaceholderCaption = string.Empty;

            ProfileToken profile = _session.Token;

            try
            {
                R2PreviewResult result = await _preview
                    .GetAsync(_session.Settings, _session.Credentials, item, contentType, cancellationToken)
                    .ConfigureAwait(true);

                if (!IsCurrent(localVersion, profile)) return;

                Apply(item, result);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer selection; the newer request owns the viewport.
            }
            catch (Exception ex)
            {
                if (!IsCurrent(localVersion, profile)) return;

                FriendlyError friendly = FriendlyErrorService.Describe(ex, _session.Settings, item.Key);
                ShowError(friendly.Headline);
                _log?.Warning("Preview.Load", "Preview failed: " + friendly.Headline);
            }
        }

        private bool IsCurrent(int version, ProfileToken profile) =>
            !_disposed && _version == version && _session.Token == profile;

        private void Apply(R2BrowserItem item, R2PreviewResult result)
        {
            switch (result.Kind)
            {
                case R2PreviewKind.Image:
                    ApplyImage(result);
                    return;

                case R2PreviewKind.Pdf:
                    if (string.IsNullOrEmpty(result.LocalPath))
                    {
                        ShowUnsupported("No preview available", result.Warning ?? "The document could not be fetched.");
                        return;
                    }

                    State = PreviewState.Pdf;
                    DocumentPath = result.LocalPath;
                    return;

                case R2PreviewKind.Unsupported:
                    ShowUnsupported(
                        "No preview for " + DescribeExtension(item),
                        result.Warning ?? "Download it to open locally");
                    return;

                default:
                    // Text, JSON, XML, CSV, Markdown, and HTML/SVG shown as source.
                    if (!string.IsNullOrEmpty(result.Warning) && string.IsNullOrEmpty(result.Text))
                    {
                        ShowUnsupported("No preview available", result.Warning!);
                        return;
                    }

                    State = PreviewState.Text;
                    Text = result.Text ?? string.Empty;
                    IsTruncated = result.Truncated;
                    return;
            }
        }

        private void ApplyImage(R2PreviewResult result)
        {
            if (string.IsNullOrEmpty(result.LocalPath) || !File.Exists(result.LocalPath))
            {
                ShowUnsupported("No preview available", result.Warning ?? "The image could not be fetched.");
                return;
            }

            try
            {
                BitmapImage bitmap = new();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;      // release the file handle
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.UriSource = new Uri(result.LocalPath, UriKind.Absolute);

                if (result.PixelWidth is > MaxDecodeWidth) bitmap.DecodePixelWidth = MaxDecodeWidth;

                bitmap.EndInit();
                bitmap.Freeze();

                Image = bitmap;
                DocumentPath = result.LocalPath;
                PixelWidth = result.PixelWidth ?? bitmap.PixelWidth;
                PixelHeight = result.PixelHeight ?? bitmap.PixelHeight;

                DimensionsText = PixelWidth > 0 && PixelHeight > 0
                    ? PixelWidth.ToString(CultureInfo.CurrentCulture) + " × " +
                      PixelHeight.ToString(CultureInfo.CurrentCulture)
                    : string.Empty;

                State = PreviewState.Image;
                IsFitToBox = true;
                Zoom = 1.0;
                UpdateZoomAffordances();
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or
                                           UnauthorizedAccessException or OutOfMemoryException)
            {
                ShowUnsupported("This image could not be decoded", "Download it to open locally");
            }
        }

        private static string DescribeExtension(R2BrowserItem item)
        {
            string extension = Path.GetExtension(item.DisplayName ?? item.Key ?? string.Empty);
            return extension.Length > 1 ? extension : "this object";
        }

        // ------------------------------------------------------------------------ commands

        private bool CanChangeZoom() => State == PreviewState.Image;

        [RelayCommand(CanExecute = nameof(CanChangeZoom))]
        private void ZoomIn()
        {
            IsFitToBox = false;
            foreach (double step in ZoomSteps)
            {
                if (step > Zoom + 0.001) { Zoom = step; return; }
            }
            Zoom = ZoomSteps[^1];
        }

        [RelayCommand(CanExecute = nameof(CanChangeZoom))]
        private void ZoomOut()
        {
            IsFitToBox = false;
            for (int index = ZoomSteps.Length - 1; index >= 0; index--)
            {
                if (ZoomSteps[index] < Zoom - 0.001) { Zoom = ZoomSteps[index]; return; }
            }
            Zoom = ZoomSteps[0];
        }

        [RelayCommand(CanExecute = nameof(CanChangeZoom))]
        private void Fit()
        {
            IsFitToBox = true;
            Zoom = 1.0;
        }

        [RelayCommand(CanExecute = nameof(CanChangeZoom))]
        private void ActualSize()
        {
            IsFitToBox = false;
            Zoom = 1.0;
        }

        [RelayCommand]
        private void Open()
        {
            // Opening means opening the local cached copy, never navigating to R2.
            string? path = DocumentPath ?? (_item is null ? null : null);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    _launcher.Open(path);
                    return;
                }
                catch (Exception)
                {
                    _toasts.ShowError("Windows could not open the cached preview file.");
                    return;
                }
            }

            // Nothing is cached locally for this kind, so the honest action is to download it.
            DownloadRequested?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void Download() => DownloadRequested?.Invoke(this, EventArgs.Empty);

        [RelayCommand]
        private void Retry() => RetryRequested?.Invoke(this, EventArgs.Empty);

        // ------------------------------------------------------------------------- helpers

        partial void OnStateChanged(PreviewState value)
        {
            OnPropertyChanged(nameof(CanZoom));
            OnPropertyChanged(nameof(CanOpen));
            UpdateZoomAffordances();
        }

        private void UpdateZoomAffordances()
        {
            ZoomInCommand.NotifyCanExecuteChanged();
            ZoomOutCommand.NotifyCanExecuteChanged();
            FitCommand.NotifyCanExecuteChanged();
            ActualSizeCommand.NotifyCanExecuteChanged();
        }

        private void Reset()
        {
            Image = null;
            Text = null;
            DocumentPath = null;
            IsTruncated = false;
            DimensionsText = string.Empty;
            PixelWidth = 0;
            PixelHeight = 0;
            Zoom = 1.0;
            IsFitToBox = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Interlocked.Increment(ref _version);
            Reset();
            _item = null;
        }
    }
}
