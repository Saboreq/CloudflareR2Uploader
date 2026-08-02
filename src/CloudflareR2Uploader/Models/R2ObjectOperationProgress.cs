namespace CloudflareR2Uploader.Models
{
    /// <summary>An immutable progress snapshot for a browser-initiated object operation.</summary>
    public sealed class R2ObjectOperationProgress
    {
        public R2ObjectOperationProgress(
            string operation,
            int completedObjects,
            string currentKey,
            long transferredBytes,
            long totalBytes)
        {
            Operation = operation ?? string.Empty;
            CompletedObjects = completedObjects < 0 ? 0 : completedObjects;
            CurrentKey = currentKey ?? string.Empty;
            TransferredBytes = transferredBytes < 0 ? 0 : transferredBytes;
            TotalBytes = totalBytes < 0 ? 0 : totalBytes;
        }

        public string Operation { get; private set; }
        public int CompletedObjects { get; private set; }
        public string CurrentKey { get; private set; }
        public long TransferredBytes { get; private set; }
        public long TotalBytes { get; private set; }

        public int Percent
        {
            get
            {
                if (TotalBytes <= 0) return 0;
                double value = (double)TransferredBytes / TotalBytes;
                if (value < 0) value = 0;
                if (value > 1) value = 1;
                return (int)(value * 100.0);
            }
        }
    }
}
