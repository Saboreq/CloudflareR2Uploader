namespace CloudflareR2Uploader.Models
{
    /// <summary>What to do when an object with the target key already exists in the bucket.</summary>
    public enum OverwriteBehavior
    {
        /// <summary>Stop and ask the user for each conflict.</summary>
        Ask = 0,

        /// <summary>Replace the existing object.</summary>
        Overwrite = 1,

        /// <summary>Leave the existing object alone and mark the queue item as skipped.</summary>
        Skip = 2,

        /// <summary>Upload under a new key: "name (1).ext", "name (2).ext", ...</summary>
        Rename = 3
    }

    /// <summary>The user's answer to a single overwrite prompt.</summary>
    public enum OverwriteDecision
    {
        Overwrite = 0,
        Skip = 1,
        Rename = 2,
        CancelAll = 3
    }
}
