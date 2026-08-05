using System;

namespace CloudflareR2Uploader.Models
{
    /// <summary>Immutable snapshot of the whole queue, shown in the status bar.</summary>
    public sealed class OverallProgressInfo
    {
        public OverallProgressInfo(
            long transferredBytes,
            long totalBytes,
            double bytesPerSecond,
            TimeSpan elapsed,
            int succeeded,
            int failed,
            int queued,
            int active,
            int skipped,
            int cancelled)
        {
            TransferredBytes = transferredBytes;
            TotalBytes = totalBytes;
            BytesPerSecond = bytesPerSecond;
            Elapsed = elapsed;
            Succeeded = succeeded;
            Failed = failed;
            Queued = queued;
            Active = active;
            Skipped = skipped;
            Cancelled = cancelled;
        }

        public long TransferredBytes { get; private set; }
        public long TotalBytes { get; private set; }
        public double BytesPerSecond { get; private set; }
        public TimeSpan Elapsed { get; private set; }
        public int Succeeded { get; private set; }
        public int Failed { get; private set; }
        public int Queued { get; private set; }
        public int Active { get; private set; }
        public int Skipped { get; private set; }
        public int Cancelled { get; private set; }

        public long RemainingBytes
        {
            get
            {
                long remaining = TotalBytes - TransferredBytes;
                return remaining < 0 ? 0 : remaining;
            }
        }

        public double Fraction
        {
            get
            {
                if (TotalBytes <= 0) return 0;
                double value = (double)TransferredBytes / TotalBytes;
                if (value < 0) return 0;
                return value > 1.0 ? 1.0 : value;
            }
        }

        public int Percent { get { return (int)Math.Round(Fraction * 100.0); } }

        public static OverallProgressInfo Empty
        {
            get { return new OverallProgressInfo(0, 0, 0, TimeSpan.Zero, 0, 0, 0, 0, 0, 0); }
        }
    }
}
