using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
        private readonly R2ObjectBrowserService _objectBrowserService;
        private readonly R2BrowserPaginationState _browserPagination = new R2BrowserPaginationState();
        private readonly Stack<string> _browserBackHistory = new Stack<string>();

        private CancellationTokenSource _browserLoadCancellation;
        private bool _browserPageActive;
        private bool _browserLoading;
        private bool _browserHasLoaded;
        private bool _browserNeedsRefresh = true;
        private int _browserContentVersion;

        private void ApplyBrowserTheme()
        {
            pageNavigationPanel.BackColor = Color.Transparent;
            bucketBrowserPagePanel.BackColor = Theme.CardBackground;
            browserToolbarPanel.BackColor = Color.Transparent;
            browserFooterPanel.BackColor = Color.Transparent;

            browserPathLabel.BackColor = Theme.InputBackground;
            browserPathLabel.ForeColor = Theme.TextSecondary;
            browserPathLabel.Font = ThemeFonts.Body;

            browserStatusLabel.BackColor = Color.Transparent;
            browserStatusLabel.ForeColor = Theme.TextMuted;
            browserStatusLabel.Font = ThemeFonts.Caption;

            browserStateLabel.BackColor = Theme.CardBackground;
            browserStateLabel.ForeColor = Theme.TextMuted;
            browserStateLabel.Font = ThemeFonts.Body;

            browserGrid.BackgroundColor = Theme.CardBackground;
            browserGrid.GridColor = Theme.Divider;
            browserGrid.Font = ThemeFonts.Body;
            browserGrid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.CardBackground,
                ForeColor = Theme.TextPrimary,
                SelectionBackColor = Theme.AccentSoft,
                SelectionForeColor = Theme.TextPrimary,
                Font = ThemeFonts.Body,
                Padding = new Padding(8, 0, 8, 0)
            };
            browserGrid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.CardBackgroundAlt,
                ForeColor = Theme.TextPrimary,
                SelectionBackColor = Theme.AccentSoft,
                SelectionForeColor = Theme.TextPrimary,
                Font = ThemeFonts.Body,
                Padding = new Padding(8, 0, 8, 0)
            };
            browserGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.ElevatedBackground,
                ForeColor = Theme.TextSecondary,
                SelectionBackColor = Theme.ElevatedBackground,
                SelectionForeColor = Theme.TextSecondary,
                Font = ThemeFonts.CaptionBold,
                Padding = new Padding(8, 0, 8, 0)
            };
        }

        private void ApplyBrowserTooltips()
        {
            toolTip.SetToolTip(uploadPageButton, "Show the upload queue (Alt+U)");
            toolTip.SetToolTip(bucketFilesPageButton, "Browse the selected bucket without changing it (Alt+B)");
            toolTip.SetToolTip(browserBackButton, "Return to the previously visited folder");
            toolTip.SetToolTip(browserUpButton, "Open the parent virtual folder (Backspace in the list)");
            toolTip.SetToolTip(browserRefreshButton, "Reload the current virtual folder (F5)");
            toolTip.SetToolTip(browserNewFolderButton, "Create a new virtual folder in the current location");
            toolTip.SetToolTip(browserPreviousButton, "Load the previous page using its saved continuation token");
            toolTip.SetToolTip(browserNextButton, "Load the next page returned by R2");
        }

        private void WireBrowserEvents()
        {
            uploadPageButton.Click += OnUploadPageClick;
            bucketFilesPageButton.Click += OnBucketFilesPageClick;
            browserBackButton.Click += OnBrowserBackClick;
            browserUpButton.Click += OnBrowserUpClick;
            browserRefreshButton.Click += OnBrowserRefreshClick;
            browserNewFolderButton.Click += OnBrowserNewFolderClick;
            browserPreviousButton.Click += OnBrowserPreviousClick;
            browserNextButton.Click += OnBrowserNextClick;
            browserGrid.CellDoubleClick += OnBrowserGridCellDoubleClick;
            browserGrid.CellPainting += OnBrowserGridCellPainting;
        }

        private void OnUploadPageClick(object sender, EventArgs e)
        {
            ShowUploadPage();
        }

        private void OnBucketFilesPageClick(object sender, EventArgs e)
        {
            ShowBucketFilesPage();
        }

        private void ShowUploadPage()
        {
            if (!_browserPageActive || IsPageTransitionRunning) return;

            Bitmap outgoing = CaptureCurrentPageSnapshot(true);
            _browserPageActive = false;
            bucketBrowserPagePanel.Visible = false;
            SetUploadContentVisible(true);
            uploadPageButton.ButtonStyle = ModernButtonStyle.Primary;
            bucketFilesPageButton.ButtonStyle = ModernButtonStyle.Ghost;
            uploadPageButton.AccessibleDescription = "Selected page. Shows the upload queue and destination controls.";
            bucketFilesPageButton.AccessibleDescription = "Browse objects and virtual folders in the selected R2 bucket.";
            StartPageTransition(outgoing, false);
        }

        private void ShowBucketFilesPage()
        {
            if (_browserPageActive)
            {
                if (_browserNeedsRefresh && !_browserLoading) BeginBrowserLoad();
                return;
            }
            if (IsPageTransitionRunning) return;

            Bitmap outgoing = CaptureCurrentPageSnapshot(false);
            _browserPageActive = true;
            SetUploadContentVisible(false);
            bucketBrowserPagePanel.Visible = true;
            bucketBrowserPagePanel.BringToFront();
            uploadPageButton.ButtonStyle = ModernButtonStyle.Ghost;
            bucketFilesPageButton.ButtonStyle = ModernButtonStyle.Primary;
            uploadPageButton.AccessibleDescription = "Show the upload queue and destination controls.";
            bucketFilesPageButton.AccessibleDescription = "Selected page. Browses objects and virtual folders in the selected R2 bucket.";

            if (_connectionVerified)
            {
                if (_browserNeedsRefresh || !_browserHasLoaded) BeginBrowserLoad();
                else UpdateBrowserCommandStates();
            }
            else
            {
                ShowBrowserAvailabilityState();
            }
            StartPageTransition(outgoing, true);
        }

        private void SetUploadContentVisible(bool visible)
        {
            dropZone.Visible = visible;
            destinationCard.Visible = visible;
            queueCard.Visible = visible;
            statusCard.Visible = visible;
        }

        private void ShowBrowserAvailabilityState()
        {
            if (!_browserPageActive) return;

            ClearBrowserRows();
            UpdateBrowserPathDisplay();

            if (_settings == null || string.IsNullOrWhiteSpace(_settings.BucketName) ||
                (string.IsNullOrWhiteSpace(_settings.AccountId) && string.IsNullOrWhiteSpace(_settings.CustomEndpoint)))
            {
                ShowBrowserState(
                    "Connection is not configured",
                    "Open Settings and enter an account or endpoint and bucket name before browsing.",
                    false);
                browserStatusLabel.Text = "No bucket configuration is available.";
            }
            else if (_credentials == null || !_credentials.IsComplete)
            {
                ShowBrowserState(
                    "Credentials are missing",
                    "Open Settings and enter the R2 S3 Access Key ID and Secret Access Key for this bucket.",
                    false);
                browserStatusLabel.Text = "No credentials are available for this bucket profile.";
            }
            else if (_backgroundTest != null)
            {
                ShowBrowserState(
                    "Verifying the connection...",
                    "The bucket will load after the read-access check succeeds.",
                    false);
                browserStatusLabel.Text = "Checking bucket access...";
            }
            else
            {
                ShowBrowserState(
                    "Connection has not been verified",
                    "Use Settings to test this bucket connection, then try again.",
                    true);
                browserStatusLabel.Text = "Bucket access is not verified.";
            }

            UpdateBrowserCommandStates();
        }

        private void ShowBrowserConnectionResult(ConnectionTestResult result)
        {
            if (!_browserPageActive) return;

            if (result != null && result.IsSuccess)
            {
                BeginBrowserLoad();
                return;
            }

            ClearBrowserRows();
            ShowBrowserState(
                "Connection could not be verified",
                result == null ? "Check the bucket settings and credentials, then retry." : result.Headline + ". " + result.Detail,
                true);
            browserStatusLabel.Text = "The browser did not send a listing request.";
            UpdateBrowserCommandStates();
        }

        private async void BeginBrowserLoad()
        {
            if (!_browserPageActive) return;
            if (!_connectionVerified)
            {
                ShowBrowserAvailabilityState();
                return;
            }

            CancelBrowserLoad(false);

            string profileId = _settings.ActiveBucketProfileId;
            AppSettings settings = _settings.Clone();
            R2Credentials credentials = _credentials;
            string prefix = _browserPagination.Prefix;
            int pageIndex = _browserPagination.PageIndex;
            string continuationToken = _browserPagination.CurrentToken;
            int contentVersion = _browserContentVersion;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            _browserLoadCancellation = cancellation;
            _browserLoading = true;

            ClearBrowserRows();
            UpdateBrowserPathDisplay();
            ShowBrowserState(
                "Loading files...",
                prefix.Length == 0 ? "Reading the bucket root." : "Reading " + prefix,
                false);
            browserStatusLabel.Text = "Loading page " + (pageIndex + 1).ToString(CultureInfo.CurrentCulture) + "...";
            UpdateBrowserCommandStates();

            try
            {
                R2BrowserPage page = await _objectBrowserService.ListPageAsync(
                    settings,
                    credentials,
                    prefix,
                    continuationToken,
                    cancellation.Token).ConfigureAwait(true);

                if (!IsCurrentBrowserRequest(
                    cancellation, profileId, settings.BucketName, prefix, pageIndex, continuationToken))
                    return;

                ApplyBrowserPage(page);
                _browserHasLoaded = true;
                _browserNeedsRefresh = contentVersion != _browserContentVersion;
                if (_browserNeedsRefresh)
                    browserStatusLabel.Text = "Recent upload completed. Refresh to show it.";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Page, folder, bucket and form changes intentionally cancel old requests.
            }
            catch (Exception ex)
            {
                if (!IsCurrentBrowserRequest(
                    cancellation, profileId, settings.BucketName, prefix, pageIndex, continuationToken))
                    return;

                FriendlyError friendly = FriendlyErrorService.Describe(ex, settings, null);
                ClearBrowserRows();
                ShowBrowserState(friendly.Headline, friendly.Detail, true);
                browserStatusLabel.Text = "Loading failed. Select Refresh to retry this page.";
            }
            finally
            {
                if (_browserLoadCancellation == cancellation)
                {
                    _browserLoadCancellation = null;
                    _browserLoading = false;
                    UpdateBrowserCommandStates();
                }
                cancellation.Dispose();
            }
        }

        private bool IsCurrentBrowserRequest(
            CancellationTokenSource cancellation,
            string profileId,
            string bucketName,
            string prefix,
            int pageIndex,
            string continuationToken)
        {
            return !IsDisposed &&
                   _browserLoadCancellation == cancellation &&
                   !cancellation.IsCancellationRequested &&
                   _settings != null &&
                   string.Equals(_settings.ActiveBucketProfileId, profileId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(_settings.BucketName, bucketName, StringComparison.Ordinal) &&
                   string.Equals(_browserPagination.Prefix, prefix, StringComparison.Ordinal) &&
                   _browserPagination.PageIndex == pageIndex &&
                   string.Equals(_browserPagination.CurrentToken, continuationToken, StringComparison.Ordinal);
        }

        private void ApplyBrowserPage(R2BrowserPage page)
        {
            browserGrid.Rows.Clear();

            foreach (R2BrowserItem item in page.Items)
            {
                string type = item.IsFolder ? "Folder" : GetBrowserFileType(item.DisplayName);
                string size = item.IsFolder ? string.Empty : FileSizeFormatter.Format(item.Size);
                string modified = item.LastModifiedUtc.HasValue
                    ? item.LastModifiedUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                    : string.Empty;

                int rowIndex = browserGrid.Rows.Add(item.DisplayName, type, size, modified);
                browserGrid.Rows[rowIndex].Tag = item;
            }

            _browserPagination.SetNextContinuationToken(
                page.IsTruncated && !string.IsNullOrEmpty(page.NextContinuationToken)
                    ? page.NextContinuationToken
                    : null);

            if (page.Items.Count == 0)
            {
                ShowBrowserState(
                    page.Prefix.Length == 0 ? "This bucket is empty" : "This folder is empty",
                    page.Prefix.Length == 0
                        ? "No objects or virtual folders are present on this page."
                        : "No objects or child prefixes are present on this page.",
                    false);
            }
            else
            {
                browserStateLabel.Visible = false;
                browserGrid.Visible = true;
                browserGrid.ClearSelection();
            }

            browserStatusLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} {1} and {2} {3} on this page | Page {4}",
                page.FolderCount,
                page.FolderCount == 1 ? "folder" : "folders",
                page.FileCount,
                page.FileCount == 1 ? "file" : "files",
                _browserPagination.PageIndex + 1);
        }

        private static string GetBrowserFileType(string displayName)
        {
            string name = displayName ?? string.Empty;
            int separator = name.LastIndexOf('/');
            int dot = name.LastIndexOf('.');
            if (dot <= separator + 1 || dot == name.Length - 1) return "File";

            string extension = name.Substring(dot + 1);
            if (extension.Length > 10) return "File";

            switch (extension.ToLowerInvariant())
            {
                case "jpg": case "jpeg": case "png": case "gif": case "webp": case "svg": case "bmp":
                    return "Image";
                case "mp4": case "mov": case "avi": case "mkv": case "webm":
                    return "Video";
                case "mp3": case "wav": case "ogg": case "flac": case "m4a":
                    return "Audio";
                case "zip": case "7z": case "rar": case "gz": case "tar":
                    return "Archive";
                case "txt": case "md": case "log": case "csv":
                    return "Text";
                case "pdf":
                    return "PDF document";
                case "exe": case "msi":
                    return "Application";
                default:
                    return extension.ToUpperInvariant() + " file";
            }
        }

        private void OnBrowserGridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= browserGrid.Rows.Count) return;
            OpenSelectedBrowserFolder();
        }

        private void OpenSelectedBrowserFolder()
        {
            if (browserGrid.SelectedRows.Count == 0) return;
            R2BrowserItem item = browserGrid.SelectedRows[0].Tag as R2BrowserItem;
            if (item == null || !item.IsFolder) return;

            NavigateBrowserTo(item.Prefix, true);
        }

        private void OnBrowserGridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != browserNameColumn.Index) return;

            R2BrowserItem item = browserGrid.Rows[e.RowIndex].Tag as R2BrowserItem;
            if (item == null) return;

            e.PaintBackground(e.CellBounds, true);
            e.Paint(e.CellBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);

            Rectangle iconBounds = new Rectangle(e.CellBounds.X + 10, e.CellBounds.Y + (e.CellBounds.Height - 18) / 2, 18, 18);
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            Color iconColor = item.IsFolder ? Theme.AccentBright : (selected ? Theme.TextPrimary : Theme.TextSecondary);
            IconPainter.Draw(e.Graphics, item.IsFolder ? AppIcon.Folder : AppIcon.File, iconBounds, iconColor, 1.8f);

            Rectangle textBounds = new Rectangle(
                e.CellBounds.X + 38,
                e.CellBounds.Y,
                Math.Max(0, e.CellBounds.Width - 46),
                e.CellBounds.Height);
            TextRenderer.DrawText(
                e.Graphics,
                Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture),
                browserGrid.Font,
                textBounds,
                selected ? Theme.TextPrimary : Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.Handled = true;
        }

        private void OnBrowserBackClick(object sender, EventArgs e)
        {
            if (_browserLoading || _browserBackHistory.Count == 0) return;
            NavigateBrowserTo(_browserBackHistory.Pop(), false);
        }

        private void OnBrowserUpClick(object sender, EventArgs e)
        {
            if (_browserLoading || _browserPagination.Prefix.Length == 0) return;
            NavigateBrowserTo(R2BrowserPathUtility.GetParentPrefix(_browserPagination.Prefix), true);
        }

        private void OnBrowserRefreshClick(object sender, EventArgs e)
        {
            if (_browserLoading || !_connectionVerified) return;
            if (_browserPagination.Prefix.Length == 0)
                _browserPagination.Reset(string.Empty);
            _browserNeedsRefresh = true;
            BeginBrowserLoad();
        }

        private void OnBrowserPreviousClick(object sender, EventArgs e)
        {
            if (_browserLoading || !_browserPagination.MovePrevious()) return;
            BeginBrowserLoad();
        }

        private void OnBrowserNextClick(object sender, EventArgs e)
        {
            if (_browserLoading || !_browserPagination.MoveNext()) return;
            BeginBrowserLoad();
        }

        private void NavigateBrowserTo(string prefix, bool rememberCurrent)
        {
            string normalized = R2BrowserPathUtility.NormalizePrefix(prefix);
            if (string.Equals(normalized, _browserPagination.Prefix, StringComparison.Ordinal)) return;

            if (rememberCurrent) _browserBackHistory.Push(_browserPagination.Prefix);
            _browserPagination.Reset(normalized);
            _browserHasLoaded = false;
            _browserNeedsRefresh = true;
            ClearBrowserRows();
            UpdateBrowserPathDisplay();
            BeginBrowserLoad();
        }

        private void ResetBrowserForTargetChange()
        {
            CancelBrowserLoad(false);
            ClearBrowserClipboardForTargetChange();
            _browserPagination.Reset(string.Empty);
            _browserBackHistory.Clear();
            _browserHasLoaded = false;
            _browserNeedsRefresh = true;
            _browserContentVersion++;
            ClearBrowserRows();
            UpdateBrowserPathDisplay();

            if (_browserPageActive) ShowBrowserAvailabilityState();
            else UpdateBrowserCommandStates();
        }

        private void MarkBrowserNeedsRefresh()
        {
            _browserContentVersion++;
            _browserNeedsRefresh = true;

            if (_browserPageActive && _browserHasLoaded && !_browserLoading)
            {
                browserStatusLabel.Text = "Recent upload completed. Refresh to show it.";
                browserStatusLabel.ForeColor = Theme.Warning;
                UpdateBrowserCommandStates();
            }
        }

        private void CancelBrowserLoad(bool disposeImmediately)
        {
            CancellationTokenSource cancellation = _browserLoadCancellation;
            if (cancellation == null) return;

            _browserLoadCancellation = null;
            _browserLoading = false;
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }

            if (disposeImmediately)
            {
                try { cancellation.Dispose(); }
                catch (ObjectDisposedException) { }
            }
        }

        private void ClearBrowserRows()
        {
            browserGrid.Rows.Clear();
            browserGrid.ClearSelection();
        }

        private void ShowBrowserState(string headline, string detail, bool isError)
        {
            browserStateLabel.Text = headline + Environment.NewLine + Environment.NewLine + detail;
            browserStateLabel.ForeColor = isError ? Theme.Error : Theme.TextMuted;
            browserStateLabel.AccessibleName = headline;
            browserStateLabel.AccessibleDescription = detail;
            browserGrid.Visible = false;
            browserStateLabel.Visible = true;
        }

        private void UpdateBrowserPathDisplay()
        {
            browserPathLabel.Text = R2BrowserPathUtility.FormatBreadcrumb(_browserPagination.Prefix);
            browserPathLabel.AccessibleName = "Current bucket path";
            browserPathLabel.AccessibleDescription = _browserPagination.Prefix.Length == 0
                ? "Bucket root"
                : "Bucket prefix " + _browserPagination.Prefix;
        }

        private void UpdateBrowserCommandStates()
        {
            bool available = _browserPageActive && _connectionVerified && !_browserOperationInProgress;
            browserBackButton.Enabled = available && !_browserLoading && _browserBackHistory.Count > 0;
            browserUpButton.Enabled = available && !_browserLoading && _browserPagination.Prefix.Length > 0;
            browserRefreshButton.Enabled = available && !_browserLoading;
            browserNewFolderButton.Enabled = available && !_browserLoading && !_queueService.IsRunning;
            browserPreviousButton.Enabled = available && !_browserLoading && _browserPagination.CanMovePrevious;
            browserNextButton.Enabled = available && !_browserLoading && _browserPagination.CanMoveNext;

            if (!_browserNeedsRefresh) browserStatusLabel.ForeColor = Theme.TextMuted;
        }

        private bool HandleBrowserShortcut(KeyEventArgs e)
        {
            if (!_browserPageActive || IsBrowserTextEntryControl(ActiveControl)) return false;

            if (e.Alt && e.KeyCode == Keys.U)
            {
                ShowUploadPage();
                return true;
            }

            if (e.KeyCode == Keys.F5)
            {
                if (browserRefreshButton.Enabled) OnBrowserRefreshClick(this, EventArgs.Empty);
                return true;
            }

            if (!browserGrid.ContainsFocus) return false;

            if (e.KeyCode == Keys.Back)
            {
                if (browserUpButton.Enabled) OnBrowserUpClick(this, EventArgs.Empty);
                return true;
            }

            if (e.KeyCode == Keys.Enter)
            {
                OpenSelectedBrowserFolder();
                return true;
            }

            return false;
        }

        private static bool IsBrowserTextEntryControl(Control control)
        {
            return control is TextBoxBase || control is ModernTextBox || control is ComboBox;
        }
    }
}
