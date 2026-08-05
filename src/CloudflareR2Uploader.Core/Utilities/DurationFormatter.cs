using System;
using System.Globalization;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>Formats elapsed times and remaining-time estimates.</summary>
    public static class DurationFormatter
    {
        public const string Unknown = "—";

        /// <summary>Formats a span as <c>h:mm:ss</c> or <c>m:ss</c>.</summary>
        public static string Format(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            if (span.TotalHours >= 1)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0}:{1:00}:{2:00}",
                    (int)span.TotalHours, span.Minutes, span.Seconds);
            }
            return string.Format(CultureInfo.CurrentCulture, "{0}:{1:00}", span.Minutes, span.Seconds);
        }

        /// <summary>
        /// Estimates remaining time from the current rate. Returns an em dash while the rate
        /// is not yet meaningful rather than showing a wildly wrong number.
        /// </summary>
        public static string FormatEta(long remainingBytes, double bytesPerSecond)
        {
            if (remainingBytes <= 0) return "0:00";
            if (double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond) || bytesPerSecond < 1d)
                return Unknown;

            double seconds = remainingBytes / bytesPerSecond;
            if (seconds > TimeSpan.MaxValue.TotalSeconds - 1 || seconds > 359999d) return "> 99:59:59";
            return Format(TimeSpan.FromSeconds(seconds));
        }
    }
}
