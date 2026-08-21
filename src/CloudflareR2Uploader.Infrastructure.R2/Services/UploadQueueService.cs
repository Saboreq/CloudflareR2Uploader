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
    public sealed class UploadQueueService : IUploadQueueLifecycle, IDisposable
    {
        private readonly ILoggingService _log;
        private readonly R2UploadService _uploadService;
        private readonly UploadStateStore _stateStore;
        private readonly PauseController _pauseController = new PauseController();
        private readonly Action<UploadQueueItem> _retryItem = RetryItemCore;
        private readonly Action<UploadQueueItem> _cancelItem = CancelItemCore;
        private readonly Func<CancellationToken, Task> _runOverride;

        private readonly object _sync = new object();
        private static readonly AsyncLocal<RunStateNotificationContext> s_runStateNotification = new();
        private readonly TaskCompletionSource _disposalCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<UploadQueueItem> _items = new List<UploadQueueItem>();
        private readonly HashSet<string> _knownFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly SpeedEstimator _overallSpeed = new SpeedEstimator();

        private RunGeneration _generation;
        private bool _disposed;
        private bool _disposalIsSynchronous;
        private int _synchronousDisposeOwnerThreadId;
        private int _lifecycleResourcesDisposed;

        private sealed class RunGeneration
        {
            private int _cancellationDisposed;
            private int _stopLifecycleRunning;
            private readonly TaskCompletionSource _stopSignalCompleted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public RunGeneration(CancellationTokenSource cancellation, Task runTask)
            {
                Cancellation = cancellation;
                RunTask = runTask;
                LifecycleTask = runTask;
            }

            public CancellationTokenSource Cancellation { get; }
            public Task RunTask { get; }
            public Task LifecycleTask { get; private set; }
            public bool StopRequested { get; set; }
            public bool PreserveParts { get; set; }
            public bool IsRunning => StopRequested
                ? Volatile.Read(ref _stopLifecycleRunning) != 0
                : !RunTask.IsCompleted;

            public static RunGeneration CreateSynthetic() =>
                new(null, Task.CompletedTask);

            public void LatchStop(bool preserveParts, Action publishTerminalState)
            {
                StopRequested = true;
                PreserveParts = preserveParts;
                Volatile.Write(ref _stopLifecycleRunning, 1);
                LifecycleTask = CompleteStopLifecycleAsync(publishTerminalState);
                _ = LifecycleTask.ContinueWith(
                    task => { _ = task.Exception; },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            private async Task CompleteStopLifecycleAsync(Action publishTerminalState)
            {
                try
                {
                    await Task.WhenAll(RunTask, _stopSignalCompleted.Task).ConfigureAwait(false);
                }
                finally
                {
                    // Keep LifecycleTask incomplete until subscribers have observed the terminal
                    // state. Start cannot replace this generation while the callback is running.
                    Volatile.Write(ref _stopLifecycleRunning, 0);
                    publishTerminalState();
                }
            }

            public void CompleteStopSignal() => _stopSignalCompleted.TrySetResult();

            public void DisposeCancellation()
            {
                if (Interlocked.Exchange(ref _cancellationDisposed, 1) == 0)
                    Cancellation?.Dispose();
            }
        }

        private sealed class StopRequest
        {
            public StopRequest(
                RunGeneration generation,
                bool newlyLatched,
                List<UploadQueueItem> queuedItems,
                bool preserveParts)
            {
                Generation = generation;
                NewlyLatched = newlyLatched;
                QueuedItems = queuedItems;
                PreserveParts = preserveParts;
            }

            public RunGeneration Generation { get; }
            public bool NewlyLatched { get; }
            public List<UploadQueueItem> QueuedItems { get; }
            public bool PreserveParts { get; }
        }

        private sealed class RunStateNotificationContext
        {
            public RunStateNotificationContext(UploadQueueService queue, RunGeneration terminalGeneration)
            {
                Queue = queue;
                TerminalGeneration = terminalGeneration;
            }

            public UploadQueueService Queue { get; }
            public RunGeneration TerminalGeneration { get; }
        }

        public UploadQueueService(ILoggingService log, UploadStateStore stateStore)
        {
            _log = log;
            _stateStore = stateStore;
            _uploadService = new R2UploadService(log, stateStore);
        }

        internal UploadQueueService(
            ILoggingService log,
            UploadStateStore stateStore,
            Func<CancellationToken, Task> runOverride)
            : this(log, stateStore)
        {
            _runOverride = runOverride;
        }

        public event EventHandler<QueueItemEventArgs> ItemAdded;
        public event EventHandler<QueueItemEventArgs> ItemRemoved;
        public event EventHandler<UploadFinishedEventArgs> ItemFinished;
        public event EventHandler RunStateChanged;

        public PauseController PauseController { get { return _pauseController; } }
        public R2UploadService UploadService { get { return _uploadService; } }
        internal Task DisposalCompleted => _disposalCompleted.Task;

        /// <summary>Set by the form so the service can ask about overwrites on the UI thread.</summary>
        public IOverwritePrompt OverwritePrompt { get; set; }

        /// <summary>Supplies the settings and credentials in force when a run starts.</summary>
        public Func<AppSettings> SettingsProvider { get; set; }
        public Func<R2Credentials> CredentialsProvider { get; set; }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _generation != null && _generation.IsRunning;
                }
            }
        }

        public bool IsPaused { get { return _pauseController.IsPaused; } }

        public List<UploadQueueItem> GetItems()
        {
            lock (_sync) { return new List<UploadQueueItem>(_items); }
        }

        IReadOnlyList<UploadQueueItem> IUploadQueueLifecycle.GetItems() => GetItems();

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
            _retryItem(item);
        }

        private static void RetryItemCore(UploadQueueItem item)
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
        /// never blocks on it. A reentrant call from the terminal <see cref="RunStateChanged"/>
        /// notification may start the next generation immediately; unrelated callers remain
        /// gated on the terminating generation until that notification returns.
        /// </summary>
        public Task StartAsync()
        {
            RunGeneration completedGeneration;
            Task runTask;
            TaskCompletionSource launch = new(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_generation != null &&
                    !_generation.LifecycleTask.IsCompleted &&
                    !CanRestartFromTerminalNotificationLocked(_generation))
                {
                    return _generation.LifecycleTask;
                }

                completedGeneration = _generation;
                CancellationTokenSource cancellation = new CancellationTokenSource();
                CancellationToken runToken = cancellation.Token;

                runTask = Task.Run(async () =>
                {
                    await launch.Task.ConfigureAwait(false);
                    await (_runOverride != null
                        ? _runOverride(runToken)
                        : RunAsync(runToken)).ConfigureAwait(false);
                });
                _generation = new RunGeneration(cancellation, runTask);
            }

            ReleaseGenerationAfterLifecycle(completedGeneration);
            try { _pauseController.Resume(); }
            catch (Exception ex)
            {
                TryLogError("Queue.Callback", "A pause-state subscriber failed while starting upload.", ex);
            }
            finally { launch.TrySetResult(); }
            RaiseRunStateChanged();
            return runTask;
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
            StopRequest request = CreateStopRequest(preserveParts);
            if (request != null) ScheduleSignalStop(request);
        }

        /// <summary>
        /// Stops the current generation and waits for its complete cleanup lifecycle. A reentrant
        /// call from that generation's terminal <see cref="RunStateChanged"/> notification is an
        /// idempotent completed no-op, so a subscriber never waits on its own callback.
        /// </summary>
        public Task StopAsync(bool preserveParts, CancellationToken cancellationToken)
        {
            StopRequest request = CreateStopRequest(preserveParts);
            if (request == null) return Task.CompletedTask;
            ScheduleSignalStop(request);
            return request.Generation.LifecycleTask.WaitAsync(cancellationToken);
        }

        private StopRequest CreateStopRequest(bool preserveParts)
        {
            lock (_sync)
            {
                if (IsTerminalNotificationForCurrentGenerationLocked()) return null;

                // Disposal is terminal. While its active lifecycle is still open, callers may
                // join that exact stop; after it completes, cancellation APIs are safe no-ops.
                if (_disposed)
                {
                    if (_generation == null || _generation.LifecycleTask.IsCompleted)
                        return null;
                    return new StopRequest(
                        _generation,
                        false,
                        null,
                        _generation.PreserveParts);
                }

                return CreateStopRequestLocked(preserveParts);
            }
        }

        private StopRequest CreateStopRequestLocked(bool preserveParts)
        {
            RunGeneration generation = _generation;
            if (generation == null)
            {
                generation = RunGeneration.CreateSynthetic();
                _generation = generation;
            }

            if (generation.StopRequested)
                return new StopRequest(generation, false, null, generation.PreserveParts);

            generation.LatchStop(preserveParts, () => RaiseRunStateChanged(generation));
            List<UploadQueueItem> queuedItems = new List<UploadQueueItem>();
            foreach (UploadQueueItem item in _items)
            {
                item.PreservePartsOnCancel = preserveParts;
                if (item.Status == UploadItemStatus.Queued) queuedItems.Add(item);
            }

            return new StopRequest(generation, true, queuedItems, preserveParts);
        }

        private void ScheduleSignalStop(StopRequest request)
        {
            if (!request.NewlyLatched) return;
            try
            {
                _ = Task.Factory.StartNew(
                    () => SignalStop(request),
                    CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                TryLogError("Queue.Cancel", "Upload cancellation signaling could not be scheduled.", ex);
                request.Generation.CompleteStopSignal();
            }
        }

        // Queue notifications have historically been raised from queue worker threads. Stop
        // signaling follows that same contract so UI callers can apply their own timeout while
        // cancellation callbacks and status subscribers finish.
        private void SignalStop(StopRequest request)
        {
            try
            {
                try { _pauseController.Resume(); }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    TryLogError("Queue.Callback", "A pause-state subscriber failed while stopping upload.", ex);
                }

                if (request.Generation.Cancellation != null)
                {
                    try { request.Generation.Cancellation.Cancel(); }
                    catch (ObjectDisposedException) { }
                    catch (Exception ex)
                    {
                        TryLogError("Queue.Cancel", "Upload cancellation callbacks failed.", ex);
                    }
                }

                if (request.QueuedItems != null)
                {
                    foreach (UploadQueueItem item in request.QueuedItems)
                    {
                        try { item.SetStatus(UploadItemStatus.Cancelled); }
                        catch (Exception ex)
                        {
                            TryLogError("Queue.Callback", "A queue-item subscriber failed while stopping upload.", ex);
                        }
                    }
                }

                try
                {
                    if (_log != null)
                        _log.Info("Queue.Cancel", "Upload cancelled (preserveParts=" + request.PreserveParts + ").");
                }
                catch (Exception ex) { TryLogError("Queue.Callback", "Upload cancellation logging failed.", ex); }
                RaiseRunStateChanged();
            }
            catch (Exception ex)
            {
                TryLogError("Queue.Cancel", "Upload cancellation signaling failed unexpectedly.", ex);
            }
            finally
            {
                request.Generation.CompleteStopSignal();
            }
        }

        public void CancelItem(UploadQueueItem item)
        {
            _cancelItem(item);
        }

        private static void CancelItemCore(UploadQueueItem item)
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

        private bool CanRestartFromTerminalNotificationLocked(RunGeneration generation)
        {
            RunStateNotificationContext notification = s_runStateNotification.Value;
            return notification != null &&
                ReferenceEquals(notification.Queue, this) &&
                ReferenceEquals(notification.TerminalGeneration, generation) &&
                generation.StopRequested &&
                !generation.IsRunning;
        }

        private bool IsTerminalNotificationForCurrentGenerationLocked()
        {
            return _generation != null && CanRestartFromTerminalNotificationLocked(_generation);
        }

        private void RaiseRunStateChanged()
        {
            RaiseRunStateChanged(null);
        }

        private void RaiseRunStateChanged(RunGeneration terminalGeneration)
        {
            EventHandler handler = RunStateChanged;
            if (handler == null) return;

            RunStateNotificationContext previous = s_runStateNotification.Value;
            s_runStateNotification.Value = terminalGeneration == null
                ? null
                : new RunStateNotificationContext(this, terminalGeneration);
            try
            {
                foreach (EventHandler callback in handler.GetInvocationList())
                {
                    try { callback(this, EventArgs.Empty); }
                    catch (Exception ex)
                    {
                        TryLogError("Queue.Callback", "A run-state subscriber failed.", ex);
                    }
                }
            }
            finally
            {
                s_runStateNotification.Value = previous;
            }
        }

        public void Dispose()
        {
            StopRequest stopRequest = null;
            RunGeneration generation = null;
            Task duplicateDisposal = null;
            lock (_sync)
            {
                if (_disposed)
                {
                    // A logging callback may reenter Dispose on the synchronous owner thread.
                    // Other duplicate idle/completed callers join its inline cleanup.
                    if (_disposalIsSynchronous &&
                        _synchronousDisposeOwnerThreadId != Environment.CurrentManagedThreadId)
                    {
                        duplicateDisposal = _disposalCompleted.Task;
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    _disposed = true;
                    generation = _generation;
                    _disposalIsSynchronous =
                        generation == null || generation.LifecycleTask.IsCompleted;
                    if (_disposalIsSynchronous)
                        _synchronousDisposeOwnerThreadId = Environment.CurrentManagedThreadId;

                    // Only an open lifecycle needs cancellation. Idle queues and completed runs
                    // release synchronously without manufacturing another stop generation.
                    if (!_disposalIsSynchronous)
                        stopRequest = CreateStopRequestLocked(true);
                }
            }

            if (duplicateDisposal != null)
            {
                duplicateDisposal.GetAwaiter().GetResult();
                return;
            }

            if (stopRequest != null) ScheduleSignalStop(stopRequest);

            if (generation != null && !generation.LifecycleTask.IsCompleted)
            {
                _ = generation.LifecycleTask.ContinueWith(
                    _ => CompleteDisposal(generation),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                return;
            }

            CompleteDisposal(generation);
        }

        private void ReleaseGenerationAfterLifecycle(RunGeneration generation)
        {
            if (generation == null) return;
            if (generation.LifecycleTask.IsCompleted)
            {
                ReleaseCompletedGeneration(generation);
                return;
            }

            _ = generation.LifecycleTask.ContinueWith(
                _ => ReleaseCompletedGeneration(generation),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void ReleaseCompletedGeneration(RunGeneration generation)
        {
            if (generation == null) return;

            ObserveGenerationFault(generation, "Queue.Run", "The previous upload run faulted before restart.");
            try { generation.DisposeCancellation(); }
            catch (Exception ex)
            {
                TryLogError("Queue.Run", "The previous upload cancellation source could not be disposed.", ex);
            }
        }

        private void CompleteDisposal(RunGeneration generation)
        {
            try
            {
                if (Interlocked.Exchange(ref _lifecycleResourcesDisposed, 1) != 0) return;

                ObserveGenerationFault(
                    generation,
                    "Queue.Dispose",
                    "The upload run faulted during shutdown cleanup.");

                try
                {
                    if (generation != null) generation.DisposeCancellation();
                }
                catch (Exception ex)
                {
                    TryLogError("Queue.Dispose", "The upload cancellation source could not be disposed.", ex);
                }

                try { _pauseController.Dispose(); }
                catch (Exception ex)
                {
                    TryLogError("Queue.Dispose", "The upload pause controller could not be disposed.", ex);
                }
            }
            catch (Exception ex)
            {
                TryLogError("Queue.Dispose", "Upload shutdown cleanup failed unexpectedly.", ex);
            }
            finally
            {
                _disposalCompleted.TrySetResult();
            }
        }

        private void ObserveRunFault(Task runTask, string operation, string message)
        {
            if (runTask == null || !runTask.IsFaulted) return;

            Exception error = runTask.Exception;
            if (error is AggregateException aggregate && aggregate.InnerExceptions.Count == 1)
                error = aggregate.InnerExceptions[0];
            TryLogError(operation, message, error);
        }

        private void ObserveGenerationFault(RunGeneration generation, string operation, string message)
        {
            if (generation == null) return;

            // Inspect the composite explicitly, then report the exact runner fault once.
            if (generation.LifecycleTask.IsFaulted)
                _ = generation.LifecycleTask.Exception;
            ObserveRunFault(generation.RunTask, operation, message);
        }

        private void TryLogError(string operation, string message, Exception exception)
        {
            try
            {
                if (_log != null) _log.Error(operation, message, exception);
            }
            catch (Exception)
            {
                // Logging must never fault a shutdown continuation and become unobserved itself.
            }
        }
    }
}
