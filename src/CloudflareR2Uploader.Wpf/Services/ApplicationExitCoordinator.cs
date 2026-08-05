using System;
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
        private readonly UploadQueueService _queue;
        private readonly IDialogService _dialogs;

        public ApplicationExitCoordinator(UploadQueueService queue, IDialogService dialogs)
        {
            _queue = queue;
            _dialogs = dialogs;
        }

        public event EventHandler? ExitCommitted;

        public async Task<bool> PrepareAsync()
        {
            if (!_queue.IsRunning) return true;

            bool? keepParts = await _dialogs
                .AskKeepPartsOnCancelAsync(CountActiveMultipartUploads())
                .ConfigureAwait(true);
            if (!keepParts.HasValue) return false;

            _queue.CancelAll(keepParts.Value);
            return true;
        }

        public void Commit() => ExitCommitted?.Invoke(this, EventArgs.Empty);

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
