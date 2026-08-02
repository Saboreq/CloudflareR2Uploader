using System;
using System.Diagnostics;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>
    /// Smoothed transfer-rate estimate. Uses an exponential moving average over real elapsed
    /// time so a burst or a stall does not make the displayed speed jump around.
    /// Thread-safe: progress arrives from several part-upload tasks at once.
    /// </summary>
    public sealed class SpeedEstimator
    {
        private const double SmoothingFactor = 0.25;
        private static readonly TimeSpan MinSampleInterval = TimeSpan.FromMilliseconds(400);

        private readonly object _sync = new object();
        private readonly Stopwatch _clock = new Stopwatch();

        private long _lastSampleBytes;
        private TimeSpan _lastSampleTime;
        private double _bytesPerSecond;
        private bool _hasSample;

        public void Start(long alreadyTransferredBytes)
        {
            lock (_sync)
            {
                _clock.Restart();
                _lastSampleBytes = alreadyTransferredBytes;
                _lastSampleTime = TimeSpan.Zero;
                _bytesPerSecond = 0;
                _hasSample = false;
            }
        }

        public void Stop()
        {
            lock (_sync) { _clock.Stop(); }
        }

        public TimeSpan Elapsed
        {
            get { lock (_sync) { return _clock.Elapsed; } }
        }

        /// <summary>Current smoothed rate in bytes per second; 0 until enough data exists.</summary>
        public double BytesPerSecond
        {
            get { lock (_sync) { return _bytesPerSecond; } }
        }

        /// <summary>
        /// Feeds the latest cumulative byte count. Samples closer together than
        /// <see cref="MinSampleInterval"/> are ignored to keep the estimate stable.
        /// </summary>
        public void Report(long totalTransferredBytes)
        {
            lock (_sync)
            {
                if (!_clock.IsRunning) return;

                TimeSpan now = _clock.Elapsed;
                TimeSpan delta = now - _lastSampleTime;
                if (delta < MinSampleInterval) return;

                long deltaBytes = totalTransferredBytes - _lastSampleBytes;
                if (deltaBytes < 0) deltaBytes = 0;

                double instant = deltaBytes / delta.TotalSeconds;
                _bytesPerSecond = _hasSample
                    ? (_bytesPerSecond * (1 - SmoothingFactor)) + (instant * SmoothingFactor)
                    : instant;

                _hasSample = true;
                _lastSampleBytes = totalTransferredBytes;
                _lastSampleTime = now;
            }
        }

        /// <summary>Zeroes the rate without losing elapsed time, e.g. while paused.</summary>
        public void MarkIdle()
        {
            lock (_sync)
            {
                _bytesPerSecond = 0;
                _hasSample = false;
                _lastSampleTime = _clock.Elapsed;
            }
        }
    }
}
