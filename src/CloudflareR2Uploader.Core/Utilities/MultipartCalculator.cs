using System;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>
    /// Works out multipart geometry. S3/R2 allow at most 10,000 parts and require every part
    /// except the last to be identically sized and at least 5 MiB.
    /// </summary>
    public static class MultipartCalculator
    {
        public const long MiB = 1024L * 1024L;
        public const long GiB = 1024L * MiB;

        /// <summary>Smallest part size S3/R2 accept for any part except the final one.</summary>
        public const long MinPartSize = 5 * MiB;

        /// <summary>Largest part size S3/R2 accept.</summary>
        public const long MaxPartSize = 5 * GiB;

        /// <summary>Hard protocol limit on the number of parts in one multipart upload.</summary>
        public const int MaxParts = 10000;

        /// <summary>
        /// Divisor used when growing the part size. Deliberately below <see cref="MaxParts"/>
        /// so rounding up to a whole MiB can never push the count over the limit.
        /// </summary>
        public const int PartCountBudget = 9990;

        public const long DefaultPartSize = 64 * MiB;
        public const long DefaultMultipartThreshold = 100 * MiB;

        /// <summary>
        /// Chooses the part size for a file: the configured size, grown when necessary to keep
        /// the part count below the budget, rounded up to a whole MiB and clamped to the
        /// supported range.
        /// </summary>
        public static long CalculatePartSize(long fileSize, long configuredPartSize)
        {
            if (fileSize < 0) throw new ArgumentOutOfRangeException("fileSize");

            long partSize = configuredPartSize < MinPartSize ? MinPartSize : configuredPartSize;
            if (partSize > MaxPartSize) partSize = MaxPartSize;

            // partSize = max(configured, ceil(fileSize / budget))
            long required = CeilingDivide(fileSize, PartCountBudget);
            if (required > partSize) partSize = required;

            partSize = RoundUpToWholeMiB(partSize);

            if (partSize < MinPartSize) partSize = MinPartSize;
            if (partSize > MaxPartSize) partSize = MaxPartSize;

            // A file larger than 10,000 * 5 GiB (~48.8 PiB) cannot be uploaded at all; that is
            // a protocol limit, not something rounding can fix.
            if (CalculatePartCount(fileSize, partSize) > MaxParts)
            {
                throw new InvalidOperationException(
                    "The file is too large for a single S3 multipart upload (it would need more than 10,000 parts of the maximum 5 GiB size).");
            }

            return partSize;
        }

        /// <summary>Number of parts a file of the given size needs. A zero-byte file has one part.</summary>
        public static int CalculatePartCount(long fileSize, long partSize)
        {
            if (partSize <= 0) throw new ArgumentOutOfRangeException("partSize");
            if (fileSize <= 0) return 1;

            long count = CeilingDivide(fileSize, partSize);
            return count > int.MaxValue ? int.MaxValue : (int)count;
        }

        /// <summary>Byte offset at which a 1-based part number begins.</summary>
        public static long GetPartOffset(int partNumber, long partSize)
        {
            if (partNumber < 1) throw new ArgumentOutOfRangeException("partNumber");
            return (partNumber - 1) * partSize;
        }

        /// <summary>Length of a 1-based part; the final part is whatever remains.</summary>
        public static long GetPartLength(int partNumber, long partSize, long fileSize)
        {
            long offset = GetPartOffset(partNumber, partSize);
            if (offset >= fileSize) return 0;

            long remaining = fileSize - offset;
            return remaining < partSize ? remaining : partSize;
        }

        /// <summary>Rounds a byte count up to the next whole MiB.</summary>
        public static long RoundUpToWholeMiB(long bytes)
        {
            if (bytes <= 0) return MiB;
            long wholeMiB = CeilingDivide(bytes, MiB);
            return wholeMiB * MiB;
        }

        private static long CeilingDivide(long value, long divisor)
        {
            if (value <= 0) return 0;
            return (value + divisor - 1) / divisor;
        }
    }
}
