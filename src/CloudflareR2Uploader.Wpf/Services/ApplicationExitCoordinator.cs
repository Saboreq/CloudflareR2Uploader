using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CloudflareR2Uploader.Services;

namespace CloudflareR2Uploader.Wpf.Services
{
    public interface IApplicationExitCoordinator
    {
        event EventHandler? ExitCommitted;

        Task<bool> PrepareAsync();

        void Commit();

        void ReleasePreparation();
    }

    public interface IApplicationController
    {
        void Shutdown();
    }

    /// <summary>
    /// Coordinates every real process exit so tray and updater paths preserve or discard
    /// resumable multipart state using the same decision.
    /// </summary>
    public sealed class ApplicationExitCoordinator : IApplicationExitCoordinator
    {
        private readonly IUploadQueueLifecycle _queue;
        private readonly IDialogService _dialogs;
        private readonly ILoggingService _log;
        private readonly TimeSpan _stopTimeout;
        private readonly object _sync = new();
        private Task<bool>? _preparation;
        private bool _authorizationOutstanding;
        private bool _committed;

        public ApplicationExitCoordinator(
            IUploadQueueLifecycle queue,
            IDialogService dialogs,
            ILoggingService log)
            : this(queue, dialogs, log, TimeSpan.FromSeconds(45))
        {
        }

        internal ApplicationExitCoordinator(
            IUploadQueueLifecycle queue,
            IDialogService dialogs,
            ILoggingService log,
            TimeSpan stopTimeout)
        {
            _queue = queue;
            _dialogs = dialogs;
            _log = log;
            _stopTimeout = stopTimeout;
        }

        public event EventHandler? ExitCommitted;

        public async Task<bool> PrepareAsync()
        {
            TaskCompletionSource<bool>? owner = null;
            Task<bool> shared;
            lock (_sync)
            {
                if (_committed || _authorizationOutstanding) return false;

                if (_preparation is not null)
                {
                    shared = _preparation;
                }
                else
                {
                    owner = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    shared = owner.Task;
                    _preparation = shared;
                }
            }

            if (owner is null)
            {
                await shared.ConfigureAwait(true);
                return false;
            }

            bool prepared = false;
            try
            {
                prepared = await PrepareCoreAsync().ConfigureAwait(true);
                owner.TrySetResult(prepared);
                return prepared;
            }
            catch (Exception)
            {
                owner.TrySetResult(false);
                throw;
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_preparation, shared)) _preparation = null;
                    if (prepared && !_committed) _authorizationOutstanding = true;
                }
            }
        }

        private async Task<bool> PrepareCoreAsync()
        {
            if (!_queue.IsRunning) return true;

            bool? keepParts = await _dialogs
                .AskKeepPartsOnCancelAsync(CountActiveMultipartUploads())
                .ConfigureAwait(true);
            if (!keepParts.HasValue) return false;

            using CancellationTokenSource timeout = new(_stopTimeout);
            Exception? stopFailure = null;
            try
            {
                await _queue.StopAsync(keepParts.Value, timeout.Token).ConfigureAwait(true);
            }
            catch (Exception ex) when (IsNonFatalStopFailure(ex))
            {
                stopFailure = ex;
            }

            if (stopFailure is null) return true;

            _log.Error("App.Exit", "Upload cleanup did not complete before exit.", stopFailure);
            await _dialogs.ShowErrorAsync(
                "The app is still finishing an upload",
                "Cloudflare R2 Uploader stayed open so resumable upload state is not lost.")
                .ConfigureAwait(true);
            return false;
        }

        private static bool IsNonFatalStopFailure(Exception exception) =>
            exception is not OutOfMemoryException and
            not StackOverflowException and
            not AccessViolationException;

        public void Commit()
        {
            bool raiseCommitted;
            lock (_sync)
            {
                raiseCommitted = !_committed && _authorizationOutstanding;
                if (!raiseCommitted) return;

                _authorizationOutstanding = false;
                _committed = true;
            }

            EventHandler? handler = ExitCommitted;
            if (handler is null) return;
            foreach (EventHandler callback in handler.GetInvocationList())
            {
                try { callback(this, EventArgs.Empty); }
                catch (Exception ex)
                {
                    try { _log.Error("App.Exit", "An exit-commit subscriber failed.", ex); }
                    catch (Exception) { }
                }
            }
        }

        public void ReleasePreparation()
        {
            lock (_sync)
            {
                if (!_committed) _authorizationOutstanding = false;
            }
        }

        private int CountActiveMultipartUploads()
        {
            int count = 0;
            foreach (Models.UploadQueueItem item in _queue.GetItems())
            {
                if (item.TotalParts > 1 && item.Status is Models.UploadItemStatus.Uploading or Models.UploadItemStatus.Paused)
                    count++;
            }

            return Math.Max(1, count);
        }
    }

    public sealed class WpfApplicationController : IApplicationController
    {
        public void Shutdown() => Application.Current.Shutdown();
    }
}
