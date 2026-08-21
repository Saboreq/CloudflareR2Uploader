using System;
using System.Globalization;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    internal sealed class UploadObjectVerifier
    {
        private readonly ILoggingService _log;

        public UploadObjectVerifier(ILoggingService log)
        {
            _log = log;
        }

        public UploadResult Verify(
            UploadQueueItem item,
            AppSettings settings,
            string objectKey,
            long remoteLength,
            string uploadETag,
            string headETag,
            bool wasMultipart)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(settings);

            if (remoteLength != item.FileSize)
            {
                string detail = string.Format(
                    CultureInfo.CurrentCulture,
                    "R2 stored {0} but the local file is {1}. The object was not the expected size, so it should not be treated as a good copy.",
                    FileSizeFormatter.Format(remoteLength),
                    FileSizeFormatter.Format(item.FileSize));

                LogFailure(objectKey,
                    "Size mismatch for key=" + objectKey + ": remote=" + remoteLength + " local=" + item.FileSize);
                return Fail(item, settings, objectKey,
                    "The uploaded object does not match the local file", detail);
            }

            if (settings.VerifyETagAfterUpload)
            {
                string normalizedUploadETag = NormalizeETag(uploadETag);
                string normalizedHeadETag = NormalizeETag(headETag);
                if (normalizedUploadETag.Length == 0 || normalizedHeadETag.Length == 0 ||
                    !string.Equals(normalizedUploadETag, normalizedHeadETag, StringComparison.OrdinalIgnoreCase))
                {
                    const string detail = "The ETag returned by the upload did not match the ETag read back from R2. The object should not be treated as a good copy.";
                    LogFailure(objectKey, "ETag verification failed for key=" + objectKey + ".");
                    return Fail(item, settings, objectKey,
                        "The uploaded object could not be identified", detail);
                }
            }

            string eTag = !string.IsNullOrEmpty(headETag) ? headETag : uploadETag;
            string publicUrl = ObjectKeyUtility.BuildPublicUrl(settings.PublicBaseUrl, objectKey);

            item.SetVerified(eTag, remoteLength, publicUrl);
            item.SetStatus(UploadItemStatus.Completed);

            if (_log != null)
            {
                _log.Info("Upload.Verify",
                    "Verified key=" + objectKey + " size=" + remoteLength +
                    " multipart=" + wasMultipart + " etag=" + eTag);
            }

            return UploadResult.Success(objectKey, eTag, remoteLength, wasMultipart);
        }

        internal static string NormalizeETag(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
                normalized = normalized[1..^1];
            return normalized;
        }

        private static UploadResult Fail(
            UploadQueueItem item,
            AppSettings settings,
            string objectKey,
            string headline,
            string detail)
        {
            string message = headline + ". " + detail;
            string technical = FriendlyErrorService.BuildTechnicalDetails(null, settings, objectKey);
            item.SetFailure(message, technical);
            return UploadResult.Failure(objectKey, message, technical, true, false);
        }

        private void LogFailure(string objectKey, string message)
        {
            if (_log != null) _log.Error("Upload.Verify", message, null);
        }
    }
}
