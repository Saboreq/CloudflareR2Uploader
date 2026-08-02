using System;
using System.IO;
using System.Threading;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Models
{
    /// <summary>
    /// One file in the upload queue.
    /// <para>
    /// Mutated by the upload worker and read by the UI, so every member is guarded by an
    /// internal lock. Consumers observe changes through <see cref="Changed"/>, which fires on
    /// whichever thread made the change; UI listeners must marshal with
    /// <see cref="UiThreadUtility"/>.
    /// </para>
    /// </summary>
    public sealed class UploadQueueItem
    {
        private readonly object _sync = new object();

        private UploadItemStatus _status = UploadItemStatus.Queued;
        private long _transferredBytes;
        private double _bytesPerSecond;
        private int _completedParts;
        private int _totalParts;
        private int _retryCount;
        private string _objectKey;
        private string _statusDetail = string.Empty;
        private string _errorMessage;
        private string _technicalDetails;
        private string _eTag;
        private string _publicUrl;
        private long _remoteLength = -1;
        private bool _hasResumableState;
        private TimeSpan _elapsed;

        public UploadQueueItem(string localFilePath, string relativePath, long fileSize, DateTime lastWriteUtc)
        {
            if (string.IsNullOrEmpty(localFilePath)) throw new ArgumentException("Local file path is required.", "localFilePath");

            Id = Guid.NewGuid();
            LocalFilePath = localFilePath;
            FileName = Path.GetFileName(localFilePath);
            RelativePath = string.IsNullOrEmpty(relativePath) ? FileName : relativePath;
            FileSize = fileSize;
            LastWriteUtc = lastWriteUtc;
            AddedUtc = DateTime.UtcNow;
        }

        /// <summary>Raised whenever any displayed value changes.</summary>
        public event EventHandler Changed;

        public Guid Id { get; private set; }
        public string LocalFilePath { get; private set; }
        public string FileName { get; private set; }

        /// <summary>Path relative to the folder the user picked, with '/' separators.</summary>
        public string RelativePath { get; private set; }

        public long FileSize { get; private set; }

        /// <summary>Recorded when the file was queued and re-checked before completing.</summary>
        public DateTime LastWriteUtc { get; private set; }

        public DateTime AddedUtc { get; private set; }

        /// <summary>True once the file is big enough to need a multipart upload.</summary>
        public bool WillUseMultipart(long thresholdBytes)
        {
            return FileSize >= thresholdBytes;
        }

        public string ObjectKey
        {
            get { lock (_sync) { return _objectKey; } }
            set
            {
                lock (_sync) { _objectKey = value; }
                RaiseChanged();
            }
        }

        public UploadItemStatus Status
        {
            get { lock (_sync) { return _status; } }
        }

        public string StatusDetail
        {
            get { lock (_sync) { return _statusDetail; } }
        }

        public long TransferredBytes
        {
            get { lock (_sync) { return _transferredBytes; } }
        }

        public double BytesPerSecond
        {
            get { lock (_sync) { return _bytesPerSecond; } }
        }

        public int CompletedParts { get { lock (_sync) { return _completedParts; } } }
        public int TotalParts { get { lock (_sync) { return _totalParts; } } }
        public int RetryCount { get { lock (_sync) { return _retryCount; } } }
        public string ErrorMessage { get { lock (_sync) { return _errorMessage; } } }
        public string TechnicalDetails { get { lock (_sync) { return _technicalDetails; } } }
        public string ETag { get { lock (_sync) { return _eTag; } } }
        public string PublicUrl { get { lock (_sync) { return _publicUrl; } } }
        public long RemoteLength { get { lock (_sync) { return _remoteLength; } } }
        public bool HasResumableState { get { lock (_sync) { return _hasResumableState; } } }
        public TimeSpan Elapsed { get { lock (_sync) { return _elapsed; } } }

        /// <summary>Fraction 0..1 of this file that has been transferred.</summary>
        public double Fraction
        {
            get
            {
                lock (_sync)
                {
                    if (_status == UploadItemStatus.Completed || _status == UploadItemStatus.Skipped) return 1.0;
                    if (FileSize <= 0) return _status == UploadItemStatus.Uploading ? 0.0 : 0.0;
                    double value = (double)_transferredBytes / FileSize;
                    if (value < 0) return 0;
                    return value > 1.0 ? 1.0 : value;
                }
            }
        }

        public int Percent { get { return (int)Math.Round(Fraction * 100.0); } }

        /// <summary>Cancellation source for the in-flight upload of this item, if any.</summary>
        internal CancellationTokenSource CancellationSource { get; set; }

        /// <summary>Set when the user chose to keep parts on cancel rather than abort them.</summary>
        internal bool PreservePartsOnCancel { get; set; }

        public void SetStatus(UploadItemStatus status, string detail = null)
        {
            lock (_sync)
            {
                _status = status;
                _statusDetail = detail ?? string.Empty;

                if (status == UploadItemStatus.Completed)
                {
                    _transferredBytes = FileSize;
                    _bytesPerSecond = 0;
                }
                else if (status.IsTerminal() || status == UploadItemStatus.Paused)
                {
                    _bytesPerSecond = 0;
                }
            }
            RaiseChanged();
        }

        public void ApplyProgress(UploadProgressInfo progress)
        {
            if (progress == null) return;

            lock (_sync)
            {
                _transferredBytes = progress.TransferredBytes;
                _bytesPerSecond = progress.BytesPerSecond;
                _completedParts = progress.CompletedParts;
                _totalParts = progress.TotalParts;
            }
            RaiseChanged();
        }

        public void SetElapsed(TimeSpan elapsed)
        {
            lock (_sync) { _elapsed = elapsed; }
        }

        public void SetMultipartLayout(int totalParts)
        {
            lock (_sync) { _totalParts = totalParts; }
            RaiseChanged();
        }

        public int IncrementRetryCount()
        {
            int value;
            lock (_sync) { value = ++_retryCount; }
            RaiseChanged();
            return value;
        }

        public void SetResumableState(bool available)
        {
            lock (_sync) { _hasResumableState = available; }
            RaiseChanged();
        }

        public void SetFailure(string message, string technicalDetails)
        {
            lock (_sync)
            {
                _status = UploadItemStatus.Failed;
                _errorMessage = message;
                _technicalDetails = technicalDetails;
                _bytesPerSecond = 0;
            }
            RaiseChanged();
        }

        public void SetVerified(string eTag, long remoteLength, string publicUrl)
        {
            lock (_sync)
            {
                _eTag = eTag;
                _remoteLength = remoteLength;
                _publicUrl = publicUrl;
            }
            RaiseChanged();
        }

        /// <summary>Clears transient state so a failed or cancelled item can be queued again.</summary>
        public void ResetForRetry()
        {
            lock (_sync)
            {
                _status = UploadItemStatus.Queued;
                _statusDetail = string.Empty;
                _errorMessage = null;
                _technicalDetails = null;
                _bytesPerSecond = 0;
                _completedParts = 0;
                // Transferred bytes are kept when resumable parts exist so the bar does not
                // jump backwards; otherwise start from zero.
                if (!_hasResumableState) _transferredBytes = 0;
            }
            RaiseChanged();
        }

        /// <summary>
        /// Re-reads the file's size and timestamp. Returns false when the file is gone or has
        /// changed since it was queued.
        /// </summary>
        public bool TryRefreshFileInfo(out string problem)
        {
            try
            {
                FileInfo info = new FileInfo(PathUtility.ToExtendedLengthPath(LocalFilePath));
                if (!info.Exists)
                {
                    problem = "The file no longer exists on disk.";
                    return false;
                }

                long size = info.Length;
                DateTime written = info.LastWriteTimeUtc;

                if (size != FileSize || written != LastWriteUtc)
                {
                    problem = "The file changed on disk after it was added to the queue.";
                    return false;
                }

                problem = null;
                return true;
            }
            catch (IOException ex)
            {
                problem = "The file could not be read: " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                problem = "Access to the file was denied.";
                return false;
            }
        }

        /// <summary>Adopts the file's current size and timestamp, e.g. after the user retries.</summary>
        public bool TryAdoptCurrentFileInfo(out string problem)
        {
            try
            {
                FileInfo info = new FileInfo(PathUtility.ToExtendedLengthPath(LocalFilePath));
                if (!info.Exists)
                {
                    problem = "The file no longer exists on disk.";
                    return false;
                }

                FileSize = info.Length;
                LastWriteUtc = info.LastWriteTimeUtc;
                problem = null;
                RaiseChanged();
                return true;
            }
            catch (IOException ex)
            {
                problem = "The file could not be read: " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                problem = "Access to the file was denied.";
                return false;
            }
        }

        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }
    }
}
