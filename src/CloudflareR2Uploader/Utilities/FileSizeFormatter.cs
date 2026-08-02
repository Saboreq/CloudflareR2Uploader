using System;
using System.Globalization;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>Formats byte counts and transfer rates for display.</summary>
    public static class FileSizeFormatter
    {
        private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

        /// <summary>
        /// Formats a byte count using binary (1024-based) units, e.g. <c>1.50 GB</c>.
        /// Negative values are clamped to zero.
        /// </summary>
        public static string Format(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes < 1024) return bytes.ToString(CultureInfo.CurrentCulture) + " B";

            double value = bytes;
            int unit = 0;
            while (value >= 1024d && unit < Units.Length - 1)
            {
                value /= 1024d;
                unit++;
            }

            // Keep three significant digits so widths stay stable in the queue list.
            string format = value >= 100d ? "0" : (value >= 10d ? "0.0" : "0.00");
            return value.ToString(format, CultureInfo.CurrentCulture) + " " + Units[unit];
        }

        /// <summary>Formats a transfer rate, e.g. <c>12.4 MB/s</c>. Returns an em dash when unknown.</summary>
        public static string FormatSpeed(double bytesPerSecond)
        {
            if (double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond) || bytesPerSecond <= 0.5)
                return "—";
            return Format((long)Math.Round(bytesPerSecond)) + "/s";
        }

        /// <summary>Formats "transferred of total", e.g. <c>120 MB / 1.50 GB</c>.</summary>
        public static string FormatProgress(long transferred, long total)
        {
            return Format(transferred) + " / " + Format(total);
        }
    }
}
