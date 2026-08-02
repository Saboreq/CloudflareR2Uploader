using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    internal interface IR2BulkObjectSource
    {
        Task<BulkListingPage> ListAsync(AppSettings settings, R2Credentials credentials, string prefix, string continuationToken, CancellationToken cancellationToken);
        Task<long?> DownloadAsync(AppSettings settings, R2Credentials credentials, string key, Stream destination, CancellationToken cancellationToken);
        Task<BulkDeleteBatchResult> DeleteAsync(AppSettings settings, R2Credentials credentials, IList<string> keys, CancellationToken cancellationToken);
    }

    public sealed class R2BulkOperationService
    {
        public const int DefaultDownloadConcurrency = 3;
        private readonly IR2BulkObjectSource _source;
        private readonly ILoggingService _log;

        public R2BulkOperationService(ILoggingService log) : this(log, new AwsBulkObjectSource()) { }
        internal R2BulkOperationService(ILoggingService log, IR2BulkObjectSource source)
        {
            _log = log;
            _source = source ?? throw new ArgumentNullException("source");
        }

        public async Task<BulkObjectOperationPlan> PlanAsync(
            AppSettings settings,
            R2Credentials credentials,
            IEnumerable<R2BrowserItem> selection,
            IProgress<BulkOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            List<R2BrowserItem> selected = selection == null ? new List<R2BrowserItem>() : selection.Where(item => item != null).Select(item => item.Clone()).ToList();
            BulkObjectOperationPlan plan = new BulkObjectOperationPlan
            {
                SelectedFileCount = selected.Count(item => !item.IsFolder),
                SelectedFolderCount = selected.Count(item => item.IsFolder)
            };
            Dictionary<string, BulkObjectEntry> unique = new Dictionary<string, BulkObjectEntry>(StringComparer.Ordinal);

            List<R2BrowserItem> folders = selected.Where(item => item.IsFolder)
                .OrderBy(item => (item.Prefix ?? string.Empty).Length).ToList();
            List<string> includedPrefixes = new List<string>();
            HashSet<string> folderRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (R2BrowserItem folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string prefix = ObjectKeyUtility.NormalizePrefix(folder.Prefix);
                if (includedPrefixes.Any(parent => prefix.StartsWith(parent, StringComparison.Ordinal))) continue;
                includedPrefixes.Add(prefix);
                string folderRoot = GetPrefixLeaf(prefix);
                if (!folderRoots.Add(folderRoot))
                {
                    int suffix = 1;
                    string candidate;
                    do { candidate = folderRoot + " (" + suffix++ + ")"; } while (!folderRoots.Add(candidate));
                    folderRoot = candidate;
                }
                string continuation = null;
                int before = unique.Count;
                do
                {
                    BulkListingPage page = await _source.ListAsync(settings, credentials, prefix, continuation, cancellationToken).ConfigureAwait(false);
                    foreach (BulkObjectEntry entry in page.Objects)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        entry.SelectedPrefix = prefix;
                        entry.RelativePath = BuildFolderRelativePath(prefix, entry.Key, folderRoot);
                        if (!unique.ContainsKey(entry.Key)) unique.Add(entry.Key, entry);
                    }
                    continuation = page.NextContinuationToken;
                    Report(progress, "Scanning folders", null, unique.Count, 0, 0, 0);
                }
                while (!string.IsNullOrEmpty(continuation));
                if (unique.Count == before) plan.EmptyDirectoryPaths.Add(folderRoot);
            }

            foreach (R2BrowserItem file in selected.Where(item => !item.IsFolder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (includedPrefixes.Any(prefix => (file.Key ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal))) continue;
                if (!unique.ContainsKey(file.Key))
                {
                    unique.Add(file.Key, new BulkObjectEntry
                    {
                        Key = file.Key,
                        Size = file.Size,
                        LastModifiedUtc = file.LastModifiedUtc,
                        ETag = file.ETag,
                        RelativePath = file.DisplayName
                    });
                }
            }

            foreach (BulkObjectEntry entry in unique.Values.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                plan.Objects.Add(entry);
                if (entry.Size > 0) plan.TotalBytes += entry.Size;
            }
            Report(progress, "Preflight complete", null, plan.Objects.Count, plan.Objects.Count, 0, plan.TotalBytes);
            return plan;
        }

        public async Task<BulkObjectOperationResult> DownloadAsync(
            AppSettings settings,
            R2Credentials credentials,
            BulkObjectOperationPlan plan,
            BulkConflictBehavior conflictBehavior,
            Func<string, Task<BulkConflictDecision>> conflictPrompt,
            IProgress<BulkOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            foreach (string directory in plan.EmptyDirectoryPaths) Directory.CreateDirectory(PathUtility.ToExtendedLengthPath(directory));
            BulkObjectOperationResult result = new BulkObjectOperationResult { DestinationDirectory = plan.DestinationDirectory };
            object gate = new object();
            int next = 0;
            BulkConflictBehavior sticky = conflictBehavior;
            bool hasSticky = conflictBehavior != BulkConflictBehavior.Ask;
            List<Task> workers = new List<Task>();
            for (int worker = 0; worker < Math.Min(DefaultDownloadConcurrency, Math.Max(1, plan.Objects.Count)); worker++)
            {
                workers.Add(Task.Run(async () =>
                {
                    while (true)
                    {
                        BulkObjectEntry entry;
                        lock (gate)
                        {
                            if (next >= plan.Objects.Count) return;
                            entry = plan.Objects[next++];
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        string target = entry.LocalPath;
                        if (entry.IsFolderMarker)
                        {
                            Directory.CreateDirectory(PathUtility.ToExtendedLengthPath(target));
                            lock (gate) { result.Completed++; result.Succeeded++; }
                            Report(progress, "Creating folders", entry.Key, result.Completed, plan.Objects.Count, result.TransferredBytes, plan.TotalBytes);
                            continue;
                        }
                        BulkConflictBehavior behavior = hasSticky ? sticky : BulkConflictBehavior.Ask;
                        if (File.Exists(PathUtility.ToExtendedLengthPath(target)))
                        {
                            if (behavior == BulkConflictBehavior.Ask && conflictPrompt != null)
                            {
                                BulkConflictDecision decision = await conflictPrompt(target).ConfigureAwait(false);
                                behavior = decision == null ? BulkConflictBehavior.Skip : decision.Behavior;
                                if (decision != null && decision.ApplyToRemaining) { lock (gate) { sticky = behavior; hasSticky = true; } }
                            }
                            if (behavior == BulkConflictBehavior.Skip) { lock (gate) { result.Skipped++; result.Completed++; } continue; }
                            if (behavior == BulkConflictBehavior.RenameAutomatically)
                            {
                                target = BulkConflictNameUtility.FindAvailable(target, value => File.Exists(PathUtility.ToExtendedLengthPath(value)));
                                lock (gate) result.Renamed++;
                                if (_log != null) _log.Info("Bulk.DownloadConflict", "Renamed a conflicting local download path to '" + LoggingService.Sanitize(Path.GetFileName(target)) + "'.");
                            }
                        }

                        string partial = target + ".r2partial";
                        try
                        {
                            Directory.CreateDirectory(PathUtility.ToExtendedLengthPath(Path.GetDirectoryName(target)));
                            long? length;
                            using (FileStream stream = new FileStream(PathUtility.ToExtendedLengthPath(partial), FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                            {
                                length = await _source.DownloadAsync(settings, credentials, entry.Key, stream, cancellationToken).ConfigureAwait(false);
                                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                                if (length.HasValue && stream.Length != length.Value) throw new IOException("The downloaded byte count did not match Content-Length.");
                            }
                            if (File.Exists(PathUtility.ToExtendedLengthPath(target))) File.Delete(PathUtility.ToExtendedLengthPath(target));
                            File.Move(PathUtility.ToExtendedLengthPath(partial), PathUtility.ToExtendedLengthPath(target));
                            if (entry.LastModifiedUtc.HasValue) File.SetLastWriteTimeUtc(PathUtility.ToExtendedLengthPath(target), entry.LastModifiedUtc.Value);
                            lock (gate) { result.Completed++; result.Succeeded++; result.TransferredBytes += entry.Size; }
                        }
                        catch (OperationCanceledException) { TryDelete(partial); throw; }
                        catch (Exception ex)
                        {
                            TryDelete(partial);
                            lock (gate) { result.Failures.Add(new BulkObjectFailure { Key = entry.Key, Message = LoggingService.Sanitize(ex.Message) }); result.Completed++; }
                        }
                        Report(progress, "Downloading", entry.Key, result.Completed, plan.Objects.Count, result.TransferredBytes, plan.TotalBytes);
                    }
                }, cancellationToken));
            }
            try { await Task.WhenAll(workers).ConfigureAwait(false); }
            catch (OperationCanceledException) { result.Cancelled = true; }
            return result;
        }

        public async Task<BulkObjectOperationResult> DeleteAsync(AppSettings settings, R2Credentials credentials, BulkObjectOperationPlan plan, IProgress<BulkOperationProgress> progress, CancellationToken cancellationToken)
        {
            BulkObjectOperationResult result = new BulkObjectOperationResult();
            for (int offset = 0; offset < plan.Objects.Count; offset += 1000)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<string> keys = plan.Objects.Skip(offset).Take(1000).Select(value => value.Key).ToList();
                BulkDeleteBatchResult batch = await _source.DeleteAsync(settings, credentials, keys, cancellationToken).ConfigureAwait(false);
                result.Failures.AddRange(batch.Failures);
                result.Completed += keys.Count;
                result.Succeeded += keys.Count - batch.Failures.Count;
                Report(progress, "Deleting", null, Math.Min(offset + keys.Count, plan.Objects.Count), plan.Objects.Count, 0, plan.TotalBytes);
            }
            return result;
        }

        private static string BuildFolderRelativePath(string prefix, string key, string root)
        {
            string remainder = (key ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal) ? key.Substring(prefix.Length) : key;
            if (remainder.Length == 0) return root;
            return root + "/" + remainder;
        }
        private static string GetPrefixLeaf(string prefix)
        {
            string value = (prefix ?? string.Empty).TrimEnd('/');
            int slash = value.LastIndexOf('/');
            return slash < 0 ? value : value.Substring(slash + 1);
        }
        private static void Report(IProgress<BulkOperationProgress> progress, string state, string current, int completed, int total, long bytes, long totalBytes)
        {
            if (progress != null) progress.Report(new BulkOperationProgress { State = state, CurrentObject = current, CompletedObjects = completed, TotalObjects = total, TransferredBytes = bytes, TotalBytes = totalBytes });
        }
        private static void TryDelete(string path) { try { if (File.Exists(PathUtility.ToExtendedLengthPath(path))) File.Delete(PathUtility.ToExtendedLengthPath(path)); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        private sealed class AwsBulkObjectSource : IR2BulkObjectSource
        {
            public async Task<BulkListingPage> ListAsync(AppSettings settings, R2Credentials credentials, string prefix, string continuationToken, CancellationToken cancellationToken)
            {
                using (AmazonS3Client client = R2ClientFactory.CreateClient(settings, credentials))
                {
                    ListObjectsV2Response response = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = settings.BucketName, Prefix = prefix, ContinuationToken = continuationToken, MaxKeys = 1000 }, cancellationToken).ConfigureAwait(false);
                    BulkListingPage page = new BulkListingPage { NextContinuationToken = response.IsTruncated ? response.NextContinuationToken : null };
                    foreach (S3Object item in response.S3Objects)
                        page.Objects.Add(new BulkObjectEntry { Key = item.Key, Size = item.Size, LastModifiedUtc = item.LastModified.ToUniversalTime(), ETag = item.ETag, IsFolderMarker = item.Key.EndsWith("/", StringComparison.Ordinal) && item.Size == 0 });
                    return page;
                }
            }

            public async Task<long?> DownloadAsync(AppSettings settings, R2Credentials credentials, string key, Stream destination, CancellationToken cancellationToken)
            {
                using (AmazonS3Client client = R2ClientFactory.CreateClient(settings, credentials))
                using (GetObjectResponse response = await client.GetObjectAsync(new GetObjectRequest { BucketName = settings.BucketName, Key = key }, cancellationToken).ConfigureAwait(false))
                {
                    await response.ResponseStream.CopyToAsync(destination, 81920, cancellationToken).ConfigureAwait(false);
                    return response.ContentLength;
                }
            }

            public async Task<BulkDeleteBatchResult> DeleteAsync(AppSettings settings, R2Credentials credentials, IList<string> keys, CancellationToken cancellationToken)
            {
                using (AmazonS3Client client = R2ClientFactory.CreateClient(settings, credentials))
                {
                    DeleteObjectsRequest request = new DeleteObjectsRequest { BucketName = settings.BucketName, Quiet = false };
                    foreach (string key in keys) request.AddKey(key);
                    DeleteObjectsResponse response = await client.DeleteObjectsAsync(request, cancellationToken).ConfigureAwait(false);
                    BulkDeleteBatchResult result = new BulkDeleteBatchResult();
                    foreach (DeleteError error in response.DeleteErrors)
                        result.Failures.Add(new BulkObjectFailure { Key = error.Key, Message = LoggingService.Sanitize(error.Message) });
                    return result;
                }
            }
        }
    }
}
