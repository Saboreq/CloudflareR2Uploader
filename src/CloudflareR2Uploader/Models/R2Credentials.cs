using System;

namespace CloudflareR2Uploader.Models
{
    /// <summary>
    /// The R2 S3 API credential pair. Deliberately a separate type from
    /// <see cref="AppSettings"/> so the secret never travels with the object that gets
    /// serialised to JSON, and so it is obvious at every call site what is sensitive.
    /// </summary>
    public sealed class R2Credentials
    {
        public R2Credentials(string accessKeyId, string secretAccessKey)
        {
            AccessKeyId = accessKeyId ?? string.Empty;
            SecretAccessKey = secretAccessKey ?? string.Empty;
        }

        public string AccessKeyId { get; private set; }

        /// <summary>Never logged, never shown in an error message, never serialised to JSON.</summary>
        public string SecretAccessKey { get; private set; }

        public bool IsComplete
        {
            get
            {
                return !string.IsNullOrWhiteSpace(AccessKeyId)
                    && !string.IsNullOrWhiteSpace(SecretAccessKey);
            }
        }

        public static R2Credentials Empty
        {
            get { return new R2Credentials(string.Empty, string.Empty); }
        }

        /// <summary>
        /// Safe for logs: shows only the first four characters of the Access Key ID and
        /// nothing at all of the secret.
        /// </summary>
        public string ToLogSafeString()
        {
            string id = AccessKeyId ?? string.Empty;
            string masked = id.Length <= 4 ? new string('*', id.Length) : id.Substring(0, 4) + new string('*', id.Length - 4);
            return "accessKeyId=" + masked + ", secretAccessKey=<redacted>";
        }

        /// <summary>Never expose the secret through ToString(), including in debuggers and logs.</summary>
        public override string ToString()
        {
            return ToLogSafeString();
        }
    }
}
