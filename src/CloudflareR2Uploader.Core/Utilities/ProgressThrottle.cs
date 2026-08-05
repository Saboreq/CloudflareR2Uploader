using System;
using System.Diagnostics;
using System.Threading;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>
    /// Rate-limits progress notifications. Part uploads can raise thousands of callbacks per
    /// second; forwarding all of them would flood the UI message queue. Lock-free so it adds
    /// no contention to the transfer path.
    /// </summary>
    public sealed class ProgressThrottle
    {
        private readonly long _intervalTicks;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastTicks;

        public ProgressThrottle(TimeSpan interval)
        {
            _intervalTicks = (long)(interval.TotalSeconds * Stopwatch.Frequency);
            _lastTicks = -_intervalTicks;
        }

        public ProgressThrottle() : this(TimeSpan.FromMilliseconds(120)) { }

        /// <summary>
        /// True at most once per interval. <paramref name="force"/> bypasses the limit for
        /// terminal updates that must never be dropped.
        /// </summary>
        public bool ShouldReport(bool force = false)
        {
            long now = _clock.ElapsedTicks;

            if (force)
            {
                Interlocked.Exchange(ref _lastTicks, now);
                return true;
            }

            long last = Interlocked.Read(ref _lastTicks);
            if (now - last < _intervalTicks) return false;

            // Only the thread that wins the exchange reports, so bursts collapse to one call.
            return Interlocked.CompareExchange(ref _lastTicks, now, last) == last;
        }
    }
}
