#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Renders activity records as RFC 4180 CSV.
    /// <para>
    /// Values are already sanitised by <see cref="ActivitySanitizer"/> before they are
    /// stored, so no secret can reach this class. Quoting is still done strictly: any value
    /// containing a comma, quote, newline or leading whitespace is quoted and embedded
    /// quotes are doubled, and a value starting with a formula character is prefixed so a
    /// spreadsheet cannot execute it.
    /// </para>
    /// </summary>
    public static class ActivityCsvExporter
    {
        private static readonly string[] Header =
        {
            "Timestamp (UTC)",
            "Action",
            "Result",
            "Object",
            "Destination",
            "Bucket",
            "Size (bytes)",
            "Duration (ms)",
            "Error code",
            "Error message"
        };

        public static string Export(IEnumerable<ActivityRecord> records)
        {
            ArgumentNullException.ThrowIfNull(records);

            StringBuilder builder = new StringBuilder();
            AppendRow(builder, Header);

            foreach (ActivityRecord record in records)
            {
                AppendRow(builder, new[]
                {
                    ActivityRecord.FormatUtc(record.TimestampUtc),
                    record.Action.ToString(),
                    record.Result.ToString(),
                    record.Target ?? string.Empty,
                    record.SecondaryTarget ?? string.Empty,
                    record.BucketName ?? string.Empty,
                    record.SizeBytes.HasValue ? record.SizeBytes.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
                    record.DurationMilliseconds.HasValue
                        ? record.DurationMilliseconds.Value.ToString(CultureInfo.InvariantCulture)
                        : string.Empty,
                    record.ErrorCode ?? string.Empty,
                    record.ErrorMessage ?? string.Empty
                });
            }

            return builder.ToString();
        }

        private static void AppendRow(StringBuilder builder, string[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (index > 0) builder.Append(',');
                builder.Append(EscapeField(values[index]));
            }
            builder.Append("\r\n");
        }

        internal static string EscapeField(string? value)
        {
            string text = value ?? string.Empty;
            if (text.Length == 0) return string.Empty;

            // An object key legitimately can start with '=', '+', '-' or '@'. Excel and
            // similar tools would evaluate that as a formula, so it is neutralised with a
            // leading apostrophe, which the same tools strip on display.
            if (text[0] == '=' || text[0] == '+' || text[0] == '-' || text[0] == '@') text = "'" + text;

            bool mustQuote =
                text.Contains(',') ||
                text.Contains('"') ||
                text.Contains('\n') ||
                text.Contains('\r') ||
                text[0] == ' ' ||
                text[text.Length - 1] == ' ';

            if (!mustQuote) return text;

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
