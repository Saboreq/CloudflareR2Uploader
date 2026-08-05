namespace CloudflareR2Uploader.Models
{
    /// <summary>Summary of a completed browser object operation.</summary>
    public sealed class R2ObjectOperationResult
    {
        public R2ObjectOperationResult(int objectCount, long totalBytes, string destinationKey)
        {
            ObjectCount = objectCount;
            TotalBytes = totalBytes;
            DestinationKey = destinationKey;
        }

        public int ObjectCount { get; private set; }
        public long TotalBytes { get; private set; }
        public string DestinationKey { get; private set; }
    }
}
