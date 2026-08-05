using System;
using System.Globalization;
using System.IO;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudflareR2Uploader.Wpf.ViewModels.Upload
{
    /// <summary>
    /// One row of the upload queue, implementing the UPLOAD QUEUE ROWS composite in
    /// Images/10-design-system-composites.png in all five states.
    /// <para>
    /// The underlying <see cref="UploadQueueItem"/> is mutated by upload workers on
    /// background threads and raises <c>Changed</c> from whichever thread made the change.
    /// This view model deliberately does <em>not</em> subscribe to that event: the upload
    /// screen pulls every row on one throttled dispatcher tick instead, so a file with
    /// thousands of part callbacks per second cannot flood the UI queue.
    /// </para>
    /// </summary>
    public sealed partial class UploadQueueItemViewModel : ObservableObject
    {
        private readonly Action<UploadQueueItemViewModel> _pauseOrResume;
        private readonly Action<UploadQueueItemViewModel> _cancel;
        private readonly Action<UploadQueueItemViewModel> _retry;
        private readonly Action<UploadQueueItemViewModel> _remove;
        private readonly Action<UploadQueueItemViewModel> _copyLink;

        public UploadQueueItemViewModel(
            UploadQueueItem item,
            Action<UploadQueueItemViewModel> pauseOrResume,
            Action<UploadQueueItemViewModel> cancel,
            Action<UploadQueueItemViewModel> retry,
            Action<UploadQueueItemViewModel> remove,
            Action<UploadQueueItemViewModel> copyLink)
        {
            Item = item;
            _pauseOrResume = pauseOrResume;
            _cancel = cancel;
            _retry = retry;
            _remove = remove;
            _copyLink = copyLink;

            FileName = item.FileName;
            TotalSizeText = FileSizeFormatter.Format(item.FileSize);
            IconKey = ResolveIconKey(item.FileName);

            Refresh();
        }

        public UploadQueueItem Item { get; }

        public string FileName { get; }

        public string TotalSizeText { get; }

        public string IconKey { get; }

        [ObservableProperty]
        private UploadItemStatus _status;

        [ObservableProperty]
        private string _statusText = string.Empty;

        /// <summary>"part 12 / 19", "ETag verified", "503 SlowDown · retry 2 of 5", "waiting for slot".</summary>
        [ObservableProperty]
        private string _detailText = string.Empty;

        [ObservableProperty]
        private string _transferredText = "0 B";

        [ObservableProperty]
        private string _speedText = string.Empty;

        [ObservableProperty]
        private string _remainingText = string.Empty;

        [ObservableProperty]
        private double _percent;

        [ObservableProperty]
        private bool _canPause;

        [ObservableProperty]
        private bool _canResume;

        [ObservableProperty]
        private bool _canCancel;

        [ObservableProperty]
        private bool _canRetry;

        [ObservableProperty]
        private bool _canRemove;

        [ObservableProperty]
        private bool _canCopyLink;

        [ObservableProperty]
        private string _automationName = string.Empty;

        /// <summary>
        /// Pulls the current values out of the thread-safe queue item. Called on the UI thread
        /// by the upload screen's single throttled timer.
        /// </summary>
        public void Refresh()
        {
            UploadItemStatus status = Item.Status;
            Status = status;
            StatusText = status.ToDisplayText();
            Percent = Item.Percent;

            TransferredText = status == UploadItemStatus.Completed
                ? FileSizeFormatter.Format(Item.FileSize)
                : FileSizeFormatter.Format(Item.TransferredBytes);

            DetailText = BuildDetail(status);
            SpeedText = status == UploadItemStatus.Uploading
                ? FileSizeFormatter.FormatSpeed(Item.BytesPerSecond)
                : string.Empty;

            RemainingText = BuildRemaining(status);

            CanPause = status == UploadItemStatus.Uploading;
            CanResume = status == UploadItemStatus.Paused;
            CanCancel = !status.IsTerminal();
            CanRetry = status is UploadItemStatus.Failed or UploadItemStatus.Cancelled;
            CanRemove = status.IsTerminal();
            CanCopyLink = status == UploadItemStatus.Completed && !string.IsNullOrEmpty(Item.PublicUrl);

            AutomationName = FileName + ", " + StatusText +
                             (Percent > 0 && Percent < 100
                                 ? ", " + Percent.ToString("0", CultureInfo.CurrentCulture) + " percent"
                                 : string.Empty);
        }

        private string BuildDetail(UploadItemStatus status)
        {
            switch (status)
            {
                case UploadItemStatus.Completed:
                    return string.IsNullOrEmpty(Item.ETag) ? "Uploaded" : "ETag verified";

                case UploadItemStatus.Failed:
                    string reason = Item.ErrorMessage ?? "Upload failed";
                    if (reason.Length > 60) reason = reason[..60].TrimEnd() + "…";
                    return Item.RetryCount > 0
                        ? reason + " · retry " + Item.RetryCount.ToString(CultureInfo.CurrentCulture)
                        : reason;

                case UploadItemStatus.Queued:
                    return "waiting for slot";

                case UploadItemStatus.Uploading:
                case UploadItemStatus.Paused:
                    int total = Item.TotalParts;
                    if (total > 0)
                    {
                        return "part " + Item.CompletedParts.ToString(CultureInfo.CurrentCulture) +
                               " / " + total.ToString(CultureInfo.CurrentCulture);
                    }
                    return Item.StatusDetail;

                default:
                    return Item.StatusDetail;
            }
        }

        private string BuildRemaining(UploadItemStatus status)
        {
            if (status == UploadItemStatus.Completed)
            {
                TimeSpan elapsed = Item.Elapsed;
                return elapsed > TimeSpan.Zero
                    ? "in " + elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + "s"
                    : string.Empty;
            }

            if (status == UploadItemStatus.Paused) return "— left";

            if (status != UploadItemStatus.Uploading) return string.Empty;

            long remaining = Item.FileSize - Item.TransferredBytes;
            return DurationFormatter.FormatEta(remaining, Item.BytesPerSecond) + " left";
        }

        private static string ResolveIconKey(string fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty).TrimStart('.').ToLowerInvariant();
            return extension switch
            {
                "jpg" or "jpeg" or "png" or "gif" or "webp" or "bmp" or "svg" => "Icon.FileImage",
                "mp4" or "mov" or "avi" or "mkv" or "webm" => "Icon.FileVideo",
                "mp3" or "wav" or "flac" or "ogg" or "m4a" => "Icon.FileAudio",
                "zip" or "7z" or "rar" or "gz" or "tar" or "tgz" => "Icon.FileArchive",
                "exe" or "msi" or "appimage" or "bin" => "Icon.FileBinary",
                "dmg" or "iso" => "Icon.FileDisk",
                "sig" or "asc" or "pub" => "Icon.FileSignature",
                _ => "Icon.File"
            };
        }

        [RelayCommand]
        private void PauseOrResume() => _pauseOrResume(this);

        [RelayCommand]
        private void Cancel() => _cancel(this);

        [RelayCommand]
        private void Retry() => _retry(this);

        [RelayCommand]
        private void Remove() => _remove(this);

        [RelayCommand]
        private void CopyLink() => _copyLink(this);
    }
}
