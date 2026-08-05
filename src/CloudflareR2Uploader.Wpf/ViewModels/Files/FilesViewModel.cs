using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using CloudflareR2Uploader.Wpf.Services;
using CloudflareR2Uploader.Wpf.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudflareR2Uploader.Wpf.ViewModels.Files
{
    /// <summary>Sort options offered by the toolbar's "Name · A→Z" selector.</summary>
    public sealed record SortOption(string DisplayName, BrowserSortColumn Column, BrowserSortDirection Direction)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record BreadcrumbSegment(string DisplayName, string Prefix, bool IsCurrent, bool IsFirst)
    {
        public override string ToString() => DisplayName;
    }

    public enum FilesViewMode
    {
        List = 0,
        Grid = 1
    }

    /// <summary>
    /// The Files browser, implementing Images/01-files-browser.png.
    /// <para>
    /// Listing, paging, filtering and sorting all preserve the WinForms behaviour exactly:
    /// one delimiter-based <c>ListObjectsV2</c> page at a time, opaque continuation tokens
    /// held per prefix, filtering and sorting applied only to the loaded page, and folders
    /// always first.
    /// </para>
    /// </summary>
    public sealed partial class FilesViewModel : ObservableObject, IDisposable
    {
        /// <summary>Matches the WinForms debounce, so typing never issues a request per keystroke.</summary>
        private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(250);

        private static readonly TimeSpan SelectionDebounce = TimeSpan.FromMilliseconds(250);

        private readonly IAppSession _session;
        private readonly IDialogService _dialogs;
        private readonly IToastService _toasts;
        private readonly IClipboardService _clipboard;
        private readonly IProcessLauncher _launcher;
        private readonly ILoggingService _log;
        private readonly R2ObjectBrowserService _browser;
        private readonly R2ObjectOperationService _operations;
        private readonly R2BulkOperationService _bulk;
        private readonly BulkLocalPathMapper _pathMapper;
        private readonly R2PresignedUrlService _presigned;
        private readonly PublicUrlService _publicUrls;
        private readonly IActivityHistoryService _activity;
        private readonly BrowserViewService _view = new();
        private readonly R2BrowserPaginationState _pagination = new();
        private readonly Stack<string> _backHistory = new();
        private readonly List<R2BrowserItem> _pageItems = new();

        private readonly DispatcherTimer _filterTimer;
        private readonly DispatcherTimer _selectionTimer;

        private CancellationTokenSource? _listCancellation;
        private ProfileToken _loadedForProfile = ProfileToken.None;
        private bool _needsRefresh = true;
        private bool _hasLoaded;
        private bool _disposed;

        public FilesViewModel(
            IAppSession session,
            IDialogService dialogs,
            IToastService toasts,
            IClipboardService clipboard,
            IProcessLauncher launcher,
            ILoggingService log,
            R2ObjectBrowserService browser,
            R2ObjectOperationService operations,
            R2BulkOperationService bulk,
            R2PresignedUrlService presigned,
            IActivityHistoryService activity,
            FileInspectorViewModel inspector)
        {
            _session = session;
            _dialogs = dialogs;
            _toasts = toasts;
            _clipboard = clipboard;
            _launcher = launcher;
            _log = log;
            _browser = browser;
            _operations = operations;
            _bulk = bulk;
            _presigned = presigned;
            _activity = activity;
            _pathMapper = new BulkLocalPathMapper();
            _publicUrls = new PublicUrlService();

            Inspector = inspector;

            AppSettings settings = _session.Settings;
            _selectedSort = SortOptions.FirstOrDefault(
                                option => option.Column == settings.BrowserSortColumn &&
                                          option.Direction == settings.BrowserSortDirection)
                            ?? SortOptions[0];
            _isInspectorVisible = settings.DetailsPanelVisible;

            _filterTimer = new DispatcherTimer { Interval = FilterDebounce };
            _filterTimer.Tick += (sender, args) =>
            {
                _filterTimer.Stop();
                ApplyView(preserveSelection: true);
            };

            _selectionTimer = new DispatcherTimer { Interval = SelectionDebounce };
            _selectionTimer.Tick += (sender, args) =>
            {
                _selectionTimer.Stop();
                Inspector.Show(SelectedRows.Select(static row => row.Item).ToList(), CurrentPrefix);
            };

            SelectedRows.CollectionChanged += OnSelectedRowsChanged;
            Rows = new ReadOnlyObservableCollection<FileRowViewModel>(_rows);
            Breadcrumbs = new ReadOnlyObservableCollection<BreadcrumbSegment>(_breadcrumbs);

            // The inspector and its preview raise intent rather than acting: they have no
            // access to the selection, the R2 services or the dialogs. This screen owns those,
            // so it is what turns those requests into the real commands.
            Inspector.CloseRequested += OnInspectorCloseRequested;
            Inspector.ActionRequested += OnInspectorActionRequested;
            Inspector.Preview.DownloadRequested += OnPreviewDownloadRequested;
            Inspector.Preview.RetryRequested += OnPreviewRetryRequested;

            RebuildBreadcrumbs();
        }

        private void OnInspectorCloseRequested(object? sender, EventArgs e)
        {
            IsInspectorVisible = false;
            PersistViewPreferences();
        }

        private void OnInspectorActionRequested(object? sender, string action)
        {
            switch (action)
            {
                case "download":
                    if (DownloadCommand.CanExecute(null)) DownloadCommand.Execute(null);
                    return;

                case "copy-keys":
                    if (CopyKeysCommand.CanExecute(null)) CopyKeysCommand.Execute(null);
                    return;

                case "temporary-link":
                    if (CreateTemporaryLinkCommand.CanExecute(null)) CreateTemporaryLinkCommand.Execute(null);
                    return;
            }
        }

        private void OnPreviewDownloadRequested(object? sender, EventArgs e)
        {
            if (DownloadCommand.CanExecute(null)) DownloadCommand.Execute(null);
        }

        /// <summary>Re-runs the inspector load for whatever is selected now.</summary>
        private void OnPreviewRetryRequested(object? sender, EventArgs e)
        {
            Inspector.Show(SelectedRows.Select(static row => row.Item).ToList(), CurrentPrefix);
        }

        public FileInspectorViewModel Inspector { get; }

        // ------------------------------------------------------------------------- state

        private readonly ObservableCollection<FileRowViewModel> _rows = new();

        public ReadOnlyObservableCollection<FileRowViewModel> Rows { get; }

        private readonly ObservableCollection<BreadcrumbSegment> _breadcrumbs = new();

        public ReadOnlyObservableCollection<BreadcrumbSegment> Breadcrumbs { get; }

        /// <summary>Bound to the grid's selection through a small view-side bridge.</summary>
        public ObservableCollection<FileRowViewModel> SelectedRows { get; } = new();

        public IReadOnlyList<SortOption> SortOptions { get; } = new[]
        {
            new SortOption("Name · A→Z", BrowserSortColumn.Name, BrowserSortDirection.Ascending),
            new SortOption("Name · Z→A", BrowserSortColumn.Name, BrowserSortDirection.Descending),
            new SortOption("Type · A→Z", BrowserSortColumn.Type, BrowserSortDirection.Ascending),
            new SortOption("Size · small→large", BrowserSortColumn.Size, BrowserSortDirection.Ascending),
            new SortOption("Size · large→small", BrowserSortColumn.Size, BrowserSortDirection.Descending),
            new SortOption("Modified · newest", BrowserSortColumn.LastModified, BrowserSortDirection.Descending),
            new SortOption("Modified · oldest", BrowserSortColumn.LastModified, BrowserSortDirection.Ascending)
        };

        public string CurrentPrefix => _pagination.Prefix;

        [ObservableProperty]
        private SortOption _selectedSort;

        [ObservableProperty]
        private string _filterText = string.Empty;

        [ObservableProperty]
        private bool _isInspectorVisible;

        [ObservableProperty]
        private FilesViewMode _viewMode = FilesViewMode.List;

        [ObservableProperty]
        private bool _isLoading;

        /// <summary>Non-null when the list cannot be shown: not configured, empty, or failed.</summary>
        [ObservableProperty]
        private string? _emptyStateTitle;

        [ObservableProperty]
        private string? _emptyStateMessage;

        [ObservableProperty]
        private bool _emptyStateOffersRetry;

        [ObservableProperty]
        private string _itemsSummary = "0 items";

        [ObservableProperty]
        private string _breakdownSummary = "0 folders · 0 objects";

        [ObservableProperty]
        private string _selectionSummary = string.Empty;

        [ObservableProperty]
        private string _listedSizeSummary = "0 B";

        [ObservableProperty]
        private string _pageSummary = "Page 1";

        [ObservableProperty]
        private bool _canGoPrevious;

        [ObservableProperty]
        private bool _canGoNext;

        [ObservableProperty]
        private bool _canGoUp;

        [ObservableProperty]
        private bool _canGoBack;

        /// <summary>Set by the view so Ctrl+F can move focus into the filter box.</summary>
        public event EventHandler? FilterFocusRequested;

        /// <summary>Raised when the view should clear the grid's selection.</summary>
        public event EventHandler? ClearSelectionRequested;

        /// <summary>Raised when the shell should switch to the Upload page.</summary>
        public event EventHandler? UploadPageRequested;

        // ---------------------------------------------------------------- selection facts

        public bool HasSelection => SelectedRows.Count > 0;

        public bool HasSingleSelection => SelectedRows.Count == 1;

        public bool HasSingleFileSelection => HasSingleSelection && !SelectedRows[0].IsFolder;

        public bool HasSingleFolderSelection => HasSingleSelection && SelectedRows[0].IsFolder;

        /// <summary>
        /// A public URL only exists for exactly one object with a configured public base URL.
        /// Folder prefixes are not objects and must never be offered a public URL.
        /// </summary>
        public bool CanOpenPublicUrl =>
            HasSingleFileSelection && !string.IsNullOrWhiteSpace(_session.Settings.PublicBaseUrl);

        public bool CanCopyPublicUrls =>
            SelectedRows.Any(static row => !row.IsFolder) &&
            !string.IsNullOrWhiteSpace(_session.Settings.PublicBaseUrl);

        public bool CanCreateTemporaryLink => HasSingleFileSelection;

        public bool CanRename => HasSingleSelection;

        public bool CanOverwrite => HasSingleFileSelection;

        // ------------------------------------------------------------------- page lifecycle

        public void OnActivated()
        {
            if (_needsRefresh || !_hasLoaded) _ = LoadAsync(resetPagination: false);
        }

        public void OnProfileChanged()
        {
            _pagination.Reset(string.Empty);
            _backHistory.Clear();
            _needsRefresh = true;
            Inspector.Clear();
            _ = LoadAsync(resetPagination: true);
        }

        public void OnConnectionSettingsChanged()
        {
            _needsRefresh = true;
            OnPropertyChanged(nameof(CanOpenPublicUrl));
            OnPropertyChanged(nameof(CanCopyPublicUrls));
        }

        // ------------------------------------------------------------------------ commands

        [RelayCommand]
        private async Task RefreshAsync() => await LoadAsync(resetPagination: true).ConfigureAwait(true);

        [RelayCommand]
        private async Task GoUpAsync()
        {
            string parent = R2BrowserPathUtility.GetParentPrefix(CurrentPrefix);
            if (string.Equals(parent, CurrentPrefix, StringComparison.Ordinal)) return;

            _backHistory.Push(CurrentPrefix);
            await NavigateAsync(parent).ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            if (_backHistory.Count == 0) return;
            await NavigateAsync(_backHistory.Pop(), recordHistory: false).ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task NavigateToAsync(string prefix)
        {
            if (string.Equals(prefix, CurrentPrefix, StringComparison.Ordinal)) return;
            _backHistory.Push(CurrentPrefix);
            await NavigateAsync(prefix).ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task OpenAsync()
        {
            if (!HasSingleFolderSelection) return;
            await NavigateToAsync(SelectedRows[0].Item.Prefix ?? string.Empty).ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task NextPageAsync()
        {
            if (!_pagination.MoveNext()) return;
            await LoadPageAsync().ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task PreviousPageAsync()
        {
            if (!_pagination.MovePrevious()) return;
            await LoadPageAsync().ConfigureAwait(true);
        }

        [RelayCommand]
        private void FocusFilter() => FilterFocusRequested?.Invoke(this, EventArgs.Empty);

        [RelayCommand]
        private void ClearFilter()
        {
            if (FilterText.Length == 0) return;
            FilterText = string.Empty;
        }

        /// <summary>Escape clears the filter first, then the selection — Explorer's order.</summary>
        [RelayCommand]
        private void Escape()
        {
            if (FilterText.Length > 0)
            {
                FilterText = string.Empty;
                return;
            }

            ClearSelectionRequested?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void GoToUpload() => UploadPageRequested?.Invoke(this, EventArgs.Empty);

        [RelayCommand]
        private void ToggleInspector()
        {
            IsInspectorVisible = !IsInspectorVisible;
            PersistViewPreferences();
        }

        [RelayCommand]
        private void CopyKeys()
        {
            if (!HasSelection) return;

            string text = string.Join(
                Environment.NewLine,
                SelectedRows.Select(static row => row.KeyOrPrefix));

            _clipboard.SetText(text);
            _toasts.ShowSuccess(SelectedRows.Count == 1
                ? "Object key copied to clipboard"
                : SelectedRows.Count + " object keys copied to clipboard");
        }

        [RelayCommand]
        private async Task CopyPublicUrlsAsync()
        {
            string publicBaseUrl = _session.Settings.PublicBaseUrl;
            if (string.IsNullOrWhiteSpace(publicBaseUrl))
            {
                await _dialogs.ShowMessageAsync(
                    "No public base URL",
                    "This bucket profile has no public base URL, so a public link cannot be built. Add one in Settings under Connection.")
                    .ConfigureAwait(true);
                return;
            }

            List<string> urls = SelectedRows
                .Where(static row => !row.IsFolder)
                .Select(row => _publicUrls.Build(publicBaseUrl, row.Item))
                .Where(static url => !string.IsNullOrEmpty(url))
                .Select(static url => url!)
                .ToList();

            if (urls.Count == 0)
            {
                _toasts.ShowError("Folders do not have public URLs. Select one or more objects.");
                return;
            }

            _clipboard.SetText(string.Join(Environment.NewLine, urls));
            _toasts.ShowSuccess(urls.Count == 1 ? "Public link copied to clipboard" : urls.Count + " public links copied");

            await RecordActivityAsync(
                ActivityAction.PublicLink,
                ActivityResult.Copied,
                urls.Count == 1 ? SelectedRows[0].KeyOrPrefix : urls.Count + " objects")
                .ConfigureAwait(true);
        }

        [RelayCommand]
        private void OpenPublicUrl()
        {
            if (!CanOpenPublicUrl) return;

            string? url = _publicUrls.Build(_session.Settings.PublicBaseUrl, SelectedRows[0].Item);
            if (string.IsNullOrEmpty(url)) return;

            try { _launcher.Open(url); }
            catch (Exception ex)
            {
                _toasts.ShowError("The browser could not be opened.");
                _log?.Warning("Files.OpenUrl", "Opening a public URL failed (" + ex.GetType().Name + ").");
            }
        }

        [RelayCommand]
        private async Task CreateTemporaryLinkAsync()
        {
            if (!CanCreateTemporaryLink) return;

            AppSettings settings = _session.Settings;
            R2BrowserItem item = SelectedRows[0].Item;

            try
            {
                PresignedUrlResult result = _presigned.Create(
                    settings, _session.Credentials, item.Key, settings.TemporaryLinkExpiry);

                TemporaryLinkDialogViewModel viewModel = new(
                    item.Key, result.Url, result.ExpiresLocal, _clipboard, _launcher);

                await _dialogs.ShowTemporaryLinkAsync(viewModel).ConfigureAwait(true);

                // Only the fact and the object key are recorded. The signed URL never is.
                await RecordActivityAsync(ActivityAction.TemporaryLink, ActivityResult.Created, item.Key)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await ShowOperationErrorAsync("The temporary link could not be created", ex).ConfigureAwait(true);
            }
        }

        [RelayCommand]
        private async Task NewFolderAsync()
        {
            string? name = await _dialogs.PromptForTextAsync(new TextPromptRequest
            {
                Title = "New folder",
                Label = "Folder name",
                ConfirmLabel = "Create",
                Placeholder = "release-notes",
                Hint = "R2 has no real folders. This creates a zero-byte marker object so the prefix appears in the browser.",
                Validate = static value =>
                {
                    R2ObjectOperationPathUtility.TryValidateLeafName(value, out string? reason);
                    return reason;
                }
            }).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(name)) return;

            string prefix = R2ObjectOperationPathUtility.CombineWithPrefix(CurrentPrefix, name.Trim(), isFolder: true);

            try
            {
                IsLoading = true;
                await _operations.CreateFolderAsync(
                    _session.Settings, _session.Credentials, prefix, null, CancellationToken.None)
                    .ConfigureAwait(true);

                _toasts.ShowSuccess("Folder created");
                await RecordActivityAsync(ActivityAction.NewFolder, ActivityResult.Success, prefix).ConfigureAwait(true);
                await LoadAsync(resetPagination: true).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await ShowOperationErrorAsync("The folder could not be created", ex).ConfigureAwait(true);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task RenameAsync()
        {
            if (!CanRename) return;

            FileRowViewModel row = SelectedRows[0];
            string currentLeaf = R2ObjectOperationPathUtility.GetLeafName(row.KeyOrPrefix, row.IsFolder);
            int stemLength = row.IsFolder ? currentLeaf.Length : Math.Max(0, currentLeaf.LastIndexOf('.'));

            string? name = await _dialogs.PromptForTextAsync(new TextPromptRequest
            {
                Title = row.IsFolder ? "Rename folder" : "Rename object",
                Label = "New name",
                ConfirmLabel = "Rename",
                InitialValue = currentLeaf,
                InitialSelectionLength = stemLength > 0 ? stemLength : -1,
                UseMonospace = true,
                Hint = row.IsFolder
                    ? "Renaming a folder copies every object under the prefix and then deletes the originals."
                    : null,
                Validate = value =>
                {
                    if (!R2ObjectOperationPathUtility.TryValidateLeafName(value, out string? reason)) return reason;
                    if (string.Equals(value, currentLeaf, StringComparison.Ordinal)) return "Enter a different name.";
                    return null;
                }
            }).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(name)) return;

            string destination = R2ObjectOperationPathUtility.CombineWithPrefix(
                CurrentPrefix, name.Trim(), row.IsFolder);

            await MoveOrRenameAsync(row, destination, ActivityAction.Rename).ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task MoveAsync()
        {
            if (!HasSingleSelection) return;

            FileRowViewModel row = SelectedRows[0];
            string leaf = R2ObjectOperationPathUtility.GetLeafName(row.KeyOrPrefix, row.IsFolder);

            string? target = await _dialogs.PromptForTextAsync(new TextPromptRequest
            {
                Title = "Move to",
                Label = "Destination folder or prefix",
                ConfirmLabel = "Move",
                InitialValue = CurrentPrefix,
                Placeholder = "assets/builds/",
                UseMonospace = true,
                Hint = "Leave empty to move to the bucket root.",
                Validate = value =>
                {
                    string normalized = R2BrowserPathUtility.NormalizePrefix(value);
                    if (row.IsFolder &&
                        R2ObjectOperationPathUtility.IsSameOrDescendantPrefix(row.Item.Prefix, normalized))
                    {
                        return "A folder cannot be moved into itself.";
                    }
                    return null;
                }
            }).ConfigureAwait(true);

            if (target is null) return;

            string destination = R2ObjectOperationPathUtility.CombineWithPrefix(
                R2BrowserPathUtility.NormalizePrefix(target), leaf, row.IsFolder);

            if (string.Equals(destination, row.KeyOrPrefix, StringComparison.Ordinal)) return;

            await MoveOrRenameAsync(row, destination, ActivityAction.Move).ConfigureAwait(true);
        }

        [RelayCommand]
        private async Task DeleteAsync()
        {
            if (!HasSelection) return;

            List<R2BrowserItem> selection = SelectedRows.Select(static row => row.Item).ToList();
            BrowserSelectionSummary summary = _view.Summarize(selection);

            if (_session.Settings.ConfirmDeletes)
            {
                string message = BuildDeleteMessage(summary);
                bool confirmed = await _dialogs
                    .ConfirmAsync("Delete " + DescribeSelection(summary) + "?", message,
                        "Delete " + DescribeSelection(summary), isDestructive: true)
                    .ConfigureAwait(true);

                if (!confirmed) return;
            }

            try
            {
                IsLoading = true;

                BulkObjectOperationPlan plan = await _bulk
                    .PlanAsync(_session.Settings, _session.Credentials, selection, null, CancellationToken.None)
                    .ConfigureAwait(true);

                if (plan.Objects.Count == 0)
                {
                    _toasts.ShowSuccess("Nothing to delete: the selection contains no objects.");
                    return;
                }

                BulkObjectOperationResult result = await _bulk
                    .DeleteAsync(_session.Settings, _session.Credentials, plan, null, CancellationToken.None)
                    .ConfigureAwait(true);

                if (result.Failures.Count > 0)
                {
                    _toasts.ShowError(result.Failures.Count + " object(s) could not be deleted.");
                }
                else
                {
                    _toasts.ShowSuccess(result.Succeeded + " object(s) deleted");
                }

                foreach (R2BrowserItem item in selection)
                {
                    await RecordActivityAsync(
                        ActivityAction.Delete,
                        result.Failures.Count > 0 ? ActivityResult.Failed : ActivityResult.Success,
                        item.Identity,
                        item.IsFolder ? null : item.Size)
                        .ConfigureAwait(true);
                }

                await LoadAsync(resetPagination: true).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await ShowOperationErrorAsync("The delete could not be completed", ex).ConfigureAwait(true);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task DownloadAsync() => await DownloadCoreAsync(overwriteExisting: false).ConfigureAwait(true);

        [RelayCommand]
        private async Task DownloadOverwriteAsync() => await DownloadCoreAsync(overwriteExisting: true).ConfigureAwait(true);

        private async Task DownloadCoreAsync(bool overwriteExisting)
        {
            if (!HasSelection) return;

            string? folder = _dialogs.BrowseForFolder("Choose where to save the selected objects");
            if (string.IsNullOrEmpty(folder)) return;

            List<R2BrowserItem> selection = SelectedRows.Select(static row => row.Item).ToList();

            try
            {
                IsLoading = true;

                BulkObjectOperationPlan plan = await _bulk
                    .PlanAsync(_session.Settings, _session.Credentials, selection, null, CancellationToken.None)
                    .ConfigureAwait(true);

                if (plan.Objects.Count == 0 && plan.EmptyDirectoryPaths.Count == 0)
                {
                    _toasts.ShowSuccess("Nothing to download: the selection contains no objects.");
                    return;
                }

                _pathMapper.Map(plan, folder);

                BulkObjectOperationResult result = await _bulk.DownloadAsync(
                    _session.Settings,
                    _session.Credentials,
                    plan,
                    overwriteExisting ? BulkConflictBehavior.Overwrite : BulkConflictBehavior.RenameAutomatically,
                    conflictPrompt: null,
                    progress: null,
                    CancellationToken.None).ConfigureAwait(true);

                if (result.Failures.Count > 0)
                {
                    _toasts.ShowError(result.Failures.Count + " object(s) failed to download.");
                }
                else
                {
                    _toasts.ShowSuccess(
                        result.Succeeded + " object(s) downloaded · " + FileSizeFormatter.Format(result.TransferredBytes));
                }

                await RecordActivityAsync(
                    ActivityAction.Download,
                    result.Failures.Count > 0 ? ActivityResult.Failed : ActivityResult.Success,
                    selection.Count == 1 ? selection[0].Identity : selection.Count + " objects",
                    result.TransferredBytes)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await ShowOperationErrorAsync("The download could not be completed", ex).ConfigureAwait(true);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private void SelectAll()
        {
            foreach (FileRowViewModel row in _rows)
            {
                if (!SelectedRows.Contains(row)) SelectedRows.Add(row);
            }
        }

        // ------------------------------------------------------------------------ loading

        private async Task NavigateAsync(string prefix, bool recordHistory = true)
        {
            _pagination.Reset(prefix);
            FilterText = string.Empty;
            Inspector.Clear();
            RebuildBreadcrumbs();
            await LoadPageAsync().ConfigureAwait(true);
        }

        private async Task LoadAsync(bool resetPagination)
        {
            if (resetPagination) _pagination.Reset(CurrentPrefix);
            await LoadPageAsync().ConfigureAwait(true);
        }

        private async Task LoadPageAsync()
        {
            CancellationTokenSource? previous = Interlocked.Exchange(ref _listCancellation, new CancellationTokenSource());
            if (previous is not null)
            {
                try { previous.Cancel(); } catch (ObjectDisposedException) { }
                previous.Dispose();
            }

            CancellationTokenSource? current = _listCancellation;
            if (current is null) return;

            if (!_session.IsConfigured)
            {
                ShowEmptyState(
                    "Connection is not configured",
                    "Open Settings and enter an account or endpoint, a bucket name and R2 credentials before browsing.",
                    offersRetry: false);
                return;
            }

            AppSettings settings = _session.Settings;
            R2Credentials credentials = _session.Credentials;
            ProfileToken token = _session.Token;

            IsLoading = true;
            EmptyStateTitle = null;

            try
            {
                R2BrowserPage page = await _browser
                    .ListPageAsync(settings, credentials, CurrentPrefix, _pagination.CurrentToken, current.Token)
                    .ConfigureAwait(true);

                // A response for the profile the user has since left must never paint.
                if (_session.Token != token) return;

                _pagination.SetNextContinuationToken(page.IsTruncated ? page.NextContinuationToken : null);

                _pageItems.Clear();
                _pageItems.AddRange(page.Items);

                _loadedForProfile = token;
                _hasLoaded = true;
                _needsRefresh = false;

                ApplyView(preserveSelection: false);
                RebuildBreadcrumbs();
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer listing.
            }
            catch (Exception ex)
            {
                if (_session.Token != token) return;

                FriendlyError friendly = FriendlyErrorService.Describe(ex, settings, null);
                ShowEmptyState(friendly.Headline, friendly.Detail, offersRetry: true);
                _log?.Warning("Files.List", "Listing failed: " + friendly.Headline);
            }
            finally
            {
                IsLoading = false;
                UpdateNavigationState();
            }
        }

        private void ApplyView(bool preserveSelection)
        {
            HashSet<string> selected = preserveSelection
                ? new HashSet<string>(SelectedRows.Select(static row => row.Identity), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            BrowserViewResult result = _view.Apply(
                _pageItems, FilterText, SelectedSort.Column, SelectedSort.Direction);

            SelectedRows.Clear();
            _rows.Clear();

            foreach (R2BrowserItem item in result.Items)
            {
                FileRowViewModel row = new(item);
                _rows.Add(row);
                if (selected.Contains(row.Identity)) SelectedRows.Add(row);
            }

            UpdateSummaries(result);

            if (_rows.Count == 0)
            {
                if (result.TotalCount > 0)
                {
                    ShowEmptyState("No matching objects", "Clear or change the filter to see this page again.", false);
                }
                else
                {
                    ShowEmptyState(
                        CurrentPrefix.Length == 0 ? "This bucket is empty" : "This prefix is empty",
                        "No objects or child prefixes are present on this page.",
                        offersRetry: false);
                }
            }
            else
            {
                EmptyStateTitle = null;
            }
        }

        private void UpdateSummaries(BrowserViewResult result)
        {
            int folders = _rows.Count(static row => row.IsFolder);
            int objects = _rows.Count - folders;
            long listedSize = _pageItems.Where(static item => !item.IsFolder).Sum(static item => item.Size);

            ItemsSummary = _rows.Count == 1 ? "1 item" : _rows.Count.ToString(CultureInfo.CurrentCulture) + " items";
            BreakdownSummary = string.Format(
                CultureInfo.CurrentCulture,
                "{0} folder{1} · {2} object{3}",
                folders, folders == 1 ? string.Empty : "s",
                objects, objects == 1 ? string.Empty : "s");

            ListedSizeSummary = FileSizeFormatter.Format(listedSize);
            PageSummary = "Page " + (_pagination.PageIndex + 1).ToString(CultureInfo.CurrentCulture);

            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            if (SelectedRows.Count == 0)
            {
                SelectionSummary = string.Empty;
                return;
            }

            BrowserSelectionSummary summary = _view.Summarize(SelectedRows.Select(static row => row.Item));
            SelectionSummary = string.Format(
                CultureInfo.CurrentCulture,
                "{0} selected · {1}",
                summary.SelectedCount,
                FileSizeFormatter.Format(summary.ListedFileSize));
        }

        private void ShowEmptyState(string title, string message, bool offersRetry)
        {
            _rows.Clear();
            SelectedRows.Clear();
            EmptyStateTitle = title;
            EmptyStateMessage = message;
            EmptyStateOffersRetry = offersRetry;
        }

        private void RebuildBreadcrumbs()
        {
            _breadcrumbs.Clear();

            IList<R2BrowserBreadcrumbSegment> segments = R2BrowserPathUtility.CreateBreadcrumbSegments(CurrentPrefix);
            string bucketName = _session.Settings.BucketName;

            for (int index = 0; index < segments.Count; index++)
            {
                R2BrowserBreadcrumbSegment segment = segments[index];
                bool isRoot = index == 0;
                bool isCurrent = index == segments.Count - 1;

                _breadcrumbs.Add(new BreadcrumbSegment(
                    isRoot ? (string.IsNullOrWhiteSpace(bucketName) ? segment.DisplayName : bucketName) : segment.DisplayName,
                    segment.Prefix,
                    isCurrent,
                    isRoot));
            }

            UpdateNavigationState();
        }

        private void UpdateNavigationState()
        {
            CanGoPrevious = _pagination.CanMovePrevious;
            CanGoNext = _pagination.CanMoveNext;
            CanGoUp = CurrentPrefix.Length > 0;
            CanGoBack = _backHistory.Count > 0;
        }

        // ------------------------------------------------------------------------- helpers

        private async Task MoveOrRenameAsync(FileRowViewModel row, string destination, ActivityAction action)
        {
            try
            {
                IsLoading = true;

                bool exists = await _operations.NameExistsAsync(
                    _session.Settings, _session.Credentials, destination, row.IsFolder, CancellationToken.None)
                    .ConfigureAwait(true);

                if (exists)
                {
                    await _dialogs.ShowMessageAsync(
                        "That name is taken",
                        "An object or folder already exists at \"" + destination + "\". Choose a different name.")
                        .ConfigureAwait(true);
                    return;
                }

                R2ObjectOperationResult result = await _operations.CopyAsync(
                    _session.Settings,
                    _session.Credentials,
                    row.Item,
                    destination,
                    deleteSourceAfterCopy: true,
                    progress: null,
                    CancellationToken.None).ConfigureAwait(true);

                _toasts.ShowSuccess(action == ActivityAction.Rename ? "Renamed" : "Moved");

                await RecordActivityAsync(
                    action, ActivityResult.Success, row.KeyOrPrefix, result.TotalBytes, destination)
                    .ConfigureAwait(true);

                await LoadAsync(resetPagination: true).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                await ShowOperationErrorAsync(
                    action == ActivityAction.Rename ? "The rename could not be completed" : "The move could not be completed",
                    ex).ConfigureAwait(true);

                await RecordActivityAsync(action, ActivityResult.Failed, row.KeyOrPrefix, null, destination, ex)
                    .ConfigureAwait(true);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static string DescribeSelection(BrowserSelectionSummary summary)
        {
            if (summary.FolderCount > 0 && summary.FileCount > 0)
                return summary.SelectedCount + " items";

            if (summary.FolderCount > 0)
                return summary.FolderCount == 1 ? "1 folder" : summary.FolderCount + " folders";

            return summary.FileCount == 1 ? "1 object" : summary.FileCount + " objects";
        }

        private string BuildDeleteMessage(BrowserSelectionSummary summary)
        {
            string sizeText = summary.ListedFileSize > 0
                ? " (" + FileSizeFormatter.Format(summary.ListedFileSize) + ")"
                : string.Empty;

            string folderNote = summary.FolderCount > 0
                ? " Deleting a folder removes every object under its prefix, including objects that are not on this page."
                : string.Empty;

            return "This permanently removes " + DescribeSelection(summary) + sizeText + " from " +
                   _session.Settings.BucketName + "." + folderNote +
                   " R2 has no recycle bin — this cannot be undone.";
        }

        private async Task ShowOperationErrorAsync(string headline, Exception exception)
        {
            FriendlyError friendly = FriendlyErrorService.Describe(exception, _session.Settings, null);
            await _dialogs.ShowErrorAsync(headline, friendly.FullMessage, friendly.TechnicalDetails).ConfigureAwait(true);
        }

        private async Task RecordActivityAsync(
            ActivityAction action,
            ActivityResult result,
            string target,
            long? size = null,
            string? secondaryTarget = null,
            Exception? exception = null)
        {
            AppSettings settings = _session.Settings;

            ActivityRecord record = new()
            {
                Action = action,
                Result = result,
                Target = target,
                SecondaryTarget = secondaryTarget ?? string.Empty,
                BucketName = settings.BucketName,
                ProfileId = settings.ActiveBucketProfileId,
                SizeBytes = size,
                TimestampUtc = DateTime.UtcNow
            };

            if (exception is not null)
            {
                record.ErrorCode = TransientErrorClassifier.GetErrorCode(exception);
                record.ErrorMessage = FriendlyErrorService.Describe(exception, settings, null).Headline;
            }

            await _activity.RecordAsync(record).ConfigureAwait(true);
        }

        private void PersistViewPreferences()
        {
            AppSettings settings = _session.Settings;
            settings.DetailsPanelVisible = IsInspectorVisible;
            settings.BrowserSortColumn = SelectedSort.Column;
            settings.BrowserSortDirection = SelectedSort.Direction;
            _session.ApplyAndSave(settings);
        }

        private void OnSelectedRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateSelectionSummary();

            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasSingleSelection));
            OnPropertyChanged(nameof(HasSingleFileSelection));
            OnPropertyChanged(nameof(HasSingleFolderSelection));
            OnPropertyChanged(nameof(CanOpenPublicUrl));
            OnPropertyChanged(nameof(CanCopyPublicUrls));
            OnPropertyChanged(nameof(CanCreateTemporaryLink));
            OnPropertyChanged(nameof(CanRename));
            OnPropertyChanged(nameof(CanOverwrite));

            // The inspector load is debounced so arrowing through a list does not start a
            // metadata request and a preview download for every row passed over.
            _selectionTimer.Stop();
            _selectionTimer.Start();
        }

        partial void OnFilterTextChanged(string value)
        {
            _filterTimer.Stop();
            _filterTimer.Start();
        }

        partial void OnSelectedSortChanged(SortOption value)
        {
            ApplyView(preserveSelection: true);
            PersistViewPreferences();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _filterTimer.Stop();
            _selectionTimer.Stop();
            SelectedRows.CollectionChanged -= OnSelectedRowsChanged;

            Inspector.CloseRequested -= OnInspectorCloseRequested;
            Inspector.ActionRequested -= OnInspectorActionRequested;
            Inspector.Preview.DownloadRequested -= OnPreviewDownloadRequested;
            Inspector.Preview.RetryRequested -= OnPreviewRetryRequested;

            CancellationTokenSource? listing = Interlocked.Exchange(ref _listCancellation, null);
            if (listing is not null)
            {
                try { listing.Cancel(); } catch (ObjectDisposedException) { }
                listing.Dispose();
            }

            Inspector.Dispose();
        }
    }
}
