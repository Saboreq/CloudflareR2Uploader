using System;
using System.Windows.Forms;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>
    /// Marshals work onto the UI thread. Every call is non-blocking (<see cref="Control.BeginInvoke"/>),
    /// so no background upload task can ever stall waiting for the UI, and no UI callback can
    /// deadlock a worker.
    /// </summary>
    public static class UiThreadUtility
    {
        /// <summary>
        /// Runs <paramref name="action"/> on the thread that owns <paramref name="control"/>.
        /// Does nothing if the control is gone or is being torn down.
        /// </summary>
        public static void Post(Control control, Action action)
        {
            if (control == null || action == null) return;

            try
            {
                if (control.IsDisposed || control.Disposing || !control.IsHandleCreated) return;

                if (control.InvokeRequired)
                {
                    control.BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (ObjectDisposedException)
            {
                // The control was disposed between the check and the call; the update is moot.
            }
            catch (InvalidOperationException)
            {
                // The handle was destroyed while marshalling; the update is moot.
            }
        }
    }
}
