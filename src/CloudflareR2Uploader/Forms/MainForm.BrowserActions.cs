using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
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
        private readonly R2ObjectOperationService _objectOperationService;
        private ContextMenuStrip _browserContextMenu;
        private ToolStripMenuItem _contextOpenItem;
        private ToolStripMenuItem _contextDownloadItem;
        private ToolStripMenuItem _contextOverwriteItem;
        private ToolStripMenuItem _contextCopyItem;
        private ToolStripMenuItem _contextPasteItem;
        private ToolStripMenuItem _contextNewFolderItem;
        private ToolStripMenuItem _contextRenameItem;
        private ToolStripMenuItem _contextMoveItem;
        private ToolStripMenuItem _contextDeleteItem;
        private ToolStripMenuItem _contextCopyKeysItem;
        private ToolStripMenuItem _contextCopyPublicUrlsItem;
        private ToolStripMenuItem _contextOpenPublicUrlItem;
        private ToolStripMenuItem _contextTemporaryUrlItem;
        private ToolStripMenuItem _contextRefreshItem;
        private ToolStripSeparator _contextObjectActionsSeparator;
        private BrowserClipboardEntry _browserClipboard;
        private R2BrowserItem _browserContextItem;
        private CancellationTokenSource _browserOperationCancellation;
        private bool _browserOperationInProgress;

        private void InitializeBrowserActions()
        {
            _browserContextMenu = new ContextMenuStrip
            {
                Name = "browserContextMenu",
                BackColor = Theme.ElevatedBackground,
                ForeColor = Theme.TextPrimary,
                Font = ThemeFonts.Body,
                Renderer = new DarkContextMenuRenderer(),
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Padding = new Padding(4)
            };

            _contextOpenItem = CreateContextItem("Open", OnContextOpenClick);
            _contextOpenItem.Name = "browserOpenMenuItem";
            _contextDownloadItem = CreateContextItem("Download...", OnContextDownloadClick);
            _contextDownloadItem.Name = "browserDownloadMenuItem";
            _contextOverwriteItem = CreateContextItem("Overwrite from local file...", OnContextOverwriteClick);
            _contextOverwriteItem.Name = "browserOverwriteMenuItem";
            _contextCopyItem = CreateContextItem("Copy", OnContextCopyClick);
            _contextCopyItem.Name = "browserCopyMenuItem";
            _contextPasteItem = CreateContextItem("Paste here", OnContextPasteClick);
            _contextPasteItem.Name = "browserPasteMenuItem";
            _contextNewFolderItem = CreateContextItem("New folder...", OnBrowserNewFolderClick);
            _contextNewFolderItem.Name = "browserNewFolderMenuItem";
            _contextRenameItem = CreateContextItem("Rename...", OnContextRenameClick);
            _contextRenameItem.Name = "browserRenameMenuItem";
            _contextMoveItem = CreateContextItem("Move...", OnContextMoveClick);
            _contextMoveItem.Name = "browserMoveMenuItem";
            _contextDeleteItem = CreateContextItem("Delete...", OnContextDeleteClick);
            _contextDeleteItem.Name = "browserDeleteMenuItem";
            _contextDeleteItem.ForeColor = Theme.Error;
            _contextCopyKeysItem = CreateContextItem("Copy object key(s)", (s, e) => CopySelectedObjectKeys());
            _contextCopyPublicUrlsItem = CreateContextItem("Copy public URL(s)", (s, e) => CopySelectedPublicUrls());
            _contextOpenPublicUrlItem = CreateContextItem("Open public URL", (s, e) => OpenSelectedPublicUrl());
            _contextTemporaryUrlItem = CreateContextItem("Create temporary link...", (s, e) => ShowPresignedUrlForSelection());
            _contextRefreshItem = CreateContextItem("Refresh", OnBrowserRefreshClick);
            _contextRefreshItem.Name = "browserContextRefreshMenuItem";
            _contextObjectActionsSeparator = new ToolStripSeparator();

            _browserContextMenu.Items.AddRange(new ToolStripItem[]
            {
                _contextOpenItem,
                _contextDownloadItem,
                _contextOverwriteItem,
                _contextCopyKeysItem,
                _contextCopyPublicUrlsItem,
                _contextOpenPublicUrlItem,
                _contextTemporaryUrlItem,
                _contextObjectActionsSeparator,
                _contextCopyItem,
                _contextPasteItem,
                _contextNewFolderItem,
                _contextRenameItem,
                _contextMoveItem,
                new ToolStripSeparator(),
                _contextDeleteItem,
                _contextRefreshItem
            });
            _browserContextMenu.Opening += OnBrowserContextMenuOpening;
            browserGrid.ContextMenuStrip = _browserContextMenu;
            browserStateLabel.ContextMenuStrip = _browserContextMenu;
            browserGrid.CellMouseDown += OnBrowserGridCellMouseDown;
            InitializeBulkActions();
        }

        private static ToolStripMenuItem CreateContextItem(string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text)
            {
                AutoSize = false,
                Size = new Size(248, 34),
                ForeColor = Theme.TextPrimary
            };
            item.Click += handler;
            return item;
        }

        private void OnBrowserGridCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;

            if (e.RowIndex >= 0 && e.RowIndex < browserGrid.Rows.Count)
            {
                if (!browserGrid.Rows[e.RowIndex].Selected)
                {
                    browserGrid.ClearSelection();
                    browserGrid.Rows[e.RowIndex].Selected = true;
                }
                browserGrid.CurrentCell = browserGrid.Rows[e.RowIndex].Cells[0];
            }
            else
            {
                browserGrid.ClearSelection();
            }
        }

        private void OnBrowserContextMenuOpening(object sender, CancelEventArgs e)
        {
            List<R2BrowserItem> selection = GetSelectedBrowserItems();
            _browserContextItem = selection.Count == 1 ? selection[0] : null;
            bool hasItem = selection.Count > 0;
            bool single = selection.Count == 1;
            bool isFolder = single && _browserContextItem.IsFolder;
            bool canOperate = _connectionVerified && !_browserLoading &&
                              !_browserOperationInProgress && !_queueService.IsRunning;
            bool clipboardAvailable = _browserClipboard != null &&
                _settings != null &&
                string.Equals(_browserClipboard.ProfileId, _settings.ActiveBucketProfileId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_browserClipboard.BucketName, _settings.BucketName, StringComparison.Ordinal);

            _contextOpenItem.Visible = single && isFolder;
            _contextOpenItem.Enabled = canOperate;
            _contextDownloadItem.Visible = hasItem;
            _contextDownloadItem.Text = selection.Count == 1 && !isFolder ? "Download..." : "Download selected...";
            _contextDownloadItem.Enabled = canOperate;
            _contextOverwriteItem.Visible = single && !isFolder;
            _contextOverwriteItem.Enabled = canOperate;
            _contextObjectActionsSeparator.Visible = hasItem;
            _contextCopyItem.Visible = single;
            _contextCopyItem.Enabled = canOperate;
            _contextNewFolderItem.Visible = !hasItem;
            _contextNewFolderItem.Enabled = canOperate;
            _contextRenameItem.Visible = single;
            _contextRenameItem.Enabled = canOperate;
            _contextMoveItem.Visible = single;
            _contextMoveItem.Enabled = canOperate;
            _contextDeleteItem.Visible = hasItem;
            _contextDeleteItem.Text = selection.Count > 1 ? "Delete selected..." : "Delete...";
            _contextDeleteItem.Enabled = canOperate;
            _contextPasteItem.Visible = clipboardAvailable && selection.Count <= 1;
            _contextPasteItem.Enabled = canOperate && clipboardAvailable && selection.Count <= 1;
            _contextPasteItem.Text = isFolder
                ? "Paste into \"" + _browserContextItem.DisplayName + "\""
                : "Paste into this folder";
            _contextRefreshItem.Enabled = _connectionVerified && !_browserLoading && !_browserOperationInProgress;
            int fileCount = selection.Count(value => !value.IsFolder);
            bool publicConfigured = _settings != null && !string.IsNullOrWhiteSpace(_settings.PublicBaseUrl);
            _contextCopyKeysItem.Visible = hasItem; _contextCopyKeysItem.Enabled = canOperate;
            _contextCopyPublicUrlsItem.Visible = fileCount > 0; _contextCopyPublicUrlsItem.Enabled = canOperate && publicConfigured;
            _contextOpenPublicUrlItem.Visible = single && !isFolder; _contextOpenPublicUrlItem.Enabled = canOperate && publicConfigured;
            _contextTemporaryUrlItem.Visible = single && !isFolder; _contextTemporaryUrlItem.Enabled = canOperate;
        }

        private R2BrowserItem GetSelectedBrowserItem()
        {
            if (browserGrid.SelectedRows.Count == 0) return null;
            return browserGrid.SelectedRows[0].Tag as R2BrowserItem;
        }

        private void OnContextOpenClick(object sender, EventArgs e)
        {
            OpenSelectedBrowserFolder();
        }

        private async void OnContextDownloadClick(object sender, EventArgs e)
        {
            List<R2BrowserItem> selection = GetSelectedBrowserItems();
            if (selection.Count != 1 || (selection.Count == 1 && selection[0].IsFolder))
            {
                await DownloadSelectedBrowserItemsAsync();
                return;
            }
            R2BrowserItem item = _browserContextItem;
            if (item == null || item.IsFolder) return;

            string leaf = R2ObjectOperationPathUtility.GetLeafName(item.Key, false);
            using (SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Download from R2",
                FileName = PathUtility.SanitizeForFileName(leaf),
                Filter = "All files (*.*)|*.*",
                AddExtension = false,
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                await RunBrowserOperationAsync(
                    "Downloading " + item.DisplayName + "...",
                    "Downloaded to " + dialog.FileName,
                    false,
                    async (settings, credentials, progress, token) =>
                        await _objectOperationService.DownloadAsync(
                            settings, credentials, item.Key, dialog.FileName, progress, token));
            }
        }

        private async void OnContextOverwriteClick(object sender, EventArgs e)
        {
            R2BrowserItem item = _browserContextItem;
            if (item == null || item.IsFolder) return;

            using (OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Choose the replacement for " + item.DisplayName,
                Multiselect = false,
                CheckFileExists = true,
                Filter = "All files (*.*)|*.*"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!R2ConfirmationDialog.Confirm(
                    this,
                    "Overwrite R2 object",
                    "Replace this object?",
                    "The contents of \"" + item.Key + "\" will be replaced by \"" +
                    Path.GetFileName(dialog.FileName) + "\". This cannot be undone.",
                    "Overwrite"))
                    return;

                await RunBrowserOperationAsync(
                    "Overwriting " + item.DisplayName + "...",
                    "Overwrite complete.",
                    true,
                    async (settings, credentials, progress, token) =>
                    {
                        UploadResult result = await _objectOperationService.OverwriteAsync(
                            settings, credentials, item.Key, dialog.FileName, progress, token);
                        if (result.Outcome == UploadOutcome.Cancelled)
                            throw new OperationCanceledException(token);
                        if (result.Outcome != UploadOutcome.Succeeded)
                            throw new BrowserOperationException(result.Message, result.TechnicalDetails);
                    });
            }
        }

        private void OnContextCopyClick(object sender, EventArgs e)
        {
            R2BrowserItem item = _browserContextItem;
            if (item == null || _settings == null) return;

            _browserClipboard = new BrowserClipboardEntry(
                CloneBrowserItem(item),
                _settings.ActiveBucketProfileId,
                _settings.BucketName);
            browserStatusLabel.Text = "Copied " + item.DisplayName + ". Right-click a destination and choose Paste.";
            browserStatusLabel.ForeColor = Theme.AccentBright;
        }

        private async void OnBrowserNewFolderClick(object sender, EventArgs e)
        {
            if (!_connectionVerified || _browserLoading || _browserOperationInProgress ||
                _queueService.IsRunning)
                return;

            string folderName;
            using (R2NameDialog dialog = new R2NameDialog(
                "New virtual folder",
                "Create a new folder",
                "Enter a folder name. It will be created inside the current bucket location.",
                string.Empty,
                "Create"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                folderName = dialog.EntryName;
            }

            string destination = R2ObjectOperationPathUtility.CombineWithPrefix(
                _browserPagination.Prefix, folderName, true);
            await RunBrowserOperationAsync(
                "Creating " + folderName + "...",
                "Folder created.",
                true,
                async (settings, credentials, progress, token) =>
                    await _objectOperationService.CreateFolderAsync(
                        settings, credentials, destination, progress, token));
        }

        private async void OnContextRenameClick(object sender, EventArgs e)
        {
            R2BrowserItem selected = _browserContextItem;
            if (selected == null) return;

            R2BrowserItem item = CloneBrowserItem(selected);
            string current = item.IsFolder ? item.Prefix : item.Key;
            string currentName = R2ObjectOperationPathUtility.GetLeafName(current, item.IsFolder);
            string newName;
            using (R2NameDialog dialog = new R2NameDialog(
                item.IsFolder ? "Rename virtual folder" : "Rename object",
                "Rename " + item.DisplayName,
                item.IsFolder
                    ? "Enter a new name for this folder. Every object under it will keep its relative path."
                    : "Enter a new name for this object. Its current virtual folder will not change.",
                currentName,
                "Rename"))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                newName = dialog.EntryName;
            }

            string destination = R2ObjectOperationPathUtility.CombineWithPrefix(
                _browserPagination.Prefix, newName, item.IsFolder);
            if (string.Equals(current, destination, StringComparison.Ordinal)) return;

            string destinationBody = item.IsFolder ? destination.TrimEnd('/') : destination;
            string validationReason;
            if (!ObjectKeyUtility.TryValidate(destinationBody, out validationReason))
            {
                ErrorDialog.Show(
                    this,
                    "That name cannot be used",
                    validationReason,
                    null);
                return;
            }

            await RunBrowserOperationAsync(
                "Renaming " + item.DisplayName + "...",
                "Rename complete.",
                true,
                async (settings, credentials, progress, token) =>
                {
                    if (await _objectOperationService.NameExistsAsync(
                        settings, credentials, destination, item.IsFolder, token))
                    {
                        throw new InvalidOperationException(
                            "A file or folder named \"" + newName + "\" already exists here.");
                    }

                    await _objectOperationService.CopyAsync(
                        settings, credentials, item, destination, true, progress, token);
                });
        }

        private async void OnContextPasteClick(object sender, EventArgs e)
        {
            if (_browserClipboard == null || _settings == null) return;
            if (!string.Equals(_browserClipboard.ProfileId, _settings.ActiveBucketProfileId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_browserClipboard.BucketName, _settings.BucketName, StringComparison.Ordinal))
                return;

            R2BrowserItem source = CloneBrowserItem(_browserClipboard.Item);
            string targetPrefix = _browserContextItem != null && _browserContextItem.IsFolder
                ? _browserContextItem.Prefix
                : _browserPagination.Prefix;
            string leaf = R2ObjectOperationPathUtility.GetLeafName(
                source.IsFolder ? source.Prefix : source.Key,
                source.IsFolder);
            string proposed = R2ObjectOperationPathUtility.CombineWithPrefix(targetPrefix, leaf, source.IsFolder);
            string finalDestination = null;

            await RunBrowserOperationAsync(
                "Preparing paste...",
                "Paste complete.",
                true,
                async (settings, credentials, progress, token) =>
                {
                    finalDestination = await _objectOperationService.FindAvailableCopyDestinationAsync(
                        settings, credentials, proposed, source.IsFolder, token);
                    await _objectOperationService.CopyAsync(
                        settings, credentials, source, finalDestination, false, progress, token);
                });
        }

        private async void OnContextMoveClick(object sender, EventArgs e)
        {
            R2BrowserItem item = _browserContextItem;
            if (item == null) return;

            string current = item.IsFolder ? item.Prefix : item.Key;
            string destination;
            using (R2DestinationDialog dialog = new R2DestinationDialog(item.IsFolder, current))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                destination = dialog.Destination;
            }

            if (string.Equals(current, destination, StringComparison.Ordinal)) return;
            string detail = item.IsFolder
                ? "Every object under \"" + item.Prefix + "\" will be copied to \"" + destination +
                  "\", then the originals will be deleted. Existing destination keys are replaced or merged."
                : "The object will be copied to \"" + destination +
                  "\", then \"" + item.Key + "\" will be deleted. An existing destination object is replaced.";
            if (!R2ConfirmationDialog.Confirm(
                this, "Move R2 object", "Move " + item.DisplayName + "?", detail, "Move"))
                return;

            await RunBrowserOperationAsync(
                "Moving " + item.DisplayName + "...",
                "Move complete.",
                true,
                async (settings, credentials, progress, token) =>
                    await _objectOperationService.CopyAsync(
                        settings, credentials, item, destination, true, progress, token));
        }

        private async void OnContextDeleteClick(object sender, EventArgs e)
        {
            await DeleteSelectedBrowserItemsAsync();
            return;
            /* Legacy single-object implementation retained below for source compatibility. */
#pragma warning disable CS0162
            R2BrowserItem item = _browserContextItem;
            if (item == null) return;

            string detail = item.IsFolder
                ? "This permanently deletes every object whose key begins with \"" + item.Prefix +
                  "\". R2 virtual folders have no separate directory record, and this cannot be undone."
                : "This permanently deletes \"" + item.Key + "\" (" +
                  FileSizeFormatter.Format(item.Size) + "). This cannot be undone.";
            if (!R2ConfirmationDialog.Confirm(
                this,
                item.IsFolder ? "Delete virtual folder" : "Delete R2 object",
                "Delete " + item.DisplayName + "?",
                detail,
                "Delete"))
                return;

            await RunBrowserOperationAsync(
                "Deleting " + item.DisplayName + "...",
                "Delete complete.",
                true,
                async (settings, credentials, progress, token) =>
                    await _objectOperationService.DeleteAsync(
                        settings, credentials, item, progress, token));
#pragma warning restore CS0162
        }

        private async Task RunBrowserOperationAsync(
            string startingMessage,
            string successMessage,
            bool refreshAfterSuccess,
            Func<AppSettings, R2Credentials, IProgress<R2ObjectOperationProgress>, CancellationToken, Task> operation)
        {
            if (_browserOperationInProgress || operation == null || !_connectionVerified) return;

            CancelBrowserLoad(false);
            string profileId = _settings.ActiveBucketProfileId;
            string bucketName = _settings.BucketName;
            AppSettings settings = _settings.Clone();
            R2Credentials credentials = _credentials;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            _browserOperationCancellation = cancellation;
            _browserOperationInProgress = true;
            bool succeeded = false;

            browserStatusLabel.Text = startingMessage;
            browserStatusLabel.ForeColor = Theme.AccentBright;
            UpdateBrowserCommandStates();
            UpdateCommandStates();

            Progress<R2ObjectOperationProgress> progress = new Progress<R2ObjectOperationProgress>(value =>
            {
                if (!IsCurrentBrowserOperation(cancellation, profileId, bucketName)) return;
                browserStatusLabel.Text = FormatBrowserOperationProgress(value);
            });

            try
            {
                await operation(settings, credentials, progress, cancellation.Token).ConfigureAwait(true);
                if (!IsCurrentBrowserOperation(cancellation, profileId, bucketName)) return;

                succeeded = true;
                browserStatusLabel.Text = successMessage;
                browserStatusLabel.ForeColor = Theme.Success;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                if (!IsDisposed)
                {
                    browserStatusLabel.Text = "Operation cancelled.";
                    browserStatusLabel.ForeColor = Theme.TextMuted;
                }
            }
            catch (Exception ex)
            {
                if (!IsCurrentBrowserOperation(cancellation, profileId, bucketName)) return;

                BrowserOperationException browserError = ex as BrowserOperationException;
                FriendlyError friendly = FriendlyErrorService.Describe(ex, settings, null);
                string headline = browserError != null
                    ? "Object operation failed"
                    : (ex is InvalidOperationException ? "Object operation could not continue" : friendly.Headline);
                string detail = browserError != null
                    ? browserError.Message
                    : (ex is InvalidOperationException ? LoggingService.Sanitize(ex.Message) : friendly.Detail);
                string technical = browserError == null ? friendly.TechnicalDetails : browserError.TechnicalDetails;

                browserStatusLabel.Text = headline + ". " + detail;
                browserStatusLabel.ForeColor = Theme.Error;
                if (_log != null)
                {
                    _log.HttpFailure(
                        "Browser.Operation",
                        TransientErrorClassifier.GetStatusCode(ex),
                        TransientErrorClassifier.GetErrorCode(ex),
                        headline,
                        null,
                        null);
                }
                ErrorDialog.Show(this, headline, detail, technical);
            }
            finally
            {
                if (_browserOperationCancellation == cancellation)
                {
                    _browserOperationCancellation = null;
                    _browserOperationInProgress = false;
                    UpdateBrowserCommandStates();
                    UpdateCommandStates();
                }
                cancellation.Dispose();

                if (succeeded && refreshAfterSuccess &&
                    IsCurrentBrowserTarget(profileId, bucketName))
                {
                    RefreshBrowserAfterMutation();
                }
            }
        }

        private bool IsCurrentBrowserOperation(
            CancellationTokenSource cancellation,
            string profileId,
            string bucketName)
        {
            return !IsDisposed &&
                   _browserOperationCancellation == cancellation &&
                   !cancellation.IsCancellationRequested &&
                   IsCurrentBrowserTarget(profileId, bucketName);
        }

        private bool IsCurrentBrowserTarget(string profileId, string bucketName)
        {
            return _settings != null &&
                   string.Equals(_settings.ActiveBucketProfileId, profileId, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(_settings.BucketName, bucketName, StringComparison.Ordinal);
        }

        private static string FormatBrowserOperationProgress(R2ObjectOperationProgress progress)
        {
            if (progress == null) return "Working...";
            if (progress.TotalBytes > 0)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0}: {1}% ({2} / {3})",
                    progress.Operation,
                    progress.Percent,
                    FileSizeFormatter.Format(progress.TransferredBytes),
                    FileSizeFormatter.Format(progress.TotalBytes));
            }
            if (progress.CompletedObjects > 0)
                return progress.Operation + ": " + progress.CompletedObjects.ToString(CultureInfo.CurrentCulture) + " object(s) processed";
            return progress.Operation + "...";
        }

        private void RefreshBrowserAfterMutation()
        {
            InvalidateBrowserDetails();
            _browserContentVersion++;
            _browserNeedsRefresh = true;
            _browserHasLoaded = false;
            _browserPagination.Reset(_browserPagination.Prefix);
            if (_browserPageActive) BeginBrowserLoad();
        }

        private void ClearBrowserClipboardForTargetChange()
        {
            _browserClipboard = null;
            _browserContextItem = null;
        }

        private void CancelBrowserOperation()
        {
            CancellationTokenSource cancellation = _browserOperationCancellation;
            if (cancellation == null) return;
            _browserOperationCancellation = null;
            _browserOperationInProgress = false;
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private void DisposeBrowserActions()
        {
            CancelBrowserOperation();
            if (_browserContextMenu != null)
            {
                _browserContextMenu.Opening -= OnBrowserContextMenuOpening;
                _browserContextMenu.Dispose();
                _browserContextMenu = null;
            }
        }

        private static R2BrowserItem CloneBrowserItem(R2BrowserItem item)
        {
            return new R2BrowserItem
            {
                Key = item.Key,
                DisplayName = item.DisplayName,
                Prefix = item.Prefix,
                IsFolder = item.IsFolder,
                Size = item.Size,
                LastModifiedUtc = item.LastModifiedUtc,
                ETag = item.ETag
            };
        }

        private sealed class BrowserClipboardEntry
        {
            public BrowserClipboardEntry(R2BrowserItem item, string profileId, string bucketName)
            {
                Item = item;
                ProfileId = profileId;
                BucketName = bucketName;
            }

            public R2BrowserItem Item { get; private set; }
            public string ProfileId { get; private set; }
            public string BucketName { get; private set; }
        }

        private sealed class BrowserOperationException : Exception
        {
            public BrowserOperationException(string message, string technicalDetails)
                : base(message ?? "The operation failed.")
            {
                TechnicalDetails = technicalDetails;
            }

            public string TechnicalDetails { get; private set; }
        }
    }
}
