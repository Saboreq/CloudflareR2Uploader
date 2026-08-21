using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>How a multipart upload ended.</summary>
    public enum MultipartOutcome
    {
        Completed = 0,
        PausedWithStatePreserved = 1,
        CancelledWithStatePreserved = 2,
        CancelledAndAborted = 3
    }

    public sealed class MultipartUploadResult
    {
        public MultipartUploadResult(MultipartOutcome outcome, string eTag, string uploadId, int partCount)
        {
            Outcome = outcome;
            ETag = eTag;
            UploadId = uploadId;
            PartCount = partCount;
        }

        public MultipartOutcome Outcome { get; private set; }

        /// <summary>
        /// The object ETag R2 returned from CompleteMultipartUpload. For a multipart object
        /// this is a digest of the part ETags with a "-N" suffix, never the whole-file MD5,
        /// so it must not be compared against a locally computed hash.
        /// </summary>
        public string ETag { get; private set; }

        public string UploadId { get; private set; }
        public int PartCount { get; private set; }
    }

    /// <summary>
    /// Drives S3/R2 multipart uploads directly — initiate, upload parts, list parts, complete,
    /// abort — so that resume, pause, per-part retry and progress aggregation are all fully
    /// under this application's control.
    /// <para>
    /// Parts are read through <see cref="BoundedFileStream"/>, one stream per in-flight part,
    /// so a multi-gigabyte file is never held in memory.
    /// </para>
    /// </summary>
    public sealed class R2MultipartUploader
    {
        private readonly ILoggingService _log;
        private readonly UploadStateStore _stateStore;

        public R2MultipartUploader(ILoggingService log, UploadStateStore stateStore)
        {
            _log = log;
            _stateStore = stateStore;
        }

        /// <summary>Raised when a resumable upload could not be resumed, with the reason why.</summary>
        public event EventHandler<ResumeRejectedEventArgs> ResumeRejected;

        public async Task<MultipartUploadResult> UploadAsync(
            IAmazonS3 client,
            string bucketName,
            string objectKey,
            UploadQueueItem item,
            string contentType,
            AppSettings settings,
            RetryService retryService,
            PauseController pauseController,
            IProgress<UploadProgressInfo> progress,
            SpeedEstimator speedEstimator,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(retryService);

            long fileSize = item.FileSize;
            string stateId = UploadStateStore.BuildStateId(item.LocalFilePath, bucketName, objectKey);

            MultipartUploadState state = await PrepareStateAsync(
                client, bucketName, objectKey, item, contentType, settings,
                retryService, stateId, cancellationToken).ConfigureAwait(false);

            long partSize = state.PartSize;
            int totalParts = MultipartCalculator.CalculatePartCount(fileSize, partSize);
            item.SetMultipartLayout(totalParts);

            ProgressTracker tracker = new ProgressTracker(fileSize, totalParts, progress, speedEstimator);
            tracker.SetConfirmed(state.Parts);

            if (_log != null)
            {
                _log.Info("Multipart.Start", string.Format(
                    CultureInfo.InvariantCulture,
                    "key={0} size={1} partSize={2} parts={3} alreadyUploaded={4} parallel={5} uploadId={6}",
                    objectKey, fileSize, partSize, totalParts, state.Parts.Count,
                    settings.ParallelParts, LoggingService.ShortenUploadId(state.UploadId)));
            }

            tracker.Report(true);

            bool userCancelled = false;
            bool paused = false;

            try
            {
                await UploadMissingPartsAsync(
                    client, bucketName, objectKey, item, state, totalParts,
                    settings, retryService, pauseController, tracker, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                userCancelled = true;
            }

            if (userCancelled)
            {
                return await HandleCancellationAsync(client, bucketName, objectKey, item, state).ConfigureAwait(false);
            }

            // A pause that arrived while parts were in flight leaves work outstanding.
            if (pauseController != null && pauseController.IsPaused && state.Parts.Count < totalParts)
            {
                paused = true;
            }

            if (paused)
            {
                _stateStore.Save(state);
                item.SetResumableState(true);
                if (_log != null)
                {
                    _log.Info("Multipart.Paused",
                        "key=" + objectKey + " parts=" + state.Parts.Count + "/" + totalParts +
                        " uploadId=" + LoggingService.ShortenUploadId(state.UploadId) + " (parts preserved).");
                }
                return new MultipartUploadResult(MultipartOutcome.PausedWithStatePreserved, null, state.UploadId, state.Parts.Count);
            }

            VerifyAllPartsPresent(state, totalParts);

            // The last chance to notice that the source changed underneath us. Publishing an
            // object assembled from a file that has since changed would be silently wrong.
            EnsureSourceUnchanged(item);

            string eTag = await CompleteAsync(
                client, bucketName, objectKey, state, retryService, cancellationToken).ConfigureAwait(false);

            _stateStore.Delete(state.StateId);
            item.SetResumableState(false);
            tracker.ReportComplete();

            return new MultipartUploadResult(MultipartOutcome.Completed, eTag, state.UploadId, state.Parts.Count);
        }

        // ---------------------------------------------------------------- state preparation

        private async Task<MultipartUploadState> PrepareStateAsync(
            IAmazonS3 client,
            string bucketName,
            string objectKey,
            UploadQueueItem item,
            string contentType,
            AppSettings settings,
            RetryService retryService,
            string stateId,
            CancellationToken cancellationToken)
        {
            MultipartUploadState existing = _stateStore.Load(stateId);

            if (existing != null)
            {
                string reason;
                if (UploadStateStore.CanResume(existing, item, out reason))
                {
                    MultipartUploadState reconciled = await TryReconcileAsync(
                        client, bucketName, objectKey, existing, item, retryService, cancellationToken).ConfigureAwait(false);

                    if (reconciled != null) return reconciled;
                }
                else
                {
                    if (_log != null) _log.Warning("Multipart.Resume", "Cannot resume key=" + objectKey + ": " + reason);
                    RaiseResumeRejected(item, reason);

                    // The upload ID is still useful even though its parts cannot safely be
                    // combined with the current local file. Abort it before discarding the
                    // local pointer so stale parts are not orphaned until lifecycle cleanup.
                    await AbortAsync(client, bucketName, objectKey, existing.UploadId).ConfigureAwait(false);
                    _stateStore.Delete(stateId);
                }
            }

            long partSize = MultipartCalculator.CalculatePartSize(item.FileSize, settings.PartSizeBytes);

            string uploadId = await retryService.ExecuteAsync(
                "InitiateMultipartUpload",
                async token =>
                {
                    InitiateMultipartUploadRequest request = new InitiateMultipartUploadRequest
                    {
                        BucketName = bucketName,
                        Key = objectKey,
                        ContentType = contentType
                    };

                    InitiateMultipartUploadResponse response =
                        await client.InitiateMultipartUploadAsync(request, token).ConfigureAwait(false);
                    return response.UploadId;
                },
                cancellationToken,
                info => item.IncrementRetryCount()).ConfigureAwait(false);

            MultipartUploadState state = new MultipartUploadState
            {
                StateId = stateId,
                LocalFilePath = item.LocalFilePath,
                FileSize = item.FileSize,
                BucketName = bucketName,
                ObjectKey = objectKey,
                UploadId = uploadId,
                PartSize = partSize,
                ContentType = contentType
            };
            state.LastWriteUtc = item.LastWriteUtc;
            state.CreatedUtc = DateTime.UtcNow;

            item.SetResumableState(_stateStore.Save(state));
            return state;
        }

        /// <summary>
        /// Asks R2 which parts it actually holds and keeps only the ones that match the
        /// expected geometry. Local state is never trusted on its own — R2 is the authority.
        /// Returns null when the upload ID is gone and a fresh upload is needed.
        /// </summary>
        private async Task<MultipartUploadState> TryReconcileAsync(
            IAmazonS3 client,
            string bucketName,
            string objectKey,
            MultipartUploadState state,
            UploadQueueItem item,
            RetryService retryService,
            CancellationToken cancellationToken)
        {
            List<PartDetail> remoteParts;

            try
            {
                remoteParts = await retryService.ExecuteAsync(
                    "ListParts",
                    token => ListAllPartsAsync(client, bucketName, objectKey, state.UploadId, token),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AmazonS3Exception ex) when (
                string.Equals(ex.ErrorCode, "NoSuchUpload", StringComparison.OrdinalIgnoreCase) ||
                (int)ex.StatusCode == 404)
            {
                string reason = "R2 no longer has the interrupted multipart upload, so it cannot be resumed. The file will be uploaded from the start.";
                if (_log != null) _log.Warning("Multipart.Resume", "uploadId=" + LoggingService.ShortenUploadId(state.UploadId) + " no longer exists on R2.");
                RaiseResumeRejected(item, reason);
                _stateStore.Delete(state.StateId);
                return null;
            }

            int expectedPartCount = MultipartCalculator.CalculatePartCount(state.FileSize, state.PartSize);

            List<CompletedPartState> verified = new List<CompletedPartState>();
            foreach (PartDetail remote in remoteParts)
            {
                long expectedLength = MultipartCalculator.GetPartLength(remote.PartNumber, state.PartSize, state.FileSize);

                if (remote.PartNumber < 1 || remote.PartNumber > expectedPartCount) continue;
                if (expectedLength <= 0) continue;

                // A size mismatch means the part was uploaded with a different part size or is
                // truncated; re-uploading it is the only safe option.
                if (remote.Size != expectedLength)
                {
                    if (_log != null)
                    {
                        _log.Warning("Multipart.Resume", string.Format(
                            CultureInfo.InvariantCulture,
                            "Discarding part {0}: R2 reports {1} bytes, expected {2}.",
                            remote.PartNumber, remote.Size, expectedLength));
                    }
                    continue;
                }

                verified.Add(new CompletedPartState(remote.PartNumber, remote.ETag, remote.Size));
            }

            state.Parts = verified;
            state.Parts.Sort(delegate (CompletedPartState a, CompletedPartState b) { return a.PartNumber.CompareTo(b.PartNumber); });
            _stateStore.Save(state);

            if (_log != null)
            {
                _log.Info("Multipart.Resume", string.Format(
                    CultureInfo.InvariantCulture,
                    "Resuming key={0} uploadId={1} with {2}/{3} parts already on R2.",
                    objectKey, LoggingService.ShortenUploadId(state.UploadId), verified.Count, expectedPartCount));
            }

            item.SetResumableState(true);
            return state;
        }

        private static async Task<List<PartDetail>> ListAllPartsAsync(
            IAmazonS3 client, string bucketName, string objectKey, string uploadId, CancellationToken cancellationToken)
        {
            List<PartDetail> all = new List<PartDetail>();
            int marker = 0;

            // R2 returns at most 1000 parts per page, and an upload can have 10,000.
            while (true)
            {
                ListPartsRequest request = new ListPartsRequest
                {
                    BucketName = bucketName,
                    Key = objectKey,
                    UploadId = uploadId,
                    MaxParts = 1000
                };
                if (marker > 0) request.PartNumberMarker = marker.ToString(CultureInfo.InvariantCulture);

                ListPartsResponse response = await client.ListPartsAsync(request, cancellationToken).ConfigureAwait(false);
                if (response.Parts != null) all.AddRange(response.Parts);

                if (!response.IsTruncated) break;

                // Guard against a service that reports truncation without advancing.
                if (response.NextPartNumberMarker <= marker) break;
                marker = response.NextPartNumberMarker;
            }

            return all;
        }

        // ------------------------------------------------------------------- part transfers

        private async Task UploadMissingPartsAsync(
            IAmazonS3 client,
            string bucketName,
            string objectKey,
            UploadQueueItem item,
            MultipartUploadState state,
            int totalParts,
            AppSettings settings,
            RetryService retryService,
            PauseController pauseController,
            ProgressTracker tracker,
            CancellationToken cancellationToken)
        {
            List<int> missing = new List<int>();
            for (int partNumber = 1; partNumber <= totalParts; partNumber++)
            {
                if (!state.HasPart(partNumber)) missing.Add(partNumber);
            }

            if (missing.Count == 0) return;

            int parallel = settings.ParallelParts;
            if (parallel < AppSettings.MinParallelParts) parallel = AppSettings.MinParallelParts;
            if (parallel > AppSettings.MaxParallelParts) parallel = AppSettings.MaxParallelParts;

            using (SemaphoreSlim gate = new SemaphoreSlim(parallel, parallel))
            {
                List<Task> running = new List<Task>();
                object stateLock = new object();

                try
                {
                    foreach (int partNumber in missing)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // A pause stops new parts being scheduled. Parts already in flight run
                        // to completion so their ETags are recorded and can be resumed from.
                        if (pauseController != null && pauseController.IsPaused)
                        {
                            if (tracker != null) tracker.MarkIdle();
                            await pauseController.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);
                        }

                        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

                        int currentPart = partNumber;
                        Task task = Task.Run(async () =>
                        {
                            try
                            {
                                CompletedPartState completed = await UploadPartWithRetryAsync(
                                    client, bucketName, objectKey, item, state, currentPart,
                                    retryService, tracker, cancellationToken).ConfigureAwait(false);

                                lock (stateLock)
                                {
                                    state.SetPart(completed);
                                    if (_stateStore.Save(state))
                                        item.SetResumableState(true);
                                }

                                tracker.ConfirmPart(currentPart, completed.Size);
                            }
                            finally
                            {
                                gate.Release();
                            }
                        }, CancellationToken.None);

                        running.Add(task);

                        // Keep the task list from growing without bound on files with
                        // thousands of parts.
                        if (running.Count >= parallel * 4)
                        {
                            running.RemoveAll(t => t.IsCompleted);
                            foreach (Task finished in running.ToArray())
                            {
                                if (finished.IsFaulted) await finished.ConfigureAwait(false); // rethrow
                            }
                        }
                    }

                    await Task.WhenAll(running).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Let in-flight parts settle before propagating, so their ETags are saved
                    // and the upload stays resumable.
                    try
                    {
                        await Task.WhenAll(running).ConfigureAwait(false);
                    }
                    catch (Exception drainFailure)
                    {
                        if (_log != null) _log.Debug("Multipart.Drain", "A part failed while draining: " + drainFailure.GetType().Name);
                    }
                    throw;
                }
            }
        }

        private static async Task<CompletedPartState> UploadPartWithRetryAsync(
            IAmazonS3 client,
            string bucketName,
            string objectKey,
            UploadQueueItem item,
            MultipartUploadState state,
            int partNumber,
            RetryService retryService,
            ProgressTracker tracker,
            CancellationToken cancellationToken)
        {
            long offset = MultipartCalculator.GetPartOffset(partNumber, state.PartSize);
            long length = MultipartCalculator.GetPartLength(partNumber, state.PartSize, state.FileSize);

            return await retryService.ExecuteAsync(
                "UploadPart",
                async token =>
                {
                    // Each attempt gets its own bounded stream so a retry restarts at the
                    // beginning of the part.
                    tracker.ResetPart(partNumber);

                    using (BoundedFileStream partStream = new BoundedFileStream(item.LocalFilePath, offset, length))
                    {
                        UploadPartRequest request = new UploadPartRequest
                        {
                            BucketName = bucketName,
                            Key = objectKey,
                            UploadId = state.UploadId,
                            PartNumber = partNumber,
                            PartSize = length,
                            InputStream = partStream,

                            // Required for Cloudflare R2: it does not implement the AWS
                            // streaming SigV4 payload signing/checksum flow.
                            DisablePayloadSigning = true,
                            DisableDefaultChecksumValidation = true,
                            UseChunkEncoding = false
                        };

                        ProgressThrottle throttle = new ProgressThrottle(TimeSpan.FromMilliseconds(150));
                        EventHandler<Amazon.Runtime.StreamTransferProgressArgs> handler =
                            delegate (object sender, Amazon.Runtime.StreamTransferProgressArgs args)
                            {
                                tracker.ReportPartBytes(partNumber, args.TransferredBytes, throttle.ShouldReport());
                            };

                        request.StreamTransferProgress += handler;

                        try
                        {
                            UploadPartResponse response = await client.UploadPartAsync(request, token).ConfigureAwait(false);

                            if (string.IsNullOrEmpty(response.ETag))
                            {
                                throw new AmazonS3Exception(
                                    "R2 accepted part " + partNumber + " but returned no ETag, so the upload cannot be completed.");
                            }

                            return new CompletedPartState(partNumber, response.ETag, length);
                        }
                        finally
                        {
                            request.StreamTransferProgress -= handler;
                        }
                    }
                },
                cancellationToken,
                info =>
                {
                    item.IncrementRetryCount();
                    tracker.ResetPart(partNumber);
                }).ConfigureAwait(false);
        }

        // ------------------------------------------------------------------ finish / cancel

        private async Task<string> CompleteAsync(
            IAmazonS3 client,
            string bucketName,
            string objectKey,
            MultipartUploadState state,
            RetryService retryService,
            CancellationToken cancellationToken)
        {
            List<PartETag> partETags = new List<PartETag>(state.Parts.Count);
            foreach (CompletedPartState part in state.Parts)
            {
                partETags.Add(new PartETag(part.PartNumber, part.ETag));
            }

            // S3/R2 requires the parts in ascending part-number order.
            partETags.Sort(delegate (PartETag a, PartETag b) { return a.PartNumber.CompareTo(b.PartNumber); });

            CompleteMultipartUploadResponse response = await retryService.ExecuteAsync(
                "CompleteMultipartUpload",
                async token =>
                {
                    CompleteMultipartUploadRequest request = new CompleteMultipartUploadRequest
                    {
                        BucketName = bucketName,
                        Key = objectKey,
                        UploadId = state.UploadId,
                        PartETags = partETags
                    };
                    return await client.CompleteMultipartUploadAsync(request, token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            if (_log != null)
            {
                _log.Info("Multipart.Complete", string.Format(
                    CultureInfo.InvariantCulture,
                    "key={0} parts={1} uploadId={2} http={3}",
                    objectKey, partETags.Count, LoggingService.ShortenUploadId(state.UploadId),
                    (int)response.HttpStatusCode));
            }

            return response.ETag;
        }

        private async Task<MultipartUploadResult> HandleCancellationAsync(
            IAmazonS3 client,
            string bucketName,
            string objectKey,
            UploadQueueItem item,
            MultipartUploadState state)
        {
            if (item.PreservePartsOnCancel)
            {
                bool saved = _stateStore.Save(state);
                item.SetResumableState(saved);

                if (saved)
                {
                    if (_log != null)
                    {
                        _log.Info("Multipart.Cancel",
                            "key=" + objectKey + " cancelled; " + state.Parts.Count +
                            " part(s) kept for resume (uploadId=" + LoggingService.ShortenUploadId(state.UploadId) + ").");
                    }

                    return new MultipartUploadResult(MultipartOutcome.CancelledWithStatePreserved, null, state.UploadId, state.Parts.Count);
                }

                if (_log != null)
                {
                    _log.Warning(
                        "Multipart.Cancel",
                        "Resume state could not be saved; aborting the incomplete upload instead of claiming it can be resumed.");
                }
            }

            await AbortAsync(client, bucketName, objectKey, state.UploadId).ConfigureAwait(false);
            _stateStore.Delete(state.StateId);
            item.SetResumableState(false);

            return new MultipartUploadResult(MultipartOutcome.CancelledAndAborted, null, state.UploadId, 0);
        }

        /// <summary>
        /// Aborts an incomplete multipart upload so its parts stop occupying the bucket.
        /// Failures here are logged but never propagated: aborting is cleanup, and the caller
        /// is already handling a cancellation or a failure.
        /// </summary>
        public async Task AbortAsync(IAmazonS3 client, string bucketName, string objectKey, string uploadId)
        {
            if (client == null || string.IsNullOrEmpty(uploadId)) return;

            try
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    await client.AbortMultipartUploadAsync(
                        new AbortMultipartUploadRequest
                        {
                            BucketName = bucketName,
                            Key = objectKey,
                            UploadId = uploadId
                        },
                        timeout.Token).ConfigureAwait(false);
                }

                if (_log != null)
                {
                    _log.Info("Multipart.Abort",
                        "Aborted uploadId=" + LoggingService.ShortenUploadId(uploadId) + " for key=" + objectKey + ".");
                }
            }
            catch (Exception ex)
            {
                if (_log != null)
                {
                    _log.Warning("Multipart.Abort",
                        "Could not abort uploadId=" + LoggingService.ShortenUploadId(uploadId) +
                        " (" + ex.GetType().Name + ": " + LoggingService.Sanitize(ex.Message) +
                        "). Incomplete parts may remain until the bucket lifecycle removes them.");
                }
            }
        }

        /// <summary>Deletes the saved resume state for a (file, bucket, key) triple.</summary>
        public void DiscardState(string localFilePath, string bucketName, string objectKey)
        {
            _stateStore.Delete(UploadStateStore.BuildStateId(localFilePath, bucketName, objectKey));
        }

        // ------------------------------------------------------------------------- checks

        private static void VerifyAllPartsPresent(MultipartUploadState state, int totalParts)
        {
            if (state.Parts.Count == totalParts) return;

            List<int> missing = new List<int>();
            for (int partNumber = 1; partNumber <= totalParts && missing.Count < 10; partNumber++)
            {
                if (!state.HasPart(partNumber)) missing.Add(partNumber);
            }

            throw new InvalidOperationException(string.Format(
                CultureInfo.CurrentCulture,
                "The multipart upload cannot be completed: {0} of {1} parts are missing (first missing: {2}).",
                totalParts - state.Parts.Count,
                totalParts,
                missing.Count > 0 ? string.Join(", ", missing.ConvertAll(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()) : "unknown"));
        }

        private static void EnsureSourceUnchanged(UploadQueueItem item)
        {
            string problem;
            if (item.TryRefreshFileInfo(out problem)) return;

            throw new IOException(
                "The upload was not completed because the source file is no longer the one that was uploaded: " + problem);
        }

        private void RaiseResumeRejected(UploadQueueItem item, string reason)
        {
            EventHandler<ResumeRejectedEventArgs> handler = ResumeRejected;
            if (handler != null) handler(this, new ResumeRejectedEventArgs(item, reason));
        }

        // ------------------------------------------------------------- progress aggregation

        /// <summary>
        /// Combines per-part byte counts into one accurate file-level figure.
        /// Confirmed parts contribute their full size; in-flight parts contribute what has
        /// been sent so far. Guarded by a lock because several part tasks report at once.
        /// </summary>
        private sealed class ProgressTracker
        {
            private readonly object _sync = new object();
            private readonly Dictionary<int, long> _inFlight = new Dictionary<int, long>();
            private readonly long _totalBytes;
            private readonly int _totalParts;
            private readonly IProgress<UploadProgressInfo> _progress;
            private readonly SpeedEstimator _speed;

            private long _confirmedBytes;
            private int _confirmedParts;

            public ProgressTracker(long totalBytes, int totalParts, IProgress<UploadProgressInfo> progress, SpeedEstimator speed)
            {
                _totalBytes = totalBytes;
                _totalParts = totalParts;
                _progress = progress;
                _speed = speed;
            }

            public void SetConfirmed(List<CompletedPartState> parts)
            {
                lock (_sync)
                {
                    _confirmedBytes = 0;
                    _confirmedParts = 0;
                    if (parts == null) return;

                    foreach (CompletedPartState part in parts)
                    {
                        _confirmedBytes += part.Size;
                        _confirmedParts++;
                    }
                }
            }

            public void ReportPartBytes(int partNumber, long transferred, bool shouldReport)
            {
                lock (_sync) { _inFlight[partNumber] = transferred; }
                if (shouldReport) Report(false);
            }

            public void ResetPart(int partNumber)
            {
                lock (_sync) { _inFlight[partNumber] = 0; }
            }

            public void ConfirmPart(int partNumber, long size)
            {
                lock (_sync)
                {
                    _inFlight.Remove(partNumber);
                    _confirmedBytes += size;
                    _confirmedParts++;
                }
                Report(true);
            }

            public void MarkIdle()
            {
                if (_speed != null) _speed.MarkIdle();
                Report(true);
            }

            public void Report(bool force)
            {
                if (_progress == null) return;

                long transferred;
                int completedParts;

                lock (_sync)
                {
                    transferred = _confirmedBytes;
                    foreach (KeyValuePair<int, long> entry in _inFlight) transferred += entry.Value;
                    completedParts = _confirmedParts;
                }

                if (transferred > _totalBytes) transferred = _totalBytes;

                if (_speed != null) _speed.Report(transferred);

                _progress.Report(new UploadProgressInfo(
                    transferred,
                    _totalBytes,
                    _speed == null ? 0 : _speed.BytesPerSecond,
                    completedParts,
                    _totalParts));
            }

            public void ReportComplete()
            {
                if (_progress == null) return;
                _progress.Report(new UploadProgressInfo(
                    _totalBytes, _totalBytes,
                    _speed == null ? 0 : _speed.BytesPerSecond,
                    _totalParts, _totalParts));
            }
        }
    }

    /// <summary>Carries the reason a resumable upload had to be restarted.</summary>
    public sealed class ResumeRejectedEventArgs : EventArgs
    {
        public ResumeRejectedEventArgs(UploadQueueItem item, string reason)
        {
            Item = item;
            Reason = reason;
        }

        public UploadQueueItem Item { get; private set; }
        public string Reason { get; private set; }
    }
}
