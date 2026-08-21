#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    /// <summary>Narrows a history query. All ranges are inclusive and expressed in UTC.</summary>
    public sealed class ActivityQuery
    {
        public ActivityCategory? Category { get; set; }

        /// <summary>When true, only failures are returned, whatever <see cref="Category"/> says.</summary>
        public bool ErrorsOnly { get; set; }

        public DateTime? FromUtc { get; set; }

        public DateTime? ToUtc { get; set; }

        /// <summary>Case-insensitive substring match over target, bucket and error text.</summary>
        public string? SearchText { get; set; }
    }

    /// <summary>Aggregates shown in the Activity footer and in the date-group headers.</summary>
    public sealed class ActivitySummary
    {
        public int EventCount { get; set; }
        public int SucceededCount { get; set; }
        public int FailedCount { get; set; }
        public long TransferredBytes { get; set; }
    }

    /// <summary>
    /// Local, per-user record of the operations the application performed.
    /// <para>
    /// Implementations must sanitise every value before it is written: no credentials, no
    /// authorization headers, no signed-URL query strings and no private object contents
    /// may reach the file or a CSV export.
    /// </para>
    /// </summary>
    public interface IActivityHistoryService
    {
        /// <summary>Raised after a record is appended, on the thread that appended it.</summary>
        event EventHandler<ActivityRecordedEventArgs>? Recorded;

        /// <summary>Raised after the history is cleared.</summary>
        event EventHandler? Cleared;

        /// <summary>
        /// Appends one record. Never throws for an I/O failure: history is diagnostic, and
        /// losing an entry must not fail the operation that produced it.
        /// </summary>
        Task RecordAsync(ActivityRecord record, CancellationToken cancellationToken = default);

        /// <summary>Reads matching records, newest first.</summary>
        Task<IReadOnlyList<ActivityRecord>> QueryAsync(ActivityQuery query, CancellationToken cancellationToken = default);

        /// <summary>Aggregates over the same predicate as <see cref="QueryAsync"/>.</summary>
        Task<ActivitySummary> SummarizeAsync(ActivityQuery query, CancellationToken cancellationToken = default);

        /// <summary>Removes every record and the backing file. Returns false when the backing file remains.</summary>
        Task<bool> ClearAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Drops records older than the retention window and enforces the file-size bound.
        /// Returns the number of records removed.
        /// </summary>
        Task<int> PruneAsync(int retentionDays, CancellationToken cancellationToken = default);
    }

    public sealed class ActivityRecordedEventArgs : EventArgs
    {
        public ActivityRecordedEventArgs(ActivityRecord record) { Record = record; }

        public ActivityRecord Record { get; }
    }
}
