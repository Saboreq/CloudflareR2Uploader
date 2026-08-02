using System;

namespace CloudflareR2Uploader.Models
{
    /// <summary>
    /// An immutable progress snapshot for one file. Passed through <see cref="IProgress{T}"/>
    /// so the value the UI reads can never be mutated underneath it by a worker thread.
    /// </summary>
    public sealed class UploadProgressInfo
    {
        public UploadProgressInfo(
            long transferredBytes,
            long totalBytes,
            double bytesPerSecond,
            int completedParts,
            int totalParts)
        {
            TransferredBytes = transferredBytes < 0 ? 0 : transferredBytes;
            TotalBytes = totalBytes < 0 ? 0 : totalBytes;
            BytesPerSecond = bytesPerSecond < 0 ? 0 : bytesPerSecond;
            CompletedParts = completedParts;
            TotalParts = totalParts;
        }

        public long TransferredBytes { get; private set; }
        public long TotalBytes { get; private set; }
        public double BytesPerSecond { get; private set; }

        /// <summary>0 for a single-request upload.</summary>
        public int CompletedParts { get; private set; }

        /// <summary>0 for a single-request upload.</summary>
        public int TotalParts { get; private set; }

        public bool IsMultipart { get { return TotalParts > 0; } }

        public long RemainingBytes
        {
            get
            {
                long remaining = TotalBytes - TransferredBytes;
                return remaining < 0 ? 0 : remaining;
            }
        }

        /// <summary>Fraction in the range 0..1. A zero-byte file counts as complete.</summary>
        public double Fraction
        {
            get
            {
                if (TotalBytes <= 0) return 1.0;
                double value = (double)TransferredBytes / TotalBytes;
                if (value < 0) return 0;
                return value > 1.0 ? 1.0 : value;
            }
        }

        public int Percent { get { return (int)Math.Round(Fraction * 100.0); } }
    }
}
