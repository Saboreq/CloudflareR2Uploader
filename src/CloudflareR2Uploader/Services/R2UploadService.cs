using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>Describes an existing object so the user can decide what to do about it.</summary>
    public sealed class ExistingObjectInfo
    {
        public ExistingObjectInfo(string key, long length, DateTime lastModifiedUtc)
        {
            Key = key;
            Length = length;
            LastModifiedUtc = lastModifiedUtc;
        }

        public string Key { get; private set; }
        public long Length { get; private set; }
        public DateTime LastModifiedUtc { get; private set; }
    }

    /// <summary>Asks the user what to do about a key that already exists.</summary>
    public interface IOverwritePrompt
    {
        Task<OverwriteDecision> AskAsync(UploadQueueItem item, ExistingObjectInfo existing);
    }

    /// <summary>
    /// Uploads one file: picks the strategy, applies the overwrite policy, verifies the
    /// result with <c>HeadObject</c>, and converts any failure into a message a person can act on.
    /// </summary>
    public sealed class R2UploadService
    {
        private const int MaxRenameAttempts = 999;

        private readonly ILoggingService _log;
        private readonly UploadStateStore _stateStore;
        private readonly R2SinglePartUploader _singlePartUploader;
        private readonly R2MultipartUploader _multipartUploader;

        public R2UploadService(ILoggingService log, UploadStateStore stateStore)
        {
            _log = log;
            _stateStore = stateStore;
            _singlePartUploader = new R2SinglePartUploader(log);
            _multipartUploader = new R2MultipartUploader(log, stateStore);
        }

        public R2MultipartUploader MultipartUploader { get { return _multipartUploader; } }

        public async Task<UploadResult> UploadAsync(
            IAmazonS3 client,
            UploadQueueItem item,
            AppSettings settings,
            R2Credentials credentials,
            IOverwritePrompt overwritePrompt,
            PauseController pauseController,
            IProgress<UploadProgressInfo> progress,
            CancellationToken cancellationToken)
        {
            if (client == null) throw new ArgumentNullException("client");
            if (item == null) throw new ArgumentNullException("item");
            if (settings == null) throw new ArgumentNullException("settings");

            string objectKey = item.ObjectKey;
            SpeedEstimator speedEstimator = new SpeedEstimator();
            RetryService retryService = new RetryService(_log, settings.RetryAttempts);
            bool usedMultipart = false;

            try
            {
                item.SetStatus(UploadItemStatus.Preparing);

                string keyProblem;
                if (!ObjectKeyUtility.TryValidate(objectKey, out keyProblem))
                {
                    return Fail(item, "The destination object key is not valid", keyProblem, settings, null, false);
                }

                // Confirm the file is still exactly what was queued before touching the network.
                string fileProblem;
                if (!item.TryRefreshFileInfo(out fileProblem))
                {
                    return Fail(item, "The file cannot be uploaded", fileProblem, settings, null, false);
                }

                // Apply the overwrite policy.
                OverwriteResolution resolution = await ResolveOverwriteAsync(
                    client, item, settings, objectKey, overwritePrompt, cancellationToken).ConfigureAwait(false);

                if (resolution.Decision == OverwriteDecision.Skip)
                {
                    item.SetStatus(UploadItemStatus.Skipped, "An object with this key already exists.");
                    return UploadResult.Skipped(objectKey, "Skipped: an object with this key already exists in the bucket.");
                }

                if (resolution.Decision == OverwriteDecision.CancelAll)
                {
                    item.SetStatus(UploadItemStatus.Cancelled, "Cancelled at the overwrite prompt.");
                    return UploadResult.Cancelled(objectKey, false);
                }

                objectKey = resolution.ObjectKey;
                if (!string.Equals(objectKey, item.ObjectKey, StringComparison.Ordinal)) item.ObjectKey = objectKey;

                string contentType = MimeTypeService.GetContentType(item.LocalFilePath);
                usedMultipart = item.FileSize >= settings.MultipartThresholdBytes;

                item.SetStatus(UploadItemStatus.Uploading, usedMultipart ? "Multipart upload" : "Single request");
                speedEstimator.Start(0);

                // A pause requested during preparation or an overwrite prompt must prevent a
                // new network transfer from starting. An already-running single PUT still
                // has to finish, but a not-yet-started one can wait safely here.
                if (pauseController != null)
                    await pauseController.WaitWhilePausedAsync(cancellationToken).ConfigureAwait(false);

                string eTag;

                if (usedMultipart)
                {
                    MultipartUploadResult multipart = await _multipartUploader.UploadAsync(
                        client, settings.BucketName, objectKey, item, contentType, settings,
                        retryService, pauseController, progress, speedEstimator, cancellationToken).ConfigureAwait(false);

                    switch (multipart.Outcome)
                    {
                        case MultipartOutcome.PausedWithStatePreserved:
                            item.SetStatus(UploadItemStatus.Paused,
                                multipart.PartCount + " part(s) uploaded and kept.");
                            return UploadResult.Paused(objectKey);

                        case MultipartOutcome.CancelledWithStatePreserved:
                            item.SetStatus(UploadItemStatus.Cancelled,
                                multipart.PartCount + " part(s) kept for resume.");
                            return UploadResult.Cancelled(objectKey, true);

                        case MultipartOutcome.CancelledAndAborted:
                            item.SetStatus(UploadItemStatus.Cancelled, "Incomplete parts were removed from the bucket.");
                            return UploadResult.Cancelled(objectKey, false);
                    }

                    eTag = multipart.ETag;
                }
                else
                {
                    SinglePartUploadResult single = await _singlePartUploader.UploadAsync(
                        client, settings.BucketName, objectKey, item, contentType,
                        retryService, progress, speedEstimator, cancellationToken).ConfigureAwait(false);

                    eTag = single.ETag;
                }

                speedEstimator.Stop();
                item.SetElapsed(speedEstimator.Elapsed);

                // Verify against R2 rather than trusting the upload call's own success.
                item.SetStatus(UploadItemStatus.Verifying);
                return await VerifyAsync(client, item, settings, objectKey, eTag, usedMultipart, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                speedEstimator.Stop();

                bool preserved = usedMultipart && item.PreservePartsOnCancel;
                item.SetStatus(UploadItemStatus.Cancelled,
                    preserved ? "Uploaded parts were kept for resume." : null);
                return UploadResult.Cancelled(objectKey, preserved);
            }
            catch (Exception ex)
            {
                speedEstimator.Stop();

                FriendlyError friendly = FriendlyErrorService.Describe(ex, settings, objectKey);

                if (_log != null)
                {
                    _log.HttpFailure(
                        usedMultipart ? "Upload.Multipart" : "Upload.Single",
                        TransientErrorClassifier.GetStatusCode(ex),
                        TransientErrorClassifier.GetErrorCode(ex),
                        friendly.Headline + " — " + LoggingService.Sanitize(ex.Message),
                        objectKey,
                        null);
                }

                bool resumePreserved = usedMultipart && _stateStore.Load(
                    UploadStateStore.BuildStateId(item.LocalFilePath, settings.BucketName, objectKey)) != null;

                item.SetFailure(friendly.FullMessage, friendly.TechnicalDetails);
                return UploadResult.Failure(objectKey, friendly.FullMessage, friendly.TechnicalDetails, friendly.IsRetryable, resumePreserved);
            }
        }

        // ------------------------------------------------------------------- verification

        private async Task<UploadResult> VerifyAsync(
            IAmazonS3 client,
            UploadQueueItem item,
            AppSettings settings,
            string objectKey,
            string uploadETag,
            bool wasMultipart,
            CancellationToken cancellationToken)
        {
            try
            {
                GetObjectMetadataResponse head = await client.GetObjectMetadataAsync(
                    new GetObjectMetadataRequest { BucketName = settings.BucketName, Key = objectKey },
                    cancellationToken).ConfigureAwait(false);

                long remoteLength = head.ContentLength;

                if (remoteLength != item.FileSize)
                {
                    string detail = string.Format(
                        CultureInfo.CurrentCulture,
                        "R2 stored {0} but the local file is {1}. The object was not the expected size, so it should not be treated as a good copy.",
                        FileSizeFormatter.Format(remoteLength),
                        FileSizeFormatter.Format(item.FileSize));

                    if (_log != null)
                    {
                        _log.Error("Upload.Verify",
                            "Size mismatch for key=" + objectKey + ": remote=" + remoteLength + " local=" + item.FileSize, null);
                    }

                    return Fail(item, "The uploaded object does not match the local file", detail, settings, objectKey, true);
                }

                string eTag = !string.IsNullOrEmpty(head.ETag) ? head.ETag : uploadETag;
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
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                FriendlyError friendly = FriendlyErrorService.Describe(ex, settings, objectKey);
                string detail = "The upload finished but the object could not be confirmed afterwards. " + friendly.Detail;

                if (_log != null) _log.Error("Upload.Verify", "HeadObject failed for key=" + objectKey + ".", ex);

                return Fail(item, "The upload could not be verified", detail, settings, objectKey, true);
            }
        }

        // ------------------------------------------------------------------ overwrite policy

        private sealed class OverwriteResolution
        {
            public OverwriteResolution(OverwriteDecision decision, string objectKey)
            {
                Decision = decision;
                ObjectKey = objectKey;
            }

            public OverwriteDecision Decision { get; private set; }
            public string ObjectKey { get; private set; }
        }

        private async Task<OverwriteResolution> ResolveOverwriteAsync(
            IAmazonS3 client,
            UploadQueueItem item,
            AppSettings settings,
            string objectKey,
            IOverwritePrompt overwritePrompt,
            CancellationToken cancellationToken)
        {
            // Overwriting unconditionally needs no lookup at all.
            if (settings.OverwriteBehavior == OverwriteBehavior.Overwrite)
                return new OverwriteResolution(OverwriteDecision.Overwrite, objectKey);

            ExistingObjectInfo existing = await TryHeadAsync(client, settings.BucketName, objectKey, cancellationToken).ConfigureAwait(false);
            if (existing == null) return new OverwriteResolution(OverwriteDecision.Overwrite, objectKey);

            switch (settings.OverwriteBehavior)
            {
                case OverwriteBehavior.Skip:
                    return new OverwriteResolution(OverwriteDecision.Skip, objectKey);

                case OverwriteBehavior.Rename:
                    return new OverwriteResolution(
                        OverwriteDecision.Rename,
                        await FindFreeKeyAsync(client, settings.BucketName, objectKey, cancellationToken).ConfigureAwait(false));

                case OverwriteBehavior.Ask:
                default:
                    if (overwritePrompt == null)
                    {
                        // Without a way to ask, skipping is the choice that cannot destroy data.
                        return new OverwriteResolution(OverwriteDecision.Skip, objectKey);
                    }

                    OverwriteDecision decision = await overwritePrompt.AskAsync(item, existing).ConfigureAwait(false);

                    if (decision == OverwriteDecision.Rename)
                    {
                        return new OverwriteResolution(
                            decision,
                            await FindFreeKeyAsync(client, settings.BucketName, objectKey, cancellationToken).ConfigureAwait(false));
                    }

                    return new OverwriteResolution(decision, objectKey);
            }
        }

        /// <summary>Finds the first "name (n).ext" variant that does not exist yet.</summary>
        private async Task<string> FindFreeKeyAsync(
            IAmazonS3 client, string bucketName, string objectKey, CancellationToken cancellationToken)
        {
            for (int index = 1; index <= MaxRenameAttempts; index++)
            {
                string candidate = ObjectKeyUtility.AppendDuplicateSuffix(objectKey, index);
                ExistingObjectInfo existing = await TryHeadAsync(client, bucketName, candidate, cancellationToken).ConfigureAwait(false);
                if (existing == null) return candidate;
            }

            throw new InvalidOperationException(
                "No free name was found for \"" + objectKey + "\" after " + MaxRenameAttempts + " attempts.");
        }

        /// <summary>Returns null when the object does not exist. Other errors propagate.</summary>
        private static async Task<ExistingObjectInfo> TryHeadAsync(
            IAmazonS3 client, string bucketName, string objectKey, CancellationToken cancellationToken)
        {
            try
            {
                GetObjectMetadataResponse response = await client.GetObjectMetadataAsync(
                    new GetObjectMetadataRequest { BucketName = bucketName, Key = objectKey },
                    cancellationToken).ConfigureAwait(false);

                return new ExistingObjectInfo(objectKey, response.ContentLength, response.LastModified.ToUniversalTime());
            }
            catch (AmazonS3Exception ex) when ((int)ex.StatusCode == 404 ||
                                               string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(ex.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        private UploadResult Fail(
            UploadQueueItem item, string headline, string detail, AppSettings settings, string objectKey, bool retryable)
        {
            string message = headline + ". " + detail;
            string technical = FriendlyErrorService.BuildTechnicalDetails(null, settings, objectKey ?? item.ObjectKey);

            if (_log != null) _log.Error("Upload", message, null);

            item.SetFailure(message, technical);
            return UploadResult.Failure(objectKey ?? item.ObjectKey, message, technical, retryable, false);
        }
    }
}
