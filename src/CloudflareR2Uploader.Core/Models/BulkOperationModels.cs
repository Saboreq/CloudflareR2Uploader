using System;
using System.Collections.Generic;

namespace CloudflareR2Uploader.Models
{
    public enum BulkConflictBehavior
    {
        Ask = 0,
        Overwrite = 1,
        Skip = 2,
        RenameAutomatically = 3
    }

    public sealed class BulkObjectEntry
    {
        public string Key { get; set; }
        public long Size { get; set; }
        public DateTime? LastModifiedUtc { get; set; }
        public string ETag { get; set; }
        public bool IsFolderMarker { get; set; }
        public string SelectedPrefix { get; set; }
        public string RelativePath { get; set; }
        public string LocalPath { get; set; }
    }

    public sealed class BulkListingPage
    {
        public BulkListingPage() { Objects = new List<BulkObjectEntry>(); }
        public List<BulkObjectEntry> Objects { get; private set; }
        public string NextContinuationToken { get; set; }
    }

    public sealed class BulkDeleteBatchResult
    {
        public BulkDeleteBatchResult() { Failures = new List<BulkObjectFailure>(); }
        public List<BulkObjectFailure> Failures { get; private set; }
    }

    public sealed class BulkObjectOperationPlan
    {
        public BulkObjectOperationPlan()
        {
            Objects = new List<BulkObjectEntry>();
            EmptyDirectoryPaths = new List<string>();
        }

        public List<BulkObjectEntry> Objects { get; private set; }
        public List<string> EmptyDirectoryPaths { get; private set; }
        public int SelectedFileCount { get; set; }
        public int SelectedFolderCount { get; set; }
        public long TotalBytes { get; set; }
        public string DestinationDirectory { get; set; }
    }

    public sealed class BulkObjectFailure
    {
        public string Key { get; set; }
        public string Message { get; set; }
    }

    public sealed class BulkObjectOperationResult
    {
        public BulkObjectOperationResult()
        {
            Failures = new List<BulkObjectFailure>();
        }

        public int Completed { get; set; }
        public int Succeeded { get; set; }
        public int Skipped { get; set; }
        public int Renamed { get; set; }
        public bool Cancelled { get; set; }
        public long TransferredBytes { get; set; }
        public string DestinationDirectory { get; set; }
        public List<BulkObjectFailure> Failures { get; private set; }
    }

    public sealed class BulkOperationProgress
    {
        public string State { get; set; }
        public string CurrentObject { get; set; }
        public int CompletedObjects { get; set; }
        public int TotalObjects { get; set; }
        public long TransferredBytes { get; set; }
        public long TotalBytes { get; set; }
    }

    public sealed class BulkConflictDecision
    {
        public BulkConflictBehavior Behavior { get; set; }
        public bool ApplyToRemaining { get; set; }
    }
}
