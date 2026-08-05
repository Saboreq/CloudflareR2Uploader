#nullable enable

using System;
using System.Text;
using System.Text.RegularExpressions;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Strips anything that must never reach the activity file or a CSV export.
    /// <para>
    /// The activity history is user-visible, exportable and long-lived, so it is treated as
    /// a publication surface: presigned URLs lose their query string entirely, credential
    /// shaped text is redacted through the same filter the log file uses, control characters
    /// are removed so a record cannot forge extra CSV or JSON lines, and every field has a
    /// hard length bound.
    /// </para>
    /// </summary>
    public static class ActivitySanitizer
    {
        public const int MaxTargetLength = 512;
        public const int MaxMessageLength = 400;
        public const int MaxCodeLength = 64;

        /// <summary>
        /// SigV4 puts the access key id in a bare <c>Credential=</c> token inside the
        /// Authorization header value. <see cref="LoggingService.Sanitize"/> only truncates
        /// that header at the first space, so the token is removed explicitly here. The
        /// activity file is exportable, so it is held to a stricter standard than the log.
        /// </summary>
        private static readonly Regex CredentialTokenPattern = new Regex(
            @"(?i)\bcredential\s*=\s*[^\s,;&""']+", RegexOptions.Compiled);

        /// <summary>Removes an <c>AWS4-HMAC-SHA256 …</c> remnant left behind by the above.</summary>
        private static readonly Regex SigningAlgorithmPattern = new Regex(
            @"(?i)\bAWS4-HMAC-SHA256\b", RegexOptions.Compiled);

        /// <summary>
        /// Returns a copy of <paramref name="record"/> that is safe to persist. The original
        /// is not modified, so a caller cannot accidentally keep a reference to raw text.
        /// </summary>
        public static ActivityRecord Sanitize(ActivityRecord record)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));

            return new ActivityRecord
            {
                Id = string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString("N") : Clean(record.Id, 64),
                TimestampUtcText = ActivityRecord.FormatUtc(
                    record.TimestampUtc == DateTime.MinValue ? DateTime.UtcNow : record.TimestampUtc),
                ActionValue = (int)record.Action,
                ResultValue = (int)record.Result,
                Target = CleanTarget(record.Target),
                SecondaryTarget = CleanTarget(record.SecondaryTarget),
                BucketName = Clean(record.BucketName, 128),
                ProfileId = Clean(record.ProfileId, 64),
                SizeBytes = record.SizeBytes.HasValue && record.SizeBytes.Value >= 0 ? record.SizeBytes : null,
                DurationMilliseconds =
                    record.DurationMilliseconds.HasValue && record.DurationMilliseconds.Value >= 0
                        ? record.DurationMilliseconds
                        : null,
                ErrorCode = Clean(record.ErrorCode, MaxCodeLength),
                ErrorMessage = Clean(record.ErrorMessage, MaxMessageLength),
                CorrelationId = Clean(record.CorrelationId, 64)
            };
        }

        /// <summary>
        /// Object keys are shown verbatim, but a value that is actually a URL must lose its
        /// query string: a presigned link's signature would otherwise be persisted.
        /// <para>
        /// The query string is removed <em>before</em> the credential filter runs. Otherwise
        /// the filter would recognise the whole thing as a presigned URL and replace it with
        /// a placeholder, and the Activity screen would lose the object path it exists to
        /// show. Trimming first keeps the useful half and discards the sensitive half.
        /// </para>
        /// </summary>
        public static string CleanTarget(string? value)
        {
            string text = value ?? string.Empty;

            if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                int query = text.IndexOf('?');
                if (query >= 0) text = text.Substring(0, query);

                int fragment = text.IndexOf('#');
                if (fragment >= 0) text = text.Substring(0, fragment);
            }

            return Clean(text, MaxTargetLength);
        }

        /// <summary>
        /// Applies the shared credential redaction, removes control characters, collapses
        /// whitespace and truncates. Never returns null.
        /// </summary>
        public static string Clean(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            string redacted = LoggingService.Sanitize(value);
            redacted = CredentialTokenPattern.Replace(redacted, "Credential=<redacted>");
            redacted = SigningAlgorithmPattern.Replace(redacted, "<redacted>");

            StringBuilder builder = new StringBuilder(Math.Min(redacted.Length, maxLength) + 1);
            bool lastWasSpace = false;
            foreach (char character in redacted)
            {
                // Control characters would let a record inject a line break into the JSON
                // Lines file or a CSV export.
                char normalized = char.IsControl(character) ? ' ' : character;

                if (normalized == ' ')
                {
                    if (lastWasSpace || builder.Length == 0) continue;
                    lastWasSpace = true;
                }
                else
                {
                    lastWasSpace = false;
                }

                builder.Append(normalized);
                if (builder.Length >= maxLength) break;
            }

            return builder.ToString().TrimEnd();
        }
    }
}
