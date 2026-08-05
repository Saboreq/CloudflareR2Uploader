#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Stores the activity history as JSON Lines under
    /// <c>%LocalAppData%\CloudflareR2Uploader\activity.jsonl</c>.
    /// <para>
    /// One object per line is used rather than a single JSON array so that appending is an
    /// ordinary append — a crash mid-write can only ever damage the last line, which the
    /// reader skips. Rewrites (prune, clear, compaction) go through a temporary file and a
    /// replace, so a reader never sees a half-written history.
    /// </para>
    /// </summary>
    public sealed class ActivityHistoryService : IActivityHistoryService, IDisposable
    {
        /// <summary>Upper bound on the file. Reached first by volume, not by age.</summary>
        public const long MaxFileBytes = 4L * 1024L * 1024L;

        /// <summary>Records kept when the file is compacted. Newest are kept.</summary>
        public const int MaxRecords = 5000;

        /// <summary>A single line longer than this is treated as corrupt and skipped.</summary>
        private const int MaxLineLength = 8 * 1024;

        private readonly ILoggingService? _log;
        private readonly string _filePath;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        private bool _disposed;

        public ActivityHistoryService(ILoggingService? log = null, string? filePath = null)
        {
            _log = log;
            _filePath = string.IsNullOrEmpty(filePath) ? AppPaths.ActivityHistoryFilePath : filePath!;
        }

        public event EventHandler<ActivityRecordedEventArgs>? Recorded;
        public event EventHandler? Cleared;

        public string FilePath { get { return _filePath; } }

        public async Task RecordAsync(ActivityRecord record, CancellationToken cancellationToken = default)
        {
            if (record is null) throw new ArgumentNullException(nameof(record));
            if (_disposed) return;

            ActivityRecord safe = ActivitySanitizer.Sanitize(record);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                AppPaths.EnsureDirectory(Path.GetDirectoryName(_filePath));

                string line = Serialize(safe);
                using (FileStream stream = new FileStream(
                    _filePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true))
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }

                await CompactIfOversizedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                // History is diagnostic. Losing an entry must never fail the operation that
                // produced it, so this is logged and swallowed.
                _log?.Warning("Activity.Record", "An activity entry could not be written (" + ex.GetType().Name + ").");
                return;
            }
            finally
            {
                if (!_disposed) _gate.Release();
            }

            Recorded?.Invoke(this, new ActivityRecordedEventArgs(safe));
        }

        public async Task<IReadOnlyList<ActivityRecord>> QueryAsync(
            ActivityQuery query, CancellationToken cancellationToken = default)
        {
            List<ActivityRecord> all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);

            List<ActivityRecord> matches = new List<ActivityRecord>();
            foreach (ActivityRecord record in all)
            {
                if (Matches(record, query)) matches.Add(record);
            }

            matches.Sort(static (left, right) => right.TimestampUtc.CompareTo(left.TimestampUtc));
            return matches;
        }

        public async Task<ActivitySummary> SummarizeAsync(
            ActivityQuery query, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ActivityRecord> matches = await QueryAsync(query, cancellationToken).ConfigureAwait(false);

            ActivitySummary summary = new ActivitySummary { EventCount = matches.Count };
            foreach (ActivityRecord record in matches)
            {
                if (record.Result == ActivityResult.Failed) summary.FailedCount++;
                else summary.SucceededCount++;

                if (record.SizeBytes.HasValue &&
                    (record.Action == ActivityAction.Upload || record.Action == ActivityAction.Download))
                {
                    summary.TransferredBytes += record.SizeBytes.Value;
                }
            }

            return summary;
        }

        public async Task ClearAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (File.Exists(_filePath)) File.Delete(_filePath);
                _log?.Info("Activity.Clear", "The activity history was cleared by the user.");
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                _log?.Error("Activity.Clear", "The activity history could not be cleared.", ex);
            }
            finally
            {
                if (!_disposed) _gate.Release();
            }

            Cleared?.Invoke(this, EventArgs.Empty);
        }

        public async Task<int> PruneAsync(int retentionDays, CancellationToken cancellationToken = default)
        {
            int days = retentionDays < AppSettings.MinActivityRetentionDays
                ? AppSettings.MinActivityRetentionDays
                : (retentionDays > AppSettings.MaxActivityRetentionDays
                    ? AppSettings.MaxActivityRetentionDays
                    : retentionDays);

            DateTime cutoff = DateTime.UtcNow.AddDays(-days);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                List<ActivityRecord> all = ReadAllUnlocked();
                if (all.Count == 0) return 0;

                List<ActivityRecord> kept = new List<ActivityRecord>(all.Count);
                foreach (ActivityRecord record in all)
                {
                    if (record.TimestampUtc >= cutoff) kept.Add(record);
                }

                int removed = all.Count - kept.Count;
                removed += TrimToMaxRecords(kept);

                if (removed > 0)
                {
                    RewriteUnlocked(kept);
                    _log?.Info("Activity.Prune",
                        "Removed " + removed.ToString(CultureInfo.InvariantCulture) +
                        " activity entries older than " + days.ToString(CultureInfo.InvariantCulture) + " day(s).");
                }

                return removed;
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                _log?.Error("Activity.Prune", "The activity history could not be pruned.", ex);
                return 0;
            }
            finally
            {
                if (!_disposed) _gate.Release();
            }
        }

        // --------------------------------------------------------------------------- reading

        private async Task<List<ActivityRecord>> ReadAllAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return ReadAllUnlocked();
            }
            finally
            {
                if (!_disposed) _gate.Release();
            }
        }

        /// <summary>
        /// Reads every parsable record. A damaged or truncated line is skipped rather than
        /// failing the whole read, which is the recovery behaviour the file format exists for.
        /// </summary>
        private List<ActivityRecord> ReadAllUnlocked()
        {
            List<ActivityRecord> results = new List<ActivityRecord>();
            int skipped = 0;

            try
            {
                if (!File.Exists(_filePath)) return results;

                using (FileStream stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false)))
                {
                    string? line;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        if (line.Length == 0) continue;
                        if (line.Length > MaxLineLength) { skipped++; continue; }

                        ActivityRecord? record = TryDeserialize(line);
                        if (record is null || !record.IsStructurallyValid()) { skipped++; continue; }

                        results.Add(record);
                    }
                }
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                _log?.Warning("Activity.Read", "The activity history could not be read (" + ex.GetType().Name + ").");
            }

            if (skipped > 0)
            {
                _log?.Warning("Activity.Read",
                    "Skipped " + skipped.ToString(CultureInfo.InvariantCulture) + " damaged activity entries.");
            }

            return results;
        }

        internal static bool Matches(ActivityRecord record, ActivityQuery? query)
        {
            if (query is null) return true;

            if (query.ErrorsOnly)
            {
                if (!record.IsError) return false;
            }
            else if (query.Category.HasValue && record.Category != query.Category.Value)
            {
                return false;
            }

            if (query.FromUtc.HasValue && record.TimestampUtc < query.FromUtc.Value) return false;
            if (query.ToUtc.HasValue && record.TimestampUtc > query.ToUtc.Value) return false;

            if (!string.IsNullOrWhiteSpace(query.SearchText))
            {
                string needle = query.SearchText!.Trim();
                if (!Contains(record.Target, needle) &&
                    !Contains(record.SecondaryTarget, needle) &&
                    !Contains(record.BucketName, needle) &&
                    !Contains(record.ErrorCode, needle) &&
                    !Contains(record.ErrorMessage, needle))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Contains(string? value, string needle)
        {
            return (value ?? string.Empty).IndexOf(needle, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        // --------------------------------------------------------------------------- writing

        /// <summary>Keeps only the newest <see cref="MaxRecords"/>. Returns how many it dropped.</summary>
        private static int TrimToMaxRecords(List<ActivityRecord> records)
        {
            if (records.Count <= MaxRecords) return 0;

            records.Sort(static (left, right) => left.TimestampUtc.CompareTo(right.TimestampUtc));
            int excess = records.Count - MaxRecords;
            records.RemoveRange(0, excess);
            return excess;
        }

        private async Task CompactIfOversizedAsync(CancellationToken cancellationToken)
        {
            FileInfo info = new FileInfo(_filePath);
            if (!info.Exists || info.Length <= MaxFileBytes) return;

            await Task.Run(
                () =>
                {
                    List<ActivityRecord> all = ReadAllUnlocked();
                    TrimToMaxRecords(all);

                    // A single very large burst can exceed the byte bound while still being
                    // under MaxRecords, so halve it until the file fits.
                    while (all.Count > 0 && EstimateBytes(all) > MaxFileBytes)
                    {
                        all.RemoveRange(0, Math.Max(1, all.Count / 2));
                    }

                    RewriteUnlocked(all);
                },
                cancellationToken).ConfigureAwait(false);

            _log?.Info("Activity.Compact", "The activity history exceeded its size bound and was compacted.");
        }

        private static long EstimateBytes(List<ActivityRecord> records)
        {
            long total = 0;
            foreach (ActivityRecord record in records)
            {
                total += 240
                    + (record.Target?.Length ?? 0)
                    + (record.SecondaryTarget?.Length ?? 0)
                    + (record.ErrorMessage?.Length ?? 0);
            }
            return total;
        }

        /// <summary>Writes the whole history through a temporary file, then replaces atomically.</summary>
        private void RewriteUnlocked(List<ActivityRecord> records)
        {
            records.Sort(static (left, right) => left.TimestampUtc.CompareTo(right.TimestampUtc));

            AppPaths.EnsureDirectory(Path.GetDirectoryName(_filePath));
            string temporary = _filePath + ".tmp";

            using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                foreach (ActivityRecord record in records) writer.WriteLine(Serialize(record));
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(_filePath)) File.Delete(_filePath);
            File.Move(temporary, _filePath);
        }

        // --------------------------------------------------------------------- serialisation

        internal static string Serialize(ActivityRecord record)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(ActivityRecord)).WriteObject(stream, record);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static ActivityRecord? TryDeserialize(string line)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                using (MemoryStream stream = new MemoryStream(bytes, writable: false))
                {
                    return new DataContractJsonSerializer(typeof(ActivityRecord)).ReadObject(stream) as ActivityRecord;
                }
            }
            catch (Exception ex) when (
                ex is System.Runtime.Serialization.SerializationException ||
                ex is System.Xml.XmlException ||
                ex is FormatException ||
                ex is ArgumentException)
            {
                return null;
            }
        }

        private static bool IsRecoverable(Exception ex)
        {
            return ex is IOException
                || ex is UnauthorizedAccessException
                || ex is System.Runtime.Serialization.SerializationException
                || ex is System.Xml.XmlException
                || ex is FormatException
                || ex is ArgumentException;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _gate.Dispose();
        }
    }
}
