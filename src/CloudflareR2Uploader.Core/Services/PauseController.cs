using System;
using System.Threading;
using System.Threading.Tasks;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// A real pause gate. When paused, the uploader stops scheduling new parts and new files;
    /// parts already in flight run to completion so their ETags can be recorded and resumed
    /// from. Nothing here fakes a pause by discarding work.
    /// </summary>
    public sealed class PauseController : IDisposable
    {
        private readonly ManualResetEventSlim _gate = new ManualResetEventSlim(true);
        private int _paused;

        public bool IsPaused { get { return Volatile.Read(ref _paused) != 0; } }

        public event EventHandler PauseStateChanged;

        public void Pause()
        {
            if (Interlocked.Exchange(ref _paused, 1) == 1) return;
            _gate.Reset();
            RaiseChanged();
        }

        public void Resume()
        {
            if (Interlocked.Exchange(ref _paused, 0) == 0) return;
            _gate.Set();
            RaiseChanged();
        }

        /// <summary>
        /// Returns immediately when running; otherwise waits until <see cref="Resume"/> is
        /// called or the token is cancelled. Polls on a short interval rather than blocking a
        /// thread pool thread on a wait handle.
        /// </summary>
        public async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
        {
            while (IsPaused)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void RaiseChanged()
        {
            EventHandler handler = PauseStateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            _gate.Dispose();
        }
    }
}
