using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    public sealed class QueueItemEventArgs : EventArgs
    {
        public QueueItemEventArgs(UploadQueueItem item) { Item = item; }
        public UploadQueueItem Item { get; private set; }
    }

    public sealed class UploadFinishedEventArgs : EventArgs
    {
        public UploadFinishedEventArgs(UploadQueueItem item, UploadResult result)
        {
            Item = item;
            Result = result;
        }

        public UploadQueueItem Item { get; private set; }
        public UploadResult Result { get; private set; }
    }

    public sealed class AddFilesResult
    {
        public AddFilesResult(int added, int duplicates, List<string> inaccessible, List<string> unreadable)
        {
            Added = added;
            Duplicates = duplicates;
            Inaccessible = inaccessible ?? new List<string>();
            Unreadable = unreadable ?? new List<string>();
        }

        public int Added { get; private set; }
        public int Duplicates { get; private set; }
        public List<string> Inaccessible { get; private set; }
        public List<string> Unreadable { get; private set; }
    }

    /// <summary>
    /// Owns the upload queue and runs it. Files are uploaded one at a time; concurrency lives
    /// inside a file's parts, which keeps the speed and remaining-time figures meaningful and
    /// avoids many large files competing for the same connection pool.
    /// </summary>
    public sealed class UploadQueueService : IDisposable
    {
        private readonly ILoggingService _log;
        private readonly R2UploadService _uploadService;
        private readonly UploadStateStore _stateStore;
        private readonly PauseController _pauseController = new PauseController();

        private readonly object _sync = new object();
        private readonly List<UploadQueueItem> _items = new List<UploadQueueItem>();
        private readonly HashSet<string> _knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly SpeedEstimator _overallSpeed = new SpeedEstimator();

        private CancellationTokenSource _runCancellation;
        private Task _runTask;
        private bool _disposed;

        public UploadQueueService(ILoggingService log, UploadStateStore stateStore)
        {
            _log = log;
            _stateStore = stateStore;
            _uploadService = new R2UploadService(log, stateStore);
        }

        public event EventHandler<QueueItemEventArgs> ItemAdded;
        public event EventHandler<QueueItemEventArgs> ItemRemoved;
        public event EventHandler<UploadFinishedEventArgs> ItemFinished;
        public event EventHandler RunStateChanged;

        public PauseController PauseController { get { return _pauseController; } }
        public R2UploadService UploadService { get { return _uploadService; } }

        /// <summary>Set by the form so the service can ask about overwrites on the UI thread.</summary>
        public IOverwritePrompt OverwritePrompt { get; set; }

        /// <summary>Supplies the settings and credentials in force when a run starts.</summary>
        public Func<AppSettings> SettingsProvider { get; set; }
        public Func<R2Credentials> CredentialsProvider { get; set; }

        public bool IsRunning
        {
            get
            {
                Task task = _runTask;
                return task != null && !task.IsCompleted;
            }
        }

        public bool IsPaused { get { return _pauseController.IsPaused; } }

        public List<UploadQueueItem> GetItems()
        {
            lock (_sync) { return new List<UploadQueueItem>(_items); }
        }

        public int Count { get { lock (_sync) { return _items.Count; } } }

        // ------------------------------------------------------------------------- queueing

        /// <summary>
        /// Adds files and folders. Folders are walked recursively; unreadable folders are
        /// reported rather than aborting the whole scan. Duplicates are ignored.
        /// </summary>
        public AddFilesResult AddPaths(IEnumerable<string> paths, AppSettings settings)
        {
            if (paths == null) return new AddFilesResult(0, 0, null, null);

            List<string> inaccessible = new List<string>();
            List<string> unreadable = new List<string>();
            List<UploadQueueItem> added = new List<UploadQueueItem>();
            int duplicates = 0;

            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;

                if (PathUtility.IsDirectory(path))
                {
                    string root = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    List<string> files = PathUtility.EnumerateFilesSafely(root, inaccessible);

                    foreach (string file in files)
                    {
                        string relative = PathUtility.GetRelativePath(Path.GetDirectoryName(root) ?? root, file);
                        if (!TryAddFile(file, relative, unreadable, added)) duplicates++;
                    }
                }
                else
                {
                    if (!TryAddFile(path, Path.GetFileName(path), unreadable, added)) duplicates++;
                }
            }

            if (added.Count > 0)
            {
                RecomputeKeys(settings);

                foreach (UploadQueueItem item in added)
                {
                    RaiseItemAdded(item);
                }
            }

            if (_log != null && (added.Count > 0 || inaccessible.Count > 0 || unreadable.Count > 0))
            {
                _log.Info("Queue.Add", string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Added {0} file(s); {1} duplicate(s) ignored; {2} unreadable folder(s); {3} unreadable file(s).",
                    added.Count, duplicates, inaccessible.Count, unreadable.Count));
            }

            return new AddFilesResult(added.Count, duplicates, inaccessible, unreadable);
        }

        private bool TryAddFile(string path, string relativePath, List<string> unreadable, List<UploadQueueItem> added)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(PathUtility.ToExtendedLengthPath(path));
                if (!info.Exists)
                {
                    unreadable.Add(path);
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                       ex is ArgumentException || ex is NotSupportedException)
            {
                unreadable.Add(path);
                return true;
            }

            UploadQueueItem item = new UploadQueueItem(
                info.FullName,
                string.IsNullOrEmpty(relativePath) ? info.Name : relativePath.Replace('\\', '/'),
                info.Length,
                info.LastWriteTimeUtc);

            lock (_sync)
            {
                if (!_knownFiles.Add(info.FullName)) return false;   // already queued
                _items.Add(item);
            }

            added.Add(item);
            return true;
        }

        /// <summary>
        /// Recomputes every pending item's destination key from the current destination
        /// settings. Items that are running or already finished keep the key they used.
        /// </summary>
        public void RecomputeKeys(AppSettings settings, string customSingleFileName = null)
        {
            if (settings == null) return;

            List<UploadQueueItem> snapshot = GetItems();
            bool useCustomName = !string.IsNullOrWhiteSpace(customSingleFileName) && snapshot.Count == 1;

            foreach (UploadQueueItem item in snapshot)
            {
                if (item.Status.IsTerminal() || item.Status.IsActive()) continue;

                string leaf = useCustomName
                    ? ObjectKeyUtility.Normalize(customSingleFileName)
                    : (settings.PreserveFolderStructure ? item.RelativePath : item.FileName);

                if (string.IsNullOrEmpty(leaf)) leaf = item.FileName;

                item.ObjectKey = ObjectKeyUtility.Combine(settings.KeyPrefix, leaf);
            }
        }

        public void Remove(UploadQueueItem item)
        {
            if (item == null) return;

            CancelItem(item);

            lock (_sync)
            {
                _items.Remove(item);
                _knownFiles.Remove(item.LocalFilePath);
            }

            RaiseItemRemoved(item);
        }

        public void ClearCompleted()
        {
            List<UploadQueueItem> removed = new List<UploadQueueItem>();

            lock (_sync)
            {
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    UploadItemStatus status = _items[i].Status;
                    if (status == UploadItemStatus.Completed || status == UploadItemStatus.Skipped)
                    {
                        removed.Add(_items[i]);
                        _knownFiles.Remove(_items[i].LocalFilePath);
                        _items.RemoveAt(i);
                    }
                }
            }

            foreach (UploadQueueItem item in removed) RaiseItemRemoved(item);
        }

        public void ClearAll()
        {
            List<UploadQueueItem> removed;

            lock (_sync)
            {
                removed = new List<UploadQueueItem>(_items);
                _items.Clear();
                _knownFiles.Clear();
            }

            foreach (UploadQueueItem item in removed)
            {
                CancelItem(item);
                RaiseItemRemoved(item);
            }
        }

        /// <summary>Requeues every failed item. Resumable multipart uploads continue, not restart.</summary>
        public int RetryFailed()
        {
            int count = 0;
            foreach (UploadQueueItem item in GetItems())
            {
                if (item.Status == UploadItemStatus.Failed || item.Status == UploadItemStatus.Cancelled)
                {
                    item.ResetForRetry();
                    count++;
                }
            }
            return count;
        }

        public void RetryItem(UploadQueueItem item)
        {
            if (item == null) return;
            if (item.Status == UploadItemStatus.Failed ||
                item.Status == UploadItemStatus.Cancelled ||
                item.Status == UploadItemStatus.Skipped)
            {
                item.ResetForRetry();
            }
        }

        // ---------------------------------------------------------------------- run control

        /// <summary>
        /// Starts uploading. Returns the running task so callers can await completion; the UI
        /// never blocks on it.
        /// </summary>
        public Task StartAsync()
        {
            lock (_sync)
            {
                if (_runTask != null && !_runTask.IsCompleted) return _runTask;

                if (_runCancellation != null) _runCancellation.Dispose();
                _runCancellation = new CancellationTokenSource();

                _pauseController.Resume();
                _runTask = Task.Run(() => RunAsync(_runCancellation.Token));
            }

            RaiseRunStateChanged();
            return _runTask;
        }

        public void Pause()
        {
            _pauseController.Pause();

            AppSettings settings = SettingsProvider != null ? SettingsProvider() : null;
            foreach (UploadQueueItem item in GetItems())
            {
                if (item.Status == UploadItemStatus.Uploading)
                {
                    if (settings != null && item.WillUseMultipart(settings.MultipartThresholdBytes))
                    {
                        item.SetStatus(
                            UploadItemStatus.Paused,
                            "Finishing the multipart parts already in flight…");
                    }
                    else
                    {
                        // A PutObject request cannot be suspended safely. Keep its honest
                        // uploading state while preventing the queue from starting another
                        // file after it completes.
                        item.SetStatus(
                            UploadItemStatus.Uploading,
                            "This single-request upload will finish; the queue is paused before the next file.");
                    }
                }
            }

            if (_log != null) _log.Info("Queue.Pause", "Upload paused; no new parts or files will be scheduled.");
            RaiseRunStateChanged();
        }

        public void Resume()
        {
            _pauseController.Resume();

            foreach (UploadQueueItem item in GetItems())
            {
                if (item.Status == UploadItemStatus.Paused) item.SetStatus(UploadItemStatus.Uploading);
            }

            if (_log != null) _log.Info("Queue.Resume", "Upload resumed.");

            if (!IsRunning) StartAsync();
            else RaiseRunStateChanged();
        }

        /// <summary>Cancels the whole run. <paramref name="preserveParts"/> keeps multipart state.</summary>
        public void CancelAll(bool preserveParts)
        {
            foreach (UploadQueueItem item in GetItems())
            {
                item.PreservePartsOnCancel = preserveParts;
            }

            _pauseController.Resume();   // never leave a cancelled run parked at the pause gate

            CancellationTokenSource source;
            lock (_sync) { source = _runCancellation; }

            if (source != null)
            {
                try { source.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            foreach (UploadQueueItem item in GetItems())
            {
                if (item.Status == UploadItemStatus.Queued) item.SetStatus(UploadItemStatus.Cancelled);
            }

            if (_log != null) _log.Info("Queue.Cancel", "Upload cancelled (preserveParts=" + preserveParts + ").");
            RaiseRunStateChanged();
        }

        public void CancelItem(UploadQueueItem item)
        {
            if (item == null) return;

            CancellationTokenSource source = item.CancellationSource;
            if (source != null)
            {
                try { source.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            else if (item.Status == UploadItemStatus.Queued)
            {
                item.SetStatus(UploadItemStatus.Cancelled);
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            AppSettings settings = SettingsProvider != null ? SettingsProvider() : null;
            R2Credentials credentials = CredentialsProvider != null ? CredentialsProvider() : null;

            if (settings == null || credentials == null || !credentials.IsComplete)
            {
                if (_log != null) _log.Error("Queue.Run", "Upload cannot start: the connection is not configured.", null);
                return;
            }

            _overallSpeed.Start(GetTransferredBytes());

            AmazonS3Client client = null;
            try
            {
                client = R2ClientFactory.CreateClient(settings, credentials);

                while (!cancellationToken.IsCancellationRequested)
                {
                    UploadQueueItem next = TakeNextQueuedItem();
                    if (next == null) break;

                    await _pauseController.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);

                    await UploadOneAsync(client, next, settings, credentials, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                if (_log != null) _log.Info("Queue.Run", "The upload run was cancelled.");
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Error("Queue.Run", "The upload run stopped because of an unexpected error.", ex);

                // Surface it on every item still waiting, rather than failing silently.
                FriendlyError friendly = FriendlyErrorService.Describe(ex, settings, null);
                foreach (UploadQueueItem item in GetItems())
                {
                    if (item.Status == UploadItemStatus.Queued)
                        item.SetFailure(friendly.FullMessage, friendly.TechnicalDetails);
                }
            }
            finally
            {
                if (client != null) client.Dispose();
                _overallSpeed.Stop();
                RaiseRunStateChanged();
            }
        }

        private async Task UploadOneAsync(
            AmazonS3Client client,
            UploadQueueItem item,
            AppSettings settings,
            R2Credentials credentials,
            CancellationToken runToken)
        {
            using (CancellationTokenSource itemCancellation = CancellationTokenSource.CreateLinkedTokenSource(runToken))
            {
                item.CancellationSource = itemCancellation;

                Progress<UploadProgressInfo> progress = new Progress<UploadProgressInfo>(info =>
                {
                    item.ApplyProgress(info);
                    _overallSpeed.Report(GetTransferredBytes());
                });

                try
                {
                    UploadResult result = await _uploadService.UploadAsync(
                        client, item, settings, credentials, OverwritePrompt,
                        _pauseController, progress, itemCancellation.Token).ConfigureAwait(false);

                    RaiseItemFinished(item, result);

                    // A paused item goes back into the queue so Resume picks it up again.
                    if (result.Outcome == UploadOutcome.Paused)
                    {
                        await _pauseController.WaitWhilePausedAsync(runToken).ConfigureAwait(false);
                        item.SetStatus(UploadItemStatus.Queued);
                    }
                }
                finally
                {
                    item.CancellationSource = null;
                }
            }
        }

        private UploadQueueItem TakeNextQueuedItem()
        {
            lock (_sync)
            {
                foreach (UploadQueueItem item in _items)
                {
                    if (item.Status == UploadItemStatus.Queued) return item;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ overall figures

        private long GetTransferredBytes()
        {
            long total = 0;
            foreach (UploadQueueItem item in GetItems()) total += item.TransferredBytes;
            return total;
        }

        /// <summary>Snapshot of the whole queue for the status bar.</summary>
        public OverallProgressInfo GetOverallProgress()
        {
            long transferred = 0;
            long total = 0;
            int succeeded = 0, failed = 0, queued = 0, active = 0, skipped = 0, cancelled = 0;

            foreach (UploadQueueItem item in GetItems())
            {
                total += item.FileSize;
                transferred += item.Status == UploadItemStatus.Completed ? item.FileSize : item.TransferredBytes;

                switch (item.Status)
                {
                    case UploadItemStatus.Completed: succeeded++; break;
                    case UploadItemStatus.Failed: failed++; break;
                    case UploadItemStatus.Skipped: skipped++; break;
                    case UploadItemStatus.Cancelled: cancelled++; break;
                    case UploadItemStatus.Queued: queued++; break;
                    case UploadItemStatus.Paused: queued++; break;
                    default: active++; break;
                }
            }

            return new OverallProgressInfo(
                transferred, total,
                IsRunning && !IsPaused ? _overallSpeed.BytesPerSecond : 0,
                _overallSpeed.Elapsed,
                succeeded, failed, queued, active, skipped, cancelled);
        }

        // ------------------------------------------------------------------------- plumbing

        private void RaiseItemAdded(UploadQueueItem item)
        {
            EventHandler<QueueItemEventArgs> handler = ItemAdded;
            if (handler != null) handler(this, new QueueItemEventArgs(item));
        }

        private void RaiseItemRemoved(UploadQueueItem item)
        {
            EventHandler<QueueItemEventArgs> handler = ItemRemoved;
            if (handler != null) handler(this, new QueueItemEventArgs(item));
        }

        private void RaiseItemFinished(UploadQueueItem item, UploadResult result)
        {
            EventHandler<UploadFinishedEventArgs> handler = ItemFinished;
            if (handler != null) handler(this, new UploadFinishedEventArgs(item, result));
        }

        private void RaiseRunStateChanged()
        {
            EventHandler handler = RunStateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { CancelAll(true); }
            catch (ObjectDisposedException) { }

            lock (_sync)
            {
                if (_runCancellation != null)
                {
                    _runCancellation.Dispose();
                    _runCancellation = null;
                }
            }

            _pauseController.Dispose();
        }
    }
}
