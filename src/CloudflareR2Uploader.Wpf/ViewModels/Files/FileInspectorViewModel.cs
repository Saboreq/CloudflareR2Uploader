using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using CloudflareR2Uploader.Wpf.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudflareR2Uploader.Wpf.ViewModels.Files
{
    public enum InspectorTab
    {
        Overview = 0,
        Metadata = 1,
        Sharing = 2
    }

    /// <summary>One label/value pair inside a metadata group, with an optional copy action.</summary>
    public sealed partial class MetadataEntry : ObservableObject
    {
        private readonly IClipboardService? _clipboard;
        private readonly IToastService? _toasts;

        public MetadataEntry(string label, string value, bool isMono = false,
            IClipboardService? clipboard = null, IToastService? toasts = null)
        {
            Label = label;
            Value = value;
            IsMonospace = isMono;
            _clipboard = clipboard;
            _toasts = toasts;
        }

        public string Label { get; }

        public string Value { get; }

        public bool IsMonospace { get; }

        public bool CanCopy => _clipboard is not null && !string.IsNullOrEmpty(Value);

        [RelayCommand]
        private void Copy()
        {
            if (_clipboard is null) return;
            _clipboard.SetText(Value);
            _toasts?.ShowSuccess(Label + " copied to clipboard");
        }
    }

    public sealed class MetadataGroup
    {
        public MetadataGroup(string header, IReadOnlyList<MetadataEntry> entries)
        {
            Header = header;
            Entries = entries;
        }

        public string Header { get; }

        public IReadOnlyList<MetadataEntry> Entries { get; }

        public bool HasEntries => Entries.Count > 0;
    }

    public sealed record SelectionTypeBreakdown(string TypeName, string CountText, string SizeText);

    /// <summary>
    /// The docked inspector, implementing Images/02-file-inspector.png and the
    /// multiple-selection variant in Images/03-multi-selection-context-menu.png.
    /// </summary>
    public sealed partial class FileInspectorViewModel : ObservableObject, IDisposable
    {
        private readonly IAppSession _session;
        private readonly IClipboardService _clipboard;
        private readonly IProcessLauncher _launcher;
        private readonly IToastService _toasts;
        private readonly ILoggingService _log;
        private readonly R2ObjectDetailsService _details;
        private readonly PublicUrlService _publicUrls = new();

        private CancellationTokenSource? _loadCancellation;

        /// <summary>
        /// Incremented on every request. A response whose version is no longer current is
        /// dropped, which is what stops a slow metadata call painting over a newer selection.
        /// </summary>
        private int _requestVersion;

        private string _currentIdentity = string.Empty;
        private bool _disposed;

        public FileInspectorViewModel(
            IAppSession session,
            IClipboardService clipboard,
            IProcessLauncher launcher,
            IToastService toasts,
            ILoggingService log,
            R2ObjectDetailsService details,
            PreviewViewModel preview)
        {
            _session = session;
            _clipboard = clipboard;
            _launcher = launcher;
            _toasts = toasts;
            _log = log;
            _details = details;

            Preview = preview;
            Groups = new ReadOnlyObservableCollection<MetadataGroup>(_groups);
            MetadataGroups = new ReadOnlyObservableCollection<MetadataGroup>(_metadataGroups);
            SelectedObjects = new ReadOnlyObservableCollection<FileRowViewModel>(_selectedObjects);
            TypeBreakdown = new ReadOnlyObservableCollection<SelectionTypeBreakdown>(_typeBreakdown);
        }

        public PreviewViewModel Preview { get; }

        private readonly ObservableCollection<MetadataGroup> _groups = new();

        /// <summary>FILE DETAILS and OBJECT, shown on the Overview tab.</summary>
        public ReadOnlyObservableCollection<MetadataGroup> Groups { get; }

        private readonly ObservableCollection<MetadataGroup> _metadataGroups = new();

        /// <summary>Standard headers, custom metadata and extracted metadata, on the Metadata tab.</summary>
        public ReadOnlyObservableCollection<MetadataGroup> MetadataGroups { get; }

        private readonly ObservableCollection<FileRowViewModel> _selectedObjects = new();

        public ReadOnlyObservableCollection<FileRowViewModel> SelectedObjects { get; }

        private readonly ObservableCollection<SelectionTypeBreakdown> _typeBreakdown = new();

        public ReadOnlyObservableCollection<SelectionTypeBreakdown> TypeBreakdown { get; }

        [ObservableProperty]
        private InspectorTab _selectedTab = InspectorTab.Overview;

        [ObservableProperty]
        private bool _hasSelection;

        [ObservableProperty]
        private bool _isMultiSelection;

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _subtitle = string.Empty;

        [ObservableProperty]
        private string _kindBadge = string.Empty;

        [ObservableProperty]
        private string _iconKey = "Icon.File";

        // Multiple selection
        [ObservableProperty]
        private string _totalSizeText = string.Empty;

        [ObservableProperty]
        private string _objectCountText = string.Empty;

        [ObservableProperty]
        private string _downloadLabel = "Download";

        // Sharing tab
        [ObservableProperty]
        private bool _isPublic;

        [ObservableProperty]
        private string _publicUrl = string.Empty;

        [ObservableProperty]
        private string _visibilityText = "Private";

        [ObservableProperty]
        private string? _errorMessage;

        public bool HasPublicUrl => !string.IsNullOrEmpty(PublicUrl);

        /// <summary>Raised when the inspector's close button is pressed.</summary>
        public event EventHandler? CloseRequested;

        /// <summary>Asks the Files view model to run a command on the current selection.</summary>
        public event EventHandler<string>? ActionRequested;

        // -------------------------------------------------------------------------- entry

        public void Clear()
        {
            CancelLoad();

            HasSelection = false;
            IsMultiSelection = false;
            IsLoading = false;
            ErrorMessage = null;
            Title = string.Empty;
            Subtitle = string.Empty;
            KindBadge = string.Empty;
            PublicUrl = string.Empty;
            _currentIdentity = string.Empty;

            _groups.Clear();
            _metadataGroups.Clear();
            _selectedObjects.Clear();
            _typeBreakdown.Clear();

            Preview.ShowNothingSelected();
            OnPropertyChanged(nameof(HasPublicUrl));
        }

        public void Show(IReadOnlyList<R2BrowserItem> selection, string currentPrefix)
        {
            if (selection is null || selection.Count == 0)
            {
                Clear();
                return;
            }

            if (selection.Count > 1)
            {
                ShowMultiple(selection, currentPrefix);
                return;
            }

            _ = ShowSingleAsync(selection[0]);
        }

        // ---------------------------------------------------------------- single selection

        private async Task ShowSingleAsync(R2BrowserItem item)
        {
            CancelLoad();

            int version = Interlocked.Increment(ref _requestVersion);
            CancellationTokenSource cancellation = new();
            _loadCancellation = cancellation;

            HasSelection = true;
            IsMultiSelection = false;
            ErrorMessage = null;
            _currentIdentity = item.Identity;

            Title = item.DisplayName ?? string.Empty;
            Subtitle = item.IsFolder ? item.Prefix ?? string.Empty : item.Key ?? string.Empty;
            IconKey = new FileRowViewModel(item).IconKey;
            KindBadge = BuildKindBadge(item);
            DownloadLabel = "Download";

            _selectedObjects.Clear();
            _typeBreakdown.Clear();

            if (item.IsFolder)
            {
                BuildFolderGroups(item);
                Preview.ShowUnsupported("Virtual folders are key prefixes", "A prefix has no content to preview.");
                UpdateSharing(item);
                return;
            }

            IsLoading = true;
            AppSettings settings = _session.Settings;
            ProfileToken token = _session.Token;

            try
            {
                R2ObjectProperties properties = await _details
                    .GetAsync(settings, _session.Credentials, item, cancellation.Token)
                    .ConfigureAwait(true);

                if (!IsCurrent(version, token, item.Identity)) return;

                BuildObjectGroups(item, properties);
                BuildMetadataGroups(properties);
                UpdateSharing(item);

                await Preview.LoadAsync(item, properties.ContentType, version, cancellation.Token).ConfigureAwait(true);

                if (!IsCurrent(version, token, item.Identity)) return;

                if (!string.IsNullOrEmpty(Preview.DocumentPath))
                {
                    R2ObjectDetailsService.AddExtractedMetadata(properties, Preview.DocumentPath);
                    BuildMetadataGroups(properties);
                }

                // Image dimensions only become known once the object has been fetched.
                if (Preview.PixelWidth > 0 && Preview.PixelHeight > 0)
                {
                    BuildObjectGroups(item, properties, Preview.PixelWidth, Preview.PixelHeight);
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer selection.
            }
            catch (Exception ex)
            {
                if (!IsCurrent(version, token, item.Identity)) return;

                FriendlyError friendly = FriendlyErrorService.Describe(ex, settings, item.Key);
                ErrorMessage = friendly.FullMessage;
                Preview.ShowError(friendly.Headline);
                _log?.Warning("Inspector.Load", "Object details could not be loaded: " + friendly.Headline);
            }
            finally
            {
                if (_requestVersion == version) IsLoading = false;
            }
        }

        private bool IsCurrent(int version, ProfileToken token, string identity)
        {
            return !_disposed
                   && _requestVersion == version
                   && _session.Token == token
                   && string.Equals(_currentIdentity, identity, StringComparison.Ordinal);
        }

        private void BuildFolderGroups(R2BrowserItem item)
        {
            _groups.Clear();
            _metadataGroups.Clear();

            _groups.Add(new MetadataGroup("FILE DETAILS", new[]
            {
                new MetadataEntry("Kind", "Virtual folder prefix"),
                new MetadataEntry("Size", "—"),
                new MetadataEntry("Modified", "—")
            }));

            _groups.Add(new MetadataGroup("OBJECT", new[]
            {
                new MetadataEntry("Prefix", item.Prefix ?? string.Empty, isMono: true, _clipboard, _toasts),
                new MetadataEntry("Bucket", _session.Settings.BucketName)
            }));
        }

        private void BuildObjectGroups(
            R2BrowserItem item, R2ObjectProperties properties, int? width = null, int? height = null)
        {
            _groups.Clear();

            List<MetadataEntry> details = new()
            {
                new MetadataEntry("Kind", DescribeKind(properties)),
                new MetadataEntry("Size", DescribeSize(properties.Size ?? item.Size))
            };

            if (width is > 0 && height is > 0)
            {
                details.Add(new MetadataEntry(
                    "Dimensions",
                    width.Value.ToString(CultureInfo.CurrentCulture) + " × " +
                    height.Value.ToString(CultureInfo.CurrentCulture) + " px"));
            }

            details.Add(new MetadataEntry("Modified", DescribeModified(properties.LastModifiedUtc ?? item.LastModifiedUtc)));

            _groups.Add(new MetadataGroup("FILE DETAILS", details));

            List<MetadataEntry> objectEntries = new()
            {
                new MetadataEntry("Key", item.Key ?? string.Empty, isMono: true, _clipboard, _toasts)
            };

            if (!string.IsNullOrEmpty(properties.ETag))
                objectEntries.Add(new MetadataEntry("ETag", properties.ETag, isMono: true, _clipboard, _toasts));

            objectEntries.Add(new MetadataEntry(
                "Storage class",
                string.IsNullOrWhiteSpace(properties.StorageClass) ? "Standard" : properties.StorageClass));

            _groups.Add(new MetadataGroup("OBJECT", objectEntries));
        }

        private void BuildMetadataGroups(R2ObjectProperties properties)
        {
            _metadataGroups.Clear();

            List<MetadataEntry> headers = new();
            AddIfPresent(headers, "Content type", properties.ContentType);
            if (properties.Size.HasValue)
                headers.Add(new MetadataEntry("Content length", properties.Size.Value.ToString(CultureInfo.CurrentCulture)));
            AddIfPresent(headers, "Cache control", properties.CacheControl);
            AddIfPresent(headers, "Content disposition", properties.ContentDisposition);
            AddIfPresent(headers, "Content encoding", properties.ContentEncoding);
            AddIfPresent(headers, "Content language", properties.ContentLanguage);
            AddIfPresent(headers, "Version id", properties.VersionId);
            AddIfPresent(headers, "Checksum", properties.Checksum);

            if (headers.Count > 0) _metadataGroups.Add(new MetadataGroup("STANDARD HEADERS", headers));

            if (properties.CustomMetadata.Count > 0)
            {
                _metadataGroups.Add(new MetadataGroup(
                    "CUSTOM METADATA",
                    properties.CustomMetadata
                        .Select(pair => new MetadataEntry(pair.Key, pair.Value, isMono: true, _clipboard, _toasts))
                        .ToList()));
            }

            if (properties.ExtractedMetadata.Count > 0)
            {
                _metadataGroups.Add(new MetadataGroup(
                    "EXTRACTED",
                    properties.ExtractedMetadata
                        .Select(pair => new MetadataEntry(pair.Key, pair.Value))
                        .ToList()));
            }
        }

        private static void AddIfPresent(List<MetadataEntry> entries, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) entries.Add(new MetadataEntry(label, value!));
        }

        private void UpdateSharing(R2BrowserItem item)
        {
            string? url = item.IsFolder ? null : _publicUrls.Build(_session.Settings.PublicBaseUrl, item);

            PublicUrl = url ?? string.Empty;
            IsPublic = !string.IsNullOrEmpty(url);

            // A public base URL makes a link constructible; it does not make the object
            // public. The wording must not imply otherwise.
            VisibilityText = IsPublic ? "Public URL configured" : "No public URL";
            OnPropertyChanged(nameof(HasPublicUrl));
        }

        // -------------------------------------------------------------- multiple selection

        private void ShowMultiple(IReadOnlyList<R2BrowserItem> selection, string currentPrefix)
        {
            CancelLoad();
            Interlocked.Increment(ref _requestVersion);

            HasSelection = true;
            IsMultiSelection = true;
            IsLoading = false;
            ErrorMessage = null;
            _currentIdentity = string.Empty;

            int folders = selection.Count(static item => item.IsFolder);
            int objects = selection.Count - folders;
            long totalSize = selection.Where(static item => !item.IsFolder).Sum(static item => item.Size);

            Title = selection.Count + " objects selected";
            Subtitle = string.IsNullOrEmpty(currentPrefix) ? "Bucket root" : currentPrefix;
            IconKey = "Icon.Layers";
            KindBadge = string.Empty;
            TotalSizeText = FileSizeFormatter.Format(totalSize);
            ObjectCountText = selection.Count.ToString(CultureInfo.CurrentCulture);
            DownloadLabel = "Download " + selection.Count.ToString(CultureInfo.CurrentCulture);

            _groups.Clear();
            _metadataGroups.Clear();

            _typeBreakdown.Clear();
            foreach (var group in selection
                         .GroupBy(BrowserViewService.GetDisplayedType)
                         .OrderByDescending(static group => group.Sum(static item => item.Size))
                         .ThenBy(static group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                int count = group.Count();
                _typeBreakdown.Add(new SelectionTypeBreakdown(
                    group.Key,
                    count == 1 ? "1 object" : count.ToString(CultureInfo.CurrentCulture) + " objects",
                    group.Any(static item => item.IsFolder) ? "—" : FileSizeFormatter.Format(group.Sum(static item => item.Size))));
            }

            _selectedObjects.Clear();
            foreach (R2BrowserItem item in selection) _selectedObjects.Add(new FileRowViewModel(item));

            // Previewing an arbitrary member of a multi-selection would be a guess.
            Preview.ShowNothingSelected("Multiple objects selected", "Select a single object to preview it.");

            IsPublic = false;
            PublicUrl = string.Empty;
            OnPropertyChanged(nameof(HasPublicUrl));
        }

        // ----------------------------------------------------------------------- commands

        [RelayCommand]
        private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

        [RelayCommand]
        private void Download() => ActionRequested?.Invoke(this, "download");

        [RelayCommand]
        private void CopyKeys() => ActionRequested?.Invoke(this, "copy-keys");

        [RelayCommand]
        private void CopyPublicUrl()
        {
            if (!HasPublicUrl) return;
            _clipboard.SetText(PublicUrl);
            _toasts.ShowSuccess("Public link copied to clipboard");
        }

        [RelayCommand]
        private void OpenPublicUrl()
        {
            if (!HasPublicUrl) return;
            try { _launcher.Open(PublicUrl); }
            catch (Exception) { _toasts.ShowError("The browser could not be opened."); }
        }

        [RelayCommand]
        private void CreateTemporaryLink() => ActionRequested?.Invoke(this, "temporary-link");

        [RelayCommand]
        private void CopyAllMetadata()
        {
            IEnumerable<MetadataEntry> all = _groups.Concat(_metadataGroups).SelectMany(static group => group.Entries);
            string text = string.Join(Environment.NewLine, all.Select(static entry => entry.Label + ": " + entry.Value));

            if (text.Length == 0) return;

            _clipboard.SetText(text);
            _toasts.ShowSuccess("Metadata copied to clipboard");
        }

        // ------------------------------------------------------------------------ helpers

        private static string BuildKindBadge(R2BrowserItem item)
        {
            if (item.IsFolder) return string.Empty;

            string extension = Path.GetExtension(item.DisplayName ?? item.Key ?? string.Empty).TrimStart('.');
            return extension.Length is > 0 and <= 5 ? extension.ToUpperInvariant() : string.Empty;
        }

        private static string DescribeKind(R2ObjectProperties properties)
        {
            if (!string.IsNullOrWhiteSpace(properties.ContentType) &&
                properties.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                string mediaType = properties.ContentType.Split(';')[0];
                string format = mediaType.Substring("image/".Length).ToUpperInvariant();
                if (string.Equals(format, "JPEG", StringComparison.Ordinal)) format = "JPEG";

                string colourModel = string.Empty;
                KeyValuePair<string, string> colour = properties.ExtractedMetadata.FirstOrDefault(
                    static pair => pair.Key.EndsWith("Color Type", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(colour.Value))
                {
                    if (colour.Value.Contains("alpha", StringComparison.OrdinalIgnoreCase)) colourModel = "RGBA";
                    else if (colour.Value.Contains("true", StringComparison.OrdinalIgnoreCase) ||
                             colour.Value.Contains("rgb", StringComparison.OrdinalIgnoreCase)) colourModel = "RGB";
                    else if (colour.Value.Contains("gray", StringComparison.OrdinalIgnoreCase)) colourModel = "Grayscale";
                }

                return format + " image" + (colourModel.Length == 0 ? string.Empty : " · " + colourModel);
            }

            if (!string.IsNullOrWhiteSpace(properties.Type) && !string.IsNullOrWhiteSpace(properties.ContentType))
                return properties.Type + " · " + properties.ContentType;

            if (!string.IsNullOrWhiteSpace(properties.ContentType)) return properties.ContentType!;
            return string.IsNullOrWhiteSpace(properties.Type) ? "File" : properties.Type!;
        }

        private static string DescribeSize(long bytes)
        {
            return FileSizeFormatter.Format(bytes) +
                   " (" + bytes.ToString("N0", CultureInfo.CurrentCulture) + " bytes)";
        }

        private static string DescribeModified(DateTime? utc)
        {
            if (!utc.HasValue || utc.Value == DateTime.MinValue) return "—";
            return utc.Value.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);
        }

        private void CancelLoad()
        {
            CancellationTokenSource? previous = Interlocked.Exchange(ref _loadCancellation, null);
            if (previous is null) return;

            try { previous.Cancel(); } catch (ObjectDisposedException) { }
            previous.Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CancelLoad();
            Preview.Dispose();
        }
    }
}
