namespace CloudflareR2Uploader.Models
{
    /// <summary>Lifecycle of one queued file.</summary>
    public enum UploadItemStatus
    {
        Queued = 0,
        Preparing = 1,
        Uploading = 2,
        Paused = 3,
        Verifying = 4,
        Completed = 5,
        Failed = 6,
        Cancelled = 7,
        Skipped = 8
    }

    public static class UploadItemStatusExtensions
    {
        /// <summary>True once the item will not change again without user action.</summary>
        public static bool IsTerminal(this UploadItemStatus status)
        {
            return status == UploadItemStatus.Completed
                || status == UploadItemStatus.Failed
                || status == UploadItemStatus.Cancelled
                || status == UploadItemStatus.Skipped;
        }

        /// <summary>True while the item is actively holding network resources.</summary>
        public static bool IsActive(this UploadItemStatus status)
        {
            return status == UploadItemStatus.Preparing
                || status == UploadItemStatus.Uploading
                || status == UploadItemStatus.Verifying;
        }

        /// <summary>Screen-reader friendly wording; never relies on colour alone.</summary>
        public static string ToDisplayText(this UploadItemStatus status)
        {
            switch (status)
            {
                case UploadItemStatus.Queued: return "Queued";
                case UploadItemStatus.Preparing: return "Preparing";
                case UploadItemStatus.Uploading: return "Uploading";
                case UploadItemStatus.Paused: return "Paused";
                case UploadItemStatus.Verifying: return "Verifying";
                case UploadItemStatus.Completed: return "Completed";
                case UploadItemStatus.Failed: return "Failed";
                case UploadItemStatus.Cancelled: return "Cancelled";
                case UploadItemStatus.Skipped: return "Skipped";
                default: return status.ToString();
            }
        }
    }
}
