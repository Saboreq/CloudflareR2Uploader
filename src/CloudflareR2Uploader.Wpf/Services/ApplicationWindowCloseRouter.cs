using System.Threading;

namespace CloudflareR2Uploader.Wpf.Services
{
    internal enum WindowCloseDisposition
    {
        Hide,
        RequestExit,
        AllowClose
    }

    internal sealed class ApplicationWindowCloseRouter
    {
        private int _exitCommitted;

        public bool ShouldShutdownAfterClosed => Volatile.Read(ref _exitCommitted) != 0;

        public WindowCloseDisposition Evaluate(bool closeToTray, bool uploadRunning)
        {
            if (ShouldShutdownAfterClosed) return WindowCloseDisposition.AllowClose;
            return closeToTray || uploadRunning
                ? WindowCloseDisposition.Hide
                : WindowCloseDisposition.RequestExit;
        }

        public void MarkExitCommitted() => Interlocked.Exchange(ref _exitCommitted, 1);
    }
}
