using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>Read/write R2 operations explicitly initiated from the Files context menu.</summary>
    public sealed class R2ObjectOperationService
    {
        private const int ListPageSize = 1000;
        private const int DeleteBatchSize = 1000;
        private const long SingleCopyLimit = (5L * MultipartCalculator.GiB) - (5L * MultipartCalculator.MiB);
        private const int MaxCopyNameAttempts = 999;

        private readonly ILoggingService _log;
        private readonly R2UploadService _uploadService;
        private readonly Func<AppSettings, R2Credentials, AmazonS3Client> _clientFactory = R2ClientFactory.CreateClient;

        public R2ObjectOperationService(ILoggingService log, UploadStateStore stateStore)
        {
            _log = log;
            _uploadService = new R2UploadService(log, stateStore);
        }

        public async Task DownloadAsync(
            AppSettings settings,
            R2Credentials credentials,
            string objectKey,
            string destinationPath,
            IProgress<R2ObjectOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(destinationPath)) throw new ArgumentException("A destination path is required.", nameof(destinationPath));

            string directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                throw new DirectoryNotFoundException("The selected download folder no longer exists.");

            string temporaryPath = Path.Combine(
                directory,
                "." + Path.GetFileName(destinationPath) + ".r2download-" + Guid.NewGuid().ToString("N") + ".tmp");
            string backupPath = temporaryPath + ".backup";

            AmazonS3Client client = null;
            try
            {
                client = _clientFactory(settings, credentials);
                using (GetObjectResponse response = await client.GetObjectAsync(
                    new GetObjectRequest { BucketName = settings.BucketName, Key = objectKey },
                    cancellationToken).ConfigureAwait(false))
                using (FileStream output = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await CopyStreamAsync(
                        response.ResponseStream,
                        output,
                        objectKey,
                        response.ContentLength,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(destinationPath))
                {
                    File.Replace(temporaryPath, destinationPath, backupPath, true);
                    TryDeleteLocalFile(backupPath);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }

                LogSuccess("Browser.Download", settings, objectKey, null);
            }
            catch
            {
                TryDeleteLocalFile(temporaryPath);
                throw;
            }
            finally
            {
                if (client != null) client.Dispose();
            }
        }

        public async Task<UploadResult> OverwriteAsync(
            AppSettings settings,
            R2Credentials credentials,
            string objectKey,
            string localFilePath,
            IProgress<R2ObjectOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            FileInfo file = new FileInfo(localFilePath);
            if (!file.Exists) throw new FileNotFoundException("The selected replacement file no longer exists.", localFilePath);

            AppSettings uploadSettings = settings.Clone();
            uploadSettings.OverwriteBehavior = OverwriteBehavior.Overwrite;
            UploadQueueItem item = new UploadQueueItem(
                file.FullName,
                file.Name,
                file.Length,
                file.LastWriteTimeUtc)
            {
                ObjectKey = objectKey
            };

            IProgress<UploadProgressInfo> uploadProgress = progress == null
                ? null
                : new Progress<UploadProgressInfo>(value => progress.Report(
                    new R2ObjectOperationProgress(
                        "Overwriting",
                        0,
                        objectKey,
                        value.TransferredBytes,
                        value.TotalBytes)));

            AmazonS3Client client = null;
            using (PauseController pause = new PauseController())
            {
                try
                {
                    client = _clientFactory(uploadSettings, credentials);
                    return await _uploadService.UploadAsync(
                        client,
                        item,
                        uploadSettings,
                        credentials,
                        null,
                        pause,
                        uploadProgress,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    if (client != null) client.Dispose();
                }
            }
        }

        public async Task<R2ObjectOperationResult> DeleteAsync(
            AppSettings settings,
            R2Credentials credentials,
            R2BrowserItem item,
            IProgress<R2ObjectOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            AmazonS3Client client = null;
            try
            {
                client = _clientFactory(settings, credentials);
                if (!item.IsFolder)
                {
                    await DeleteKeyAsync(client, settings, item.Key, cancellationToken).ConfigureAwait(false);
                    Report(progress, "Deleting", 1, item.Key, item.Size, item.Size);
                    LogSuccess("Browser.Delete", settings, item.Key, null);
                    return new R2ObjectOperationResult(1, item.Size, null);
                }

                int deleted = 0;
                long bytes = 0;
                while (true)
                {
                    ListObjectsV2Response page = await client.ListObjectsV2Async(
                        new ListObjectsV2Request
                        {
                            BucketName = settings.BucketName,
                            Prefix = item.Prefix,
                            MaxKeys = ListPageSize
                        },
                        cancellationToken).ConfigureAwait(false);

                    if (page.S3Objects == null || page.S3Objects.Count == 0) break;

                    List<KeyVersion> keys = new List<KeyVersion>();
                    foreach (S3Object source in page.S3Objects)
                    {
                        keys.Add(new KeyVersion { Key = source.Key });
                        bytes += source.Size;
                    }

                    await DeleteBatchAsync(client, settings, keys, cancellationToken).ConfigureAwait(false);
                    deleted += keys.Count;
                    Report(progress, "Deleting folder", deleted, item.Prefix, bytes, 0);
                }

                LogSuccess("Browser.DeleteFolder", settings, item.Prefix, null);
                return new R2ObjectOperationResult(deleted, bytes, null);
            }
            finally
            {
                if (client != null) client.Dispose();
            }
        }

        public async Task<R2ObjectOperationResult> CopyAsync(
            AppSettings settings,
            R2Credentials credentials,
            R2BrowserItem item,
            string destination,
            bool deleteSourceAfterCopy,
            IProgress<R2ObjectOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            if (item.IsFolder && R2ObjectOperationPathUtility.IsSameOrDescendantPrefix(item.Prefix, destination))
                throw new InvalidOperationException("A folder cannot be copied or moved into itself or one of its descendants.");
            if (!item.IsFolder && string.Equals(item.Key, destination, StringComparison.Ordinal))
                throw new InvalidOperationException("The source and destination object keys are the same.");

            AmazonS3Client client = null;
            try
            {
                client = _clientFactory(settings, credentials);
                RetryService retry = new RetryService(_log, settings.RetryAttempts);

                if (!item.IsFolder)
                {
                    await CopyOneAsync(
                        client, retry, settings, item.Key, destination, item.Size, item.ETag,
                        progress, 0, cancellationToken).ConfigureAwait(false);

                    if (deleteSourceAfterCopy)
                    {
                        await EnsureSourceUnchangedAsync(
                            client, settings, item.Key, item.Size, item.ETag, cancellationToken).ConfigureAwait(false);
                        await DeleteKeyAsync(client, settings, item.Key, cancellationToken).ConfigureAwait(false);
                    }

                    LogSuccess(deleteSourceAfterCopy ? "Browser.Move" : "Browser.Copy", settings, item.Key, destination);
                    return new R2ObjectOperationResult(1, item.Size, destination);
                }

                int copied = 0;
                long copiedBytes = 0;
                List<SourceSnapshot> sourceSnapshots = new List<SourceSnapshot>();
                string continuationToken = null;
                do
                {
                    ListObjectsV2Response page = await client.ListObjectsV2Async(
                        new ListObjectsV2Request
                        {
                            BucketName = settings.BucketName,
                            Prefix = item.Prefix,
                            MaxKeys = ListPageSize,
                            ContinuationToken = continuationToken
                        },
                        cancellationToken).ConfigureAwait(false);

                    if (page.S3Objects != null)
                    {
                        foreach (S3Object source in page.S3Objects)
                        {
                            string relative = source.Key.StartsWith(item.Prefix, StringComparison.Ordinal)
                                ? source.Key.Substring(item.Prefix.Length)
                                : source.Key;
                            string targetKey = destination + relative;

                            await CopyOneAsync(
                                client, retry, settings, source.Key, targetKey, source.Size, source.ETag,
                                progress, copied, cancellationToken).ConfigureAwait(false);
                            sourceSnapshots.Add(new SourceSnapshot(source.Key, source.Size, source.ETag));
                            copied++;
                            copiedBytes += source.Size;
                            Report(progress, deleteSourceAfterCopy ? "Moving folder" : "Copying folder",
                                copied, source.Key, copiedBytes, 0);
                        }
                    }

                    continuationToken = page.IsTruncated ? page.NextContinuationToken : null;
                }
                while (!string.IsNullOrEmpty(continuationToken));

                if (deleteSourceAfterCopy)
                {
                    await EnsurePrefixUnchangedAsync(
                        client, settings, item.Prefix, sourceSnapshots, cancellationToken).ConfigureAwait(false);
                    await DeleteSnapshotsAsync(
                        client, settings, sourceSnapshots, progress, cancellationToken).ConfigureAwait(false);
                }

                LogSuccess(deleteSourceAfterCopy ? "Browser.MoveFolder" : "Browser.CopyFolder", settings, item.Prefix, destination);
                return new R2ObjectOperationResult(copied, copiedBytes, destination);
            }
            finally
            {
                if (client != null) client.Dispose();
            }
        }

        public async Task<string> FindAvailableCopyDestinationAsync(
            AppSettings settings,
            R2Credentials credentials,
            string proposedDestination,
            bool isFolder,
            CancellationToken cancellationToken)
        {
            AmazonS3Client client = null;
            try
            {
                client = _clientFactory(settings, credentials);
                if (!await NameExistsAsync(
                    client, settings.BucketName, proposedDestination, isFolder, cancellationToken).ConfigureAwait(false))
                    return proposedDestination;

                for (int index = 1; index <= MaxCopyNameAttempts; index++)
                {
                    string candidate = R2ObjectOperationPathUtility.AppendCopySuffix(proposedDestination, isFolder, index);
                    if (!await NameExistsAsync(
                        client, settings.BucketName, candidate, isFolder, cancellationToken).ConfigureAwait(false))
                        return candidate;
                }

                throw new InvalidOperationException("No free destination name was found after 999 attempts.");
            }
            finally
            {
                if (client != null) client.Dispose();
            }
        }

        public async Task<bool> DestinationExistsAsync(
            AppSettings settings,
            R2Credentials credentials,
            string destination,
            bool isFolder,
            CancellationToken cancellationToken)
        {
            AmazonS3Client client = null;
            try
            {
                client = _clientFactory(settings, credentials);
                return await ExistsAsync(client, settings.BucketName, destination, isFolder, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (client != null) client.Dispose();
            }
        }

        public async Task<bool> NameExistsAsync(
            AppSettings settings,
            R2Credentials credentials,
            string destination,
            bool isFolder,
            CancellationToken cancellationToken)
        {
            AmazonS3Client client = null;
            try
            {
                client = _clientFactory(settings, credentials);
                return await NameExistsAsync(
                    client, settings.BucketName, destination, isFolder, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (client != null) client.Dispose();
            }
        }

        public async Task CreateFolderAsync(
            AppSettings settings,
            R2Credentials credentials,
            string folderPrefix,
            IProgress<R2ObjectOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            string normalized = R2BrowserPathUtility.NormalizePrefix(folderPrefix);
            string keyBody = normalized.TrimEnd('/');
            string reason = null;
            if (keyBody.Length == 0 || !ObjectKeyUtility.TryValidate(keyBody, out reason))
            {
                throw new InvalidOperationException(
                    keyBody.Length == 0 ? "A folder name is required." : reason);
            }

            AmazonS3Client client = null;
            try
            {
                client = _clientFactory(settings, credentials);
                if (await NameExistsAsync(
                    client, settings.BucketName, normalized, true, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("A file or folder with that name already exists here.");
                }

                RetryService retry = new RetryService(_log, settings.RetryAttempts);
                await retry.ExecuteAsync(
                    "PutFolderMarker",
                    async token =>
                    {
                        using (MemoryStream content = new MemoryStream(Array.Empty<byte>(), false))
                        {
                            PutObjectRequest request = new PutObjectRequest
                            {
                                BucketName = settings.BucketName,
                                Key = normalized,
                                InputStream = content,
                                ContentType = "application/x-directory",
                                AutoCloseStream = false,
                                AutoResetStreamPosition = false,
                                DisablePayloadSigning = true,
                                DisableDefaultChecksumValidation = true,
                                UseChunkEncoding = false
                            };
                            await client.PutObjectAsync(request, token).ConfigureAwait(false);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);

                Report(progress, "Creating folder", 1, normalized, 0, 0);
                LogSuccess("Browser.CreateFolder", settings, normalized, null);
            }
            finally
            {
                if (client != null) client.Dispose();
            }
        }

        private async Task CopyOneAsync(
            AmazonS3Client client,
            RetryService retry,
            AppSettings settings,
            string sourceKey,
            string destinationKey,
            long size,
            string sourceETag,
            IProgress<R2ObjectOperationProgress> progress,
            int completedObjects,
            CancellationToken cancellationToken)
        {
            if (size <= SingleCopyLimit)
            {
                await retry.ExecuteAsync(
                    "CopyObject",
                    async token => await client.CopyObjectAsync(
                        new CopyObjectRequest
                        {
                            SourceBucket = settings.BucketName,
                            SourceKey = sourceKey,
                            DestinationBucket = settings.BucketName,
                            DestinationKey = destinationKey,
                            ETagToMatch = sourceETag
                        },
                        token).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await MultipartCopyAsync(
                    client, retry, settings, sourceKey, destinationKey, size,
                    progress, completedObjects, cancellationToken).ConfigureAwait(false);
            }

            GetObjectMetadataResponse verification = await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = settings.BucketName, Key = destinationKey },
                cancellationToken).ConfigureAwait(false);
            if (verification.ContentLength != size)
                throw new IOException("The copied object size does not match its source.");
        }

        private async Task MultipartCopyAsync(
            AmazonS3Client client,
            RetryService retry,
            AppSettings settings,
            string sourceKey,
            string destinationKey,
            long size,
            IProgress<R2ObjectOperationProgress> progress,
            int completedObjects,
            CancellationToken cancellationToken)
        {
            GetObjectMetadataResponse sourceMetadata = await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = settings.BucketName, Key = sourceKey },
                cancellationToken).ConfigureAwait(false);

            InitiateMultipartUploadRequest initiateRequest = new InitiateMultipartUploadRequest
            {
                BucketName = settings.BucketName,
                Key = destinationKey,
                ContentType = sourceMetadata.Headers.ContentType
            };
            if (sourceMetadata.Metadata != null)
            {
                foreach (string metadataKey in sourceMetadata.Metadata.Keys)
                    initiateRequest.Metadata.Add(metadataKey, sourceMetadata.Metadata[metadataKey]);
            }

            InitiateMultipartUploadResponse initiated = await client.InitiateMultipartUploadAsync(
                initiateRequest,
                cancellationToken).ConfigureAwait(false);

            bool completed = false;
            try
            {
                long partSize = MultipartCalculator.CalculatePartSize(size, settings.PartSizeBytes);
                int partCount = MultipartCalculator.CalculatePartCount(size, partSize);
                List<PartETag> partETags = new List<PartETag>(partCount);

                for (int partNumber = 1; partNumber <= partCount; partNumber++)
                {
                    long firstByte = MultipartCalculator.GetPartOffset(partNumber, partSize);
                    long length = MultipartCalculator.GetPartLength(partNumber, partSize, size);
                    long lastByte = firstByte + length - 1;

                    CopyPartResponse part = await retry.ExecuteAsync(
                        "UploadPartCopy",
                        async token => await client.CopyPartAsync(
                            new CopyPartRequest
                            {
                                SourceBucket = settings.BucketName,
                                SourceKey = sourceKey,
                                DestinationBucket = settings.BucketName,
                                DestinationKey = destinationKey,
                                UploadId = initiated.UploadId,
                                PartNumber = partNumber,
                                FirstByte = firstByte,
                                LastByte = lastByte
                            },
                            token).ConfigureAwait(false),
                        cancellationToken).ConfigureAwait(false);

                    partETags.Add(new PartETag(partNumber, part.ETag));
                    Report(progress, "Copying", completedObjects, sourceKey, lastByte + 1, size);
                }

                await retry.ExecuteAsync(
                    "CompleteMultipartCopy",
                    async token => await client.CompleteMultipartUploadAsync(
                        new CompleteMultipartUploadRequest
                        {
                            BucketName = settings.BucketName,
                            Key = destinationKey,
                            UploadId = initiated.UploadId,
                            PartETags = partETags
                        },
                        token).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
                completed = true;
            }
            finally
            {
                if (!completed)
                {
                    try
                    {
                        await client.AbortMultipartUploadAsync(
                            new AbortMultipartUploadRequest
                            {
                                BucketName = settings.BucketName,
                                Key = destinationKey,
                                UploadId = initiated.UploadId
                            },
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (_log != null) _log.Warning("Browser.CopyAbort", "Incomplete multipart copy could not be aborted: " + LoggingService.Sanitize(ex.Message));
                    }
                }
            }
        }

        private static async Task EnsureSourceUnchangedAsync(
            AmazonS3Client client,
            AppSettings settings,
            string key,
            long expectedSize,
            string expectedETag,
            CancellationToken cancellationToken)
        {
            GetObjectMetadataResponse current = await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = settings.BucketName, Key = key },
                cancellationToken).ConfigureAwait(false);
            if (current.ContentLength != expectedSize ||
                (!string.IsNullOrEmpty(expectedETag) &&
                 !string.Equals(current.ETag, expectedETag, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "The source changed while it was being moved. The verified copy was kept, but the newer source was not deleted.");
            }
        }

        private static async Task EnsurePrefixUnchangedAsync(
            AmazonS3Client client,
            AppSettings settings,
            string prefix,
            IList<SourceSnapshot> expected,
            CancellationToken cancellationToken)
        {
            Dictionary<string, SourceSnapshot> remaining =
                new Dictionary<string, SourceSnapshot>(StringComparer.Ordinal);
            foreach (SourceSnapshot snapshot in expected) remaining[snapshot.Key] = snapshot;

            string continuationToken = null;
            do
            {
                ListObjectsV2Response page = await client.ListObjectsV2Async(
                    new ListObjectsV2Request
                    {
                        BucketName = settings.BucketName,
                        Prefix = prefix,
                        MaxKeys = ListPageSize,
                        ContinuationToken = continuationToken
                    },
                    cancellationToken).ConfigureAwait(false);

                if (page.S3Objects != null)
                {
                    foreach (S3Object current in page.S3Objects)
                    {
                        SourceSnapshot snapshot;
                        if (!remaining.TryGetValue(current.Key, out snapshot) ||
                            snapshot.Size != current.Size ||
                            (!string.IsNullOrEmpty(snapshot.ETag) &&
                             !string.Equals(snapshot.ETag, current.ETag, StringComparison.Ordinal)))
                        {
                            throw new InvalidOperationException(
                                "The source folder changed while it was being moved. The copied objects were kept, but no source objects were deleted.");
                        }
                        remaining.Remove(current.Key);
                    }
                }

                continuationToken = page.IsTruncated ? page.NextContinuationToken : null;
            }
            while (!string.IsNullOrEmpty(continuationToken));

            if (remaining.Count != 0)
                throw new InvalidOperationException(
                    "The source folder changed while it was being moved. The copied objects were kept, but no source objects were deleted.");
        }

        private static async Task DeleteSnapshotsAsync(
            AmazonS3Client client,
            AppSettings settings,
            IList<SourceSnapshot> snapshots,
            IProgress<R2ObjectOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            int deleted = 0;
            while (deleted < snapshots.Count)
            {
                int count = Math.Min(DeleteBatchSize, snapshots.Count - deleted);
                List<KeyVersion> keys = new List<KeyVersion>(count);
                for (int i = 0; i < count; i++)
                    keys.Add(new KeyVersion { Key = snapshots[deleted + i].Key });

                await DeleteBatchAsync(client, settings, keys, cancellationToken).ConfigureAwait(false);
                deleted += count;
                Report(progress, "Removing source folder", deleted, string.Empty, 0, 0);
            }
        }

        private static async Task DeleteKeyAsync(
            AmazonS3Client client,
            AppSettings settings,
            string key,
            CancellationToken cancellationToken)
        {
            await client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = settings.BucketName, Key = key },
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task DeleteBatchAsync(
            AmazonS3Client client,
            AppSettings settings,
            List<KeyVersion> keys,
            CancellationToken cancellationToken)
        {
            DeleteObjectsResponse response = await client.DeleteObjectsAsync(
                new DeleteObjectsRequest
                {
                    BucketName = settings.BucketName,
                    Objects = keys,
                    Quiet = true
                },
                cancellationToken).ConfigureAwait(false);

            if (response.DeleteErrors != null && response.DeleteErrors.Count > 0)
            {
                DeleteError first = response.DeleteErrors[0];
                throw new InvalidOperationException(
                    "R2 did not delete " + response.DeleteErrors.Count + " object(s). First error: " +
                    (first.Code ?? "Unknown") + ".");
            }
        }

        private static async Task<bool> ExistsAsync(
            AmazonS3Client client,
            string bucketName,
            string destination,
            bool isFolder,
            CancellationToken cancellationToken)
        {
            if (isFolder)
            {
                ListObjectsV2Response response = await client.ListObjectsV2Async(
                    new ListObjectsV2Request
                    {
                        BucketName = bucketName,
                        Prefix = R2BrowserPathUtility.NormalizePrefix(destination),
                        MaxKeys = 1
                    },
                    cancellationToken).ConfigureAwait(false);
                return response.S3Objects != null && response.S3Objects.Count > 0;
            }

            try
            {
                await client.GetObjectMetadataAsync(
                    new GetObjectMetadataRequest { BucketName = bucketName, Key = destination },
                    cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (AmazonS3Exception ex) when ((int)ex.StatusCode == 404 ||
                                               string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase) ||
                                               string.Equals(ex.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        private static async Task<bool> NameExistsAsync(
            AmazonS3Client client,
            string bucketName,
            string destination,
            bool isFolder,
            CancellationToken cancellationToken)
        {
            string objectKey = isFolder
                ? R2BrowserPathUtility.NormalizePrefix(destination).TrimEnd('/')
                : destination;
            string folderPrefix = R2BrowserPathUtility.NormalizePrefix(objectKey);

            return await ExistsAsync(
                       client, bucketName, objectKey, false, cancellationToken).ConfigureAwait(false) ||
                   await ExistsAsync(
                       client, bucketName, folderPrefix, true, cancellationToken).ConfigureAwait(false);
        }

        private static async Task CopyStreamAsync(
            Stream input,
            FileStream output,
            string key,
            long totalBytes,
            IProgress<R2ObjectOperationProgress> progress,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[81920];
            long copied = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
                Report(progress, "Downloading", 0, key, copied, totalBytes);
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void Report(
            IProgress<R2ObjectOperationProgress> progress,
            string operation,
            int completedObjects,
            string currentKey,
            long transferredBytes,
            long totalBytes)
        {
            if (progress != null)
                progress.Report(new R2ObjectOperationProgress(
                    operation, completedObjects, currentKey, transferredBytes, totalBytes));
        }

        private void LogSuccess(string operation, AppSettings settings, string source, string destination)
        {
            if (_log == null) return;
            string message = "Bucket='" + settings.BucketName + "' endpoint=" +
                R2ClientFactory.DescribeEndpoint(settings) + " source='" + source + "'";
            if (!string.IsNullOrEmpty(destination)) message += " destination='" + destination + "'";
            _log.Info(operation, message + ".");
        }

        private static void TryDeleteLocalFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private sealed class SourceSnapshot
        {
            public SourceSnapshot(string key, long size, string eTag)
            {
                Key = key;
                Size = size;
                ETag = eTag;
            }

            public string Key { get; private set; }
            public long Size { get; private set; }
            public string ETag { get; private set; }
        }
    }
}
