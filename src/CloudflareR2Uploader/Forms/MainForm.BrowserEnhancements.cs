using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CloudflareR2Uploader.Controls;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Forms
{
    public partial class MainForm
    {
        private readonly BrowserViewService _browserViewService = new BrowserViewService();
        private readonly List<R2BrowserItem> _browserPageItems = new List<R2BrowserItem>();
        private ModernTextBox _browserFilterTextBox;
        private Button _browserFilterClearButton;
        private Label _browserFilterIcon;
        private Button _browserDetailsToggleButton;
        private Label _browserSelectionLabel;
        private System.Windows.Forms.Timer _browserFilterTimer;
        private System.Windows.Forms.Timer _browserSelectionTimer;
        private Panel _browserMiddlePanel;
        private SplitContainer _browserSplit;
        private R2DetailsPanel _browserDetailsPanel;
        private R2ObjectDetailsService _objectDetailsService;
        private PreviewCacheService _previewCache;
        private R2PreviewService _previewService;
        private CancellationTokenSource _detailsCancellation;
        private int _detailsRequestVersion;
        private bool _applyingBrowserRows;

        private void InitializeBrowserEnhancements()
        {
            browserGrid.MultiSelect = true;
            browserNameColumn.SortMode = DataGridViewColumnSortMode.Programmatic;
            browserTypeColumn.SortMode = DataGridViewColumnSortMode.Programmatic;
            browserSizeColumn.SortMode = DataGridViewColumnSortMode.Programmatic;
            browserModifiedColumn.SortMode = DataGridViewColumnSortMode.Programmatic;

            _browserFilterTextBox = new ModernTextBox
            {
                Name = "browserFilterTextBox", AccessibleName = "Filter this page", AccessibleDescription = "Filters only the currently loaded R2 page.",
                BackColor = Theme.InputBackground, ForeColor = Theme.TextPrimary, Font = ThemeFonts.Body,
                Text = string.Empty, PlaceholderText = "Filter this page"
            };
            _browserFilterClearButton = new Button { Name = "browserFilterClearButton", Text = "×", AccessibleName = "Clear filter", FlatStyle = FlatStyle.Flat, BackColor = Theme.InputBackground, ForeColor = Theme.TextSecondary };
            _browserFilterIcon = new Label { Name = "browserFilterIcon", Text = "⌕", AccessibleName = "Search", TextAlign = ContentAlignment.MiddleCenter, BackColor = Theme.InputBackground, ForeColor = Theme.TextMuted, Font = ThemeFonts.Title };
            _browserDetailsToggleButton = new Button { Name = "browserDetailsToggleButton", Text = "Details", AccessibleName = "Show or hide Preview and Properties", FlatStyle = FlatStyle.Flat, BackColor = Theme.ElevatedBackground, ForeColor = Theme.TextPrimary };
            browserToolbarPanel.Controls.Add(_browserFilterIcon); browserToolbarPanel.Controls.Add(_browserFilterTextBox); browserToolbarPanel.Controls.Add(_browserFilterClearButton); browserToolbarPanel.Controls.Add(_browserDetailsToggleButton);
            browserToolbarPanel.Resize += (s, e) => PositionBrowserEnhancementControls();
            PositionBrowserEnhancementControls();

            bucketBrowserPagePanel.Controls.Remove(browserGrid);
            bucketBrowserPagePanel.Controls.Remove(browserStateLabel);
            _browserMiddlePanel = new Panel { Name = "browserMiddlePanel", BackColor = Theme.CardBackground };
            _browserSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, FixedPanel = FixedPanel.Panel2, SplitterWidth = 5, BackColor = Theme.Divider, AccessibleName = "Files and details" };
            Panel left = new Panel { Dock = DockStyle.Fill, BackColor = Theme.CardBackground };
            left.Controls.Add(browserGrid); left.Controls.Add(browserStateLabel);
            _browserSplit.Panel1.Controls.Add(left);
            _browserDetailsPanel = new R2DetailsPanel { Dock = DockStyle.Fill };
            _browserSplit.Panel2.Controls.Add(_browserDetailsPanel);
            _browserMiddlePanel.Controls.Add(_browserSplit);
            bucketBrowserPagePanel.Controls.Add(_browserMiddlePanel);
            bucketBrowserPagePanel.Resize += (s, e) => PositionBrowserMiddlePanel();
            PositionBrowserMiddlePanel();

            _browserSelectionLabel = new Label { Name = "browserSelectionLabel", AutoEllipsis = true, ForeColor = Theme.TextSecondary, Font = ThemeFonts.Caption, TextAlign = ContentAlignment.MiddleLeft };
            browserFooterPanel.Controls.Add(_browserSelectionLabel); _browserSelectionLabel.BringToFront();
            PositionSelectionLabel(); browserFooterPanel.Resize += (s, e) => PositionSelectionLabel();

            _browserFilterTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _browserFilterTimer.Tick += (s, e) => { _browserFilterTimer.Stop(); ApplyBrowserView(true); };
            _browserSelectionTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _browserSelectionTimer.Tick += (s, e) => { _browserSelectionTimer.Stop(); BeginDetailsLoad(); };
            _browserFilterTextBox.TextChanged += (s, e) => { _browserFilterClearButton.Enabled = _browserFilterTextBox.Text.Length > 0; _browserFilterTimer.Stop(); _browserFilterTimer.Start(); };
            _browserFilterClearButton.Click += (s, e) => { _browserFilterTextBox.Text = string.Empty; _browserFilterTextBox.Focus(); };
            _browserDetailsToggleButton.Click += (s, e) => ToggleDetailsPanel();
            browserGrid.SelectionChanged += (s, e) => { if (!_applyingBrowserRows) { UpdateSelectionSummary(); _browserSelectionTimer.Stop(); _browserSelectionTimer.Start(); } };
            browserGrid.ColumnHeaderMouseClick += OnBrowserColumnHeaderClick;
            _browserDetailsPanel.SelectedTabChanged += (s, e) => { if (_settings != null) _settings.DetailsSelectedTab = _browserDetailsPanel.SelectedTab; };
            _browserDetailsPanel.CopyKeyRequested += (s, e) => CopySelectedObjectKeys();
            _browserDetailsPanel.CopyPublicUrlRequested += (s, e) => CopySelectedPublicUrls();
            _browserDetailsPanel.OpenPublicUrlRequested += (s, e) => OpenSelectedPublicUrl();
            _browserDetailsPanel.TemporaryLinkRequested += (s, e) => ShowPresignedUrlForSelection();

            _objectDetailsService = new R2ObjectDetailsService();
            _previewCache = new PreviewCacheService();
            _previewService = new R2PreviewService(_previewCache);
            _browserDetailsPanel.ShowNeutral("Select an object to inspect Preview and Properties.");
        }

        private void ApplyBrowserEnhancementSettings()
        {
            if (_settings == null) return;
            if (_browserSplit.Width > 520) { _browserSplit.Panel1MinSize = 260; _browserSplit.Panel2MinSize = 240; }
            _browserDetailsPanel.SelectedTab = _settings.DetailsSelectedTab;
            _browserSplit.Panel2Collapsed = !_settings.DetailsPanelVisible;
            if (!_browserSplit.Panel2Collapsed)
            {
                int width = Math.Max(240, Math.Min(_settings.DetailsPanelWidth, Math.Max(240, _browserSplit.Width - 300)));
                _browserSplit.SplitterDistance = Math.Max(300, _browserSplit.Width - width - _browserSplit.SplitterWidth);
            }
            UpdateSortGlyph();
        }

        private void PositionBrowserEnhancementControls()
        {
            if (_browserFilterTextBox == null) return;
            int right = browserToolbarPanel.ClientSize.Width;
            _browserDetailsToggleButton.SetBounds(Math.Max(450, right - 94), 7, 88, 38);
            _browserFilterClearButton.SetBounds(Math.Max(450, right - 130), 9, 32, 34);
            _browserFilterTextBox.SetBounds(Math.Max(476, right - 320), 10, 188, 32);
            _browserFilterIcon.SetBounds(Math.Max(450, right - 346), 10, 26, 32);
            int pathWidth = Math.Max(120, _browserFilterTextBox.Left - browserPathLabel.Left - 10);
            browserPathLabel.Width = pathWidth;
        }

        private void PositionBrowserMiddlePanel()
        {
            if (_browserMiddlePanel == null) return;
            int top = browserToolbarPanel.Bottom;
            int bottom = browserFooterPanel.Top;
            _browserMiddlePanel.SetBounds(bucketBrowserPagePanel.Padding.Left, top, Math.Max(0, bucketBrowserPagePanel.ClientSize.Width - bucketBrowserPagePanel.Padding.Horizontal), Math.Max(0, bottom - top));
            _browserMiddlePanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        }
        private void PositionSelectionLabel()
        {
            if (_browserSelectionLabel == null) return;
            int available = Math.Max(240, browserPreviousButton.Left - 8);
            browserStatusLabel.Width = Math.Max(220, available / 2);
            _browserSelectionLabel.SetBounds(browserStatusLabel.Right + 8, 7, Math.Max(100, available - browserStatusLabel.Width - 8), 36);
        }

        private void ToggleDetailsPanel()
        {
            if (_browserSplit == null) return;
            if (!_browserSplit.Panel2Collapsed && _settings != null) _settings.DetailsPanelWidth = _browserSplit.Panel2.Width;
            _browserSplit.Panel2Collapsed = !_browserSplit.Panel2Collapsed;
            if (_settings != null) _settings.DetailsPanelVisible = !_browserSplit.Panel2Collapsed;
        }

        internal List<R2BrowserItem> GetSelectedBrowserItems()
        {
            List<R2BrowserItem> result = new List<R2BrowserItem>();
            foreach (DataGridViewRow row in browserGrid.Rows)
                if (row.Selected && row.Tag is R2BrowserItem) result.Add(((R2BrowserItem)row.Tag).Clone());
            return result;
        }

        private void ApplyBrowserView(bool preserveSelection)
        {
            HashSet<string> selected = preserveSelection ? new HashSet<string>(GetSelectedBrowserItems().Select(item => item.Identity), StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
            BrowserViewResult view = _browserViewService.Apply(_browserPageItems, _browserFilterTextBox == null ? string.Empty : _browserFilterTextBox.Text, _settings == null ? BrowserSortColumn.Name : _settings.BrowserSortColumn, _settings == null ? BrowserSortDirection.Ascending : _settings.BrowserSortDirection);
            _applyingBrowserRows = true;
            try
            {
                browserGrid.Rows.Clear();
                foreach (R2BrowserItem item in view.Items)
                {
                    string type = BrowserViewService.GetDisplayedType(item);
                    int rowIndex = browserGrid.Rows.Add(item.DisplayName, type, item.IsFolder ? string.Empty : FileSizeFormatter.Format(item.Size), item.LastModifiedUtc.HasValue ? item.LastModifiedUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : string.Empty);
                    browserGrid.Rows[rowIndex].Tag = item;
                    browserGrid.Rows[rowIndex].Selected = selected.Contains(item.Identity);
                }
                browserGrid.Visible = view.Items.Count > 0;
                browserStateLabel.Visible = view.Items.Count == 0;
                if (view.Items.Count == 0 && view.TotalCount > 0) ShowBrowserState("No matching objects", "Clear or change the current-page filter.", false);
                else if (view.TotalCount == 0) ShowBrowserState(_browserPagination.Prefix.Length == 0 ? "This bucket is empty" : "This folder is empty", "No objects or child prefixes are present on this page.", false);
                else { browserStateLabel.Visible = false; browserGrid.Visible = true; }
            }
            finally { _applyingBrowserRows = false; }
            browserStatusLabel.Text = view.Items.Count + " shown of " + view.TotalCount + " on this page | Page " + (_browserPagination.PageIndex + 1);
            UpdateSortGlyph(); UpdateSelectionSummary();
        }

        private void OnBrowserColumnHeaderClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            BrowserSortColumn column = e.ColumnIndex == browserTypeColumn.Index ? BrowserSortColumn.Type : e.ColumnIndex == browserSizeColumn.Index ? BrowserSortColumn.Size : e.ColumnIndex == browserModifiedColumn.Index ? BrowserSortColumn.LastModified : BrowserSortColumn.Name;
            if (_settings.BrowserSortColumn == column) _settings.BrowserSortDirection = _settings.BrowserSortDirection == BrowserSortDirection.Ascending ? BrowserSortDirection.Descending : BrowserSortDirection.Ascending;
            else { _settings.BrowserSortColumn = column; _settings.BrowserSortDirection = BrowserSortDirection.Ascending; }
            ApplyBrowserView(true);
        }

        private void UpdateSortGlyph()
        {
            foreach (DataGridViewColumn column in browserGrid.Columns) column.HeaderCell.SortGlyphDirection = SortOrder.None;
            if (_settings == null) return;
            DataGridViewColumn selected = _settings.BrowserSortColumn == BrowserSortColumn.Type ? browserTypeColumn : _settings.BrowserSortColumn == BrowserSortColumn.Size ? browserSizeColumn : _settings.BrowserSortColumn == BrowserSortColumn.LastModified ? browserModifiedColumn : browserNameColumn;
            selected.HeaderCell.SortGlyphDirection = _settings.BrowserSortDirection == BrowserSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending;
        }

        private void UpdateSelectionSummary()
        {
            BrowserSelectionSummary summary = _browserViewService.Summarize(GetSelectedBrowserItems());
            _browserSelectionLabel.Text = summary.SelectedCount == 0 ? "No selection" : summary.SelectedCount + " selected · " + summary.FolderCount + " folders · " + summary.FileCount + " files · " + FileSizeFormatter.Format(summary.ListedFileSize) + " listed size";
            _browserSelectionLabel.AccessibleName = "Selection summary"; _browserSelectionLabel.AccessibleDescription = _browserSelectionLabel.Text;
        }

        private async void BeginDetailsLoad()
        {
            CancelDetailsLoad();
            List<R2BrowserItem> selection = GetSelectedBrowserItems();
            if (selection.Count == 0) { _browserDetailsPanel.ShowNeutral("Select an object to inspect Preview and Properties."); return; }
            if (selection.Count > 1) { _browserDetailsPanel.ShowAggregate(_browserViewService.Summarize(selection)); return; }
            R2BrowserItem item = selection[0];
            int version = ++_detailsRequestVersion;
            CancellationTokenSource cancellation = new CancellationTokenSource(); _detailsCancellation = cancellation;
            _browserDetailsPanel.ShowLoading();
            try
            {
                AppSettings settings = _settings.Clone(); R2Credentials credentials = _credentials;
                R2ObjectProperties properties = await _objectDetailsService.GetAsync(settings, credentials, item, cancellation.Token).ConfigureAwait(true);
                if (!IsCurrentDetails(version, cancellation, item.Identity)) return;
                _browserDetailsPanel.ShowProperties(properties);
                if (!item.IsFolder)
                {
                    R2PreviewResult preview = await _previewService.GetAsync(settings, credentials, item, properties.ContentType, cancellation.Token).ConfigureAwait(true);
                    if (!IsCurrentDetails(version, cancellation, item.Identity)) return;
                    if (!string.IsNullOrEmpty(preview.LocalPath))
                    {
                        R2ObjectDetailsService.AddExtractedMetadata(properties, preview.LocalPath);
                        _browserDetailsPanel.ShowProperties(properties);
                    }
                    await _browserDetailsPanel.ShowPreviewAsync(preview, AppPaths.WebView2DataDirectory);
                }
                else await _browserDetailsPanel.ShowPreviewAsync(new R2PreviewResult { Kind = R2PreviewKind.Unsupported, Warning = "Virtual folders are prefixes and do not have preview content." }, AppPaths.WebView2DataDirectory);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (IsCurrentDetails(version, cancellation, item.Identity)) _browserDetailsPanel.ShowNeutral("Details could not be loaded.\r\n\r\n" + LoggingService.Sanitize(ex.Message));
            }
        }

        private bool IsCurrentDetails(int version, CancellationTokenSource cancellation, string identity)
        {
            return !IsDisposed && _detailsRequestVersion == version && _detailsCancellation == cancellation && !cancellation.IsCancellationRequested && GetSelectedBrowserItems().Count == 1 && string.Equals(GetSelectedBrowserItems()[0].Identity, identity, StringComparison.Ordinal);
        }
        private void CancelDetailsLoad() { _detailsRequestVersion++; CancellationTokenSource cancellation = _detailsCancellation; _detailsCancellation = null; if (cancellation != null) { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } cancellation.Dispose(); } }
        private void InvalidateBrowserDetails() { CancelDetailsLoad(); if (_objectDetailsService != null) _objectDetailsService.Clear(); if (_previewCache != null) _previewCache.ClearSession(); }
        private void DisposeBrowserEnhancements()
        {
            CancelDetailsLoad();
            if (_browserFilterTimer != null) _browserFilterTimer.Dispose();
            if (_browserSelectionTimer != null) _browserSelectionTimer.Dispose();
            if (_browserDetailsPanel != null) _browserDetailsPanel.DisposePreview();
            if (_previewCache != null) _previewCache.Dispose();
            if (_settings != null && _browserSplit != null && !_browserSplit.Panel2Collapsed) _settings.DetailsPanelWidth = _browserSplit.Panel2.Width;
        }
    }
}
