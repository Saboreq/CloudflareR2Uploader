using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using Ookii.Dialogs.WinForms;

namespace CloudflareR2Uploader.Forms
{
    public partial class MainForm
    {
        private R2BulkOperationService _bulkOperationService;
        private BulkLocalPathMapper _bulkPathMapper;
        private CancellationTokenSource _bulkOperationCancellation;
        private readonly IClipboardService _clipboardService = new WindowsClipboardService();
        private readonly IProcessLauncher _processLauncher = new WindowsProcessLauncher();
        private readonly PublicUrlService _publicUrlService = new PublicUrlService();

        private void InitializeBulkActions()
        {
            _bulkOperationService = new R2BulkOperationService(_log);
            _bulkPathMapper = new BulkLocalPathMapper();
        }

        private async Task DownloadSelectedBrowserItemsAsync()
        {
            List<R2BrowserItem> selection = GetSelectedBrowserItems();
            if (selection.Count == 0 || _bulkOperationCancellation != null) return;
            string destination;
            using (VistaFolderBrowserDialog dialog = new VistaFolderBrowserDialog { Description = "Choose where the selected R2 objects will be downloaded", UseDescriptionForTitle = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                destination = dialog.SelectedPath;
            }

            CancellationTokenSource cancellation = new CancellationTokenSource(); _bulkOperationCancellation = cancellation; _browserOperationInProgress = true;
            using (BulkOperationProgressDialog progressDialog = new BulkOperationProgressDialog("Download selected objects", cancellation))
            {
                Progress<BulkOperationProgress> progress = new Progress<BulkOperationProgress>(progressDialog.Report);
                progressDialog.Show(this);
                try
                {
                    BulkObjectOperationPlan plan = await _bulkOperationService.PlanAsync(_settings.Clone(), _credentials, selection, progress, cancellation.Token).ConfigureAwait(true);
                    _bulkPathMapper.Map(plan, destination);
                    if (_log != null) foreach (BulkObjectEntry entry in plan.Objects) _log.Debug("Bulk.Map", "Mapped object key '" + LoggingService.Sanitize(entry.Key) + "' to relative path '" + LoggingService.Sanitize(entry.RelativePath) + "'.");
                    if (!R2ConfirmationDialog.Confirm(this, "Bulk download", "Download " + plan.Objects.Count + " R2 object(s)?",
                        "The selection contains " + plan.SelectedFileCount + " file(s) and " + plan.SelectedFolderCount + " virtual folder(s), totaling " + FileSizeFormatter.Format(plan.TotalBytes) + ".\r\n\r\nDestination: " + destination, "Download")) return;
                    BulkObjectOperationResult result = await _bulkOperationService.DownloadAsync(_settings.Clone(), _credentials, plan, BulkConflictBehavior.Ask, PromptForConflictAsync, progress, cancellation.Token).ConfigureAwait(true);
                    ShowBulkSummary("Download complete", result, destination);
                }
                catch (OperationCanceledException) { MessageBox.Show(this, "The bulk download was cancelled. Completed files were kept; incomplete .r2partial files were removed.", "Download cancelled", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { ErrorDialog.Show(this, "Bulk download failed", LoggingService.Sanitize(ex.Message), FriendlyErrorService.BuildTechnicalDetails(ex, _settings, null)); }
                finally { progressDialog.Close(); FinishBulkOperation(cancellation); }
            }
        }

        private async Task DeleteSelectedBrowserItemsAsync()
        {
            List<R2BrowserItem> selection = GetSelectedBrowserItems();
            if (selection.Count == 0 || _bulkOperationCancellation != null) return;
            CancellationTokenSource cancellation = new CancellationTokenSource(); _bulkOperationCancellation = cancellation; _browserOperationInProgress = true;
            using (BulkOperationProgressDialog progressDialog = new BulkOperationProgressDialog("Delete selected objects", cancellation))
            {
                Progress<BulkOperationProgress> progress = new Progress<BulkOperationProgress>(progressDialog.Report); progressDialog.Show(this);
                try
                {
                    BulkObjectOperationPlan plan = await _bulkOperationService.PlanAsync(_settings.Clone(), _credentials, selection, progress, cancellation.Token).ConfigureAwait(true);
                    if (!R2ConfirmationDialog.Confirm(this, "Permanent bulk delete", "Permanently delete " + plan.Objects.Count + " R2 object(s)?",
                        "Your visible selection contains " + plan.SelectedFileCount + " file(s) and " + plan.SelectedFolderCount + " virtual folder(s).\r\nThe folders expand to " + plan.Objects.Count + " unique objects totaling approximately " + FileSizeFormatter.Format(plan.TotalBytes) + ".\r\n\r\nThis cannot be undone.", "Delete")) return;
                    BulkObjectOperationResult result = await _bulkOperationService.DeleteAsync(_settings.Clone(), _credentials, plan, progress, cancellation.Token).ConfigureAwait(true);
                    ShowBulkSummary("Delete complete", result, null);
                    RefreshBrowserAfterMutation();
                }
                catch (OperationCanceledException) { MessageBox.Show(this, "The bulk delete was cancelled. Objects deleted before cancellation remain deleted.", "Delete cancelled", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                catch (Exception ex) { ErrorDialog.Show(this, "Bulk delete failed", LoggingService.Sanitize(ex.Message), FriendlyErrorService.BuildTechnicalDetails(ex, _settings, null)); }
                finally { progressDialog.Close(); FinishBulkOperation(cancellation); }
            }
        }

        private Task<BulkConflictDecision> PromptForConflictAsync(string path)
        {
            TaskCompletionSource<BulkConflictDecision> completion = new TaskCompletionSource<BulkConflictDecision>();
            Action show = () =>
            {
                using (BulkConflictDialog dialog = new BulkConflictDialog(path))
                {
                    completion.TrySetResult(dialog.ShowDialog(this) == DialogResult.OK ? dialog.Decision : new BulkConflictDecision { Behavior = BulkConflictBehavior.Skip, ApplyToRemaining = false });
                }
            };
            if (InvokeRequired) BeginInvoke(show); else show();
            return completion.Task;
        }

        private void FinishBulkOperation(CancellationTokenSource cancellation)
        {
            if (_bulkOperationCancellation == cancellation) _bulkOperationCancellation = null;
            _browserOperationInProgress = false; cancellation.Dispose(); UpdateBrowserCommandStates(); UpdateCommandStates();
        }

        private void ShowBulkSummary(string title, BulkObjectOperationResult result, string destination)
        {
            string failures = result.Failures.Count == 0 ? "None" : string.Join("\r\n", result.Failures.Take(20).Select(value => value.Key + ": " + value.Message).ToArray());
            string message = "Succeeded: " + result.Succeeded + "\r\nProcessed: " + result.Completed + "\r\nSkipped: " + result.Skipped + "\r\nRenamed: " + result.Renamed + "\r\nFailed: " + result.Failures.Count + "\r\nCancelled: " + (result.Cancelled ? "Yes" : "No") +
                             (string.IsNullOrEmpty(destination) ? string.Empty : "\r\nDestination: " + destination) + "\r\n\r\nFailure details:\r\n" + failures;
            using (BulkOperationSummaryDialog dialog = new BulkOperationSummaryDialog(title, message, destination, result.Failures.Count > 0)) dialog.ShowDialog(this);
            RaiseBackgroundOperationCompleted(title + ": " + result.Succeeded + " succeeded, " + result.Failures.Count + " failed.", result.Failures.Count > 0);
        }

        private void CopySelectedObjectKeys()
        {
            List<R2BrowserItem> selection = GetSelectedBrowserItems(); if (selection.Count == 0) return;
            string text = string.Join(Environment.NewLine, selection.Select(item => item.IsFolder ? item.Prefix : item.Key).ToArray());
            TrySetClipboard(text, "object key(s)");
        }

        private void CopySelectedPublicUrls()
        {
            List<R2BrowserItem> selection = GetSelectedBrowserItems(); List<string> urls = selection.Where(item => !item.IsFolder).Select(item => _publicUrlService.Build(_settings.PublicBaseUrl, item)).Where(value => !string.IsNullOrEmpty(value)).ToList();
            if (urls.Count == 0) { MessageBox.Show(this, string.IsNullOrWhiteSpace(_settings.PublicBaseUrl) ? "Configure PublicBaseUrl for this bucket profile first. A configured base URL does not make a private bucket public." : "Public URLs apply to files, not virtual folders.", "Public URLs", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            TrySetClipboard(string.Join(Environment.NewLine, urls.ToArray()), "public URL(s)");
            int ignored = selection.Count - urls.Count; if (ignored > 0) MessageBox.Show(this, ignored + " selected folder(s) were ignored because virtual folders do not have public URLs.", "Public URLs copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenSelectedPublicUrl()
        {
            List<R2BrowserItem> selection = GetSelectedBrowserItems(); if (selection.Count != 1 || selection[0].IsFolder) return;
            string url = _publicUrlService.Build(_settings.PublicBaseUrl, selection[0]); if (string.IsNullOrEmpty(url)) { MessageBox.Show(this, "Configure PublicBaseUrl first. This setting does not make a private bucket public.", "Public URL unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try { _processLauncher.Open(url); } catch (Exception ex) { ErrorDialog.Show(this, "The URL could not be opened", LoggingService.Sanitize(ex.Message), null); }
        }

        private void ShowPresignedUrlForSelection()
        {
            List<R2BrowserItem> selection = GetSelectedBrowserItems(); if (selection.Count != 1 || selection[0].IsFolder) return;
            using (PresignedUrlDialog dialog = new PresignedUrlDialog(_settings.Clone(), _credentials, selection[0], _clipboardService, _processLauncher)) dialog.ShowDialog(this);
        }

        private void TrySetClipboard(string text, string description)
        {
            try { _clipboardService.SetText(text); browserStatusLabel.Text = "Copied " + description + "."; }
            catch (Exception ex) { ErrorDialog.Show(this, "Clipboard unavailable", LoggingService.Sanitize(ex.Message), null); }
        }

        private void CancelBulkOperation()
        {
            CancellationTokenSource cancellation = _bulkOperationCancellation; if (cancellation != null) { try { cancellation.Cancel(); } catch (ObjectDisposedException) { } }
        }
    }
}
