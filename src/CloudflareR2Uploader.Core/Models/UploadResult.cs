using System;

namespace CloudflareR2Uploader.Models
{
    /// <summary>Why an upload attempt ended.</summary>
    public enum UploadOutcome
    {
        Succeeded = 0,
        Failed = 1,
        Cancelled = 2,
        Skipped = 3,
        Paused = 4
    }

    /// <summary>
    /// The result of uploading one file, including the verification data read back from R2.
    /// </summary>
    public sealed class UploadResult
    {
        private UploadResult() { }

        public UploadOutcome Outcome { get; private set; }
        public string ObjectKey { get; private set; }

        /// <summary>
        /// The object ETag reported by R2. For multipart objects this is a digest of the part
        /// ETags with a "-N" suffix; it is not the MD5 of the file and is never compared as one.
        /// </summary>
        public string ETag { get; private set; }

        /// <summary>Object length reported by HeadObject, used to verify the upload.</summary>
        public long RemoteLength { get; private set; }

        public bool WasMultipart { get; private set; }

        /// <summary>Human-readable message suitable for showing directly in the UI.</summary>
        public string Message { get; private set; }

        /// <summary>Sanitised technical detail for the "Copy technical details" button.</summary>
        public string TechnicalDetails { get; private set; }

        /// <summary>True when the failure can be retried by the user without other changes.</summary>
        public bool IsRetryable { get; private set; }

        /// <summary>True when a resumable multipart upload was preserved for later.</summary>
        public bool ResumeStatePreserved { get; private set; }

        public static UploadResult Success(string objectKey, string eTag, long remoteLength, bool wasMultipart)
        {
            return new UploadResult
            {
                Outcome = UploadOutcome.Succeeded,
                ObjectKey = objectKey,
                ETag = eTag,
                RemoteLength = remoteLength,
                WasMultipart = wasMultipart,
                Message = "Upload complete."
            };
        }

        public static UploadResult Failure(string objectKey, string message, string technicalDetails, bool isRetryable, bool resumeStatePreserved)
        {
            return new UploadResult
            {
                Outcome = UploadOutcome.Failed,
                ObjectKey = objectKey,
                Message = message,
                TechnicalDetails = technicalDetails,
                IsRetryable = isRetryable,
                ResumeStatePreserved = resumeStatePreserved
            };
        }

        public static UploadResult Cancelled(string objectKey, bool resumeStatePreserved)
        {
            return new UploadResult
            {
                Outcome = UploadOutcome.Cancelled,
                ObjectKey = objectKey,
                IsRetryable = true,
                ResumeStatePreserved = resumeStatePreserved,
                Message = resumeStatePreserved
                    ? "Cancelled. The uploaded parts were kept so this file can be resumed."
                    : "Cancelled. The incomplete upload was removed from the bucket."
            };
        }

        public static UploadResult Skipped(string objectKey, string message)
        {
            return new UploadResult
            {
                Outcome = UploadOutcome.Skipped,
                ObjectKey = objectKey,
                Message = message
            };
        }

        public static UploadResult Paused(string objectKey)
        {
            return new UploadResult
            {
                Outcome = UploadOutcome.Paused,
                ObjectKey = objectKey,
                IsRetryable = true,
                ResumeStatePreserved = true,
                Message = "Paused. Completed parts were kept, so resuming continues where it stopped."
            };
        }
    }
}
