using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>Outcome of a single-request upload.</summary>
    public sealed class SinglePartUploadResult
    {
        public SinglePartUploadResult(string eTag)
        {
            ETag = eTag;
        }

        public string ETag { get; private set; }
    }

    /// <summary>
    /// Uploads a file below the multipart threshold in one <c>PutObject</c> request.
    /// <para>
    /// The body is streamed straight from disk — the file is never read into memory — and the
    /// stream is re-created for every attempt so a retry always starts from byte zero.
    /// </para>
    /// </summary>
    public sealed class R2SinglePartUploader
    {
        private const int StreamBufferSize = 81920;

        private readonly ILoggingService _log;

        public R2SinglePartUploader(ILoggingService log)
        {
            _log = log;
        }

        public async Task<SinglePartUploadResult> UploadAsync(
            IAmazonS3 client,
            string bucketName,
            string objectKey,
            UploadQueueItem item,
            string contentType,
            RetryService retryService,
            IProgress<UploadProgressInfo> progress,
            SpeedEstimator speedEstimator,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(retryService);

            ProgressThrottle throttle = new ProgressThrottle();

            string eTag = await retryService.ExecuteAsync(
                "PutObject",
                async token => await PutOnceAsync(
                    client, bucketName, objectKey, item, contentType,
                    progress, speedEstimator, throttle, token).ConfigureAwait(false),
                cancellationToken,
                info => item.IncrementRetryCount()).ConfigureAwait(false);

            // A same-size file can still be modified through a handle that was opened before
            // our read stream. Do not report that object as a good copy unless both the size
            // and last-write timestamp still match after the request has finished.
            string sourceProblem;
            if (!item.TryRefreshFileInfo(out sourceProblem))
            {
                throw new IOException(
                    "The upload finished, but the source file changed while it was being read: " +
                    sourceProblem);
            }

            return new SinglePartUploadResult(eTag);
        }

        private async Task<string> PutOnceAsync(
            IAmazonS3 client,
            string bucketName,
            string objectKey,
            UploadQueueItem item,
            string contentType,
            IProgress<UploadProgressInfo> progress,
            SpeedEstimator speedEstimator,
            ProgressThrottle throttle,
            CancellationToken cancellationToken)
        {
            // A fresh stream per attempt: the previous attempt may have consumed part of it.
            using (FileStream source = new FileStream(
                PathUtility.ToExtendedLengthPath(item.LocalFilePath),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                StreamBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (source.Length != item.FileSize)
                {
                    throw new IOException(
                        "The file changed size while it was queued (expected "
                        + FileSizeFormatter.Format(item.FileSize) + ", found "
                        + FileSizeFormatter.Format(source.Length) + ").");
                }

                PutObjectRequest request = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = objectKey,
                    InputStream = source,
                    ContentType = contentType,
                    AutoCloseStream = false,
                    AutoResetStreamPosition = false,

                    // Cloudflare R2 does not support the AWS streaming SigV4 payload
                    // signing/checksum flow, so both must be switched off. This is only safe
                    // because R2ClientFactory refuses any endpoint that is not HTTPS.
                    DisablePayloadSigning = true,
                    DisableDefaultChecksumValidation = true,
                    UseChunkEncoding = false
                };

                EventHandler<Amazon.Runtime.StreamTransferProgressArgs> handler =
                    delegate (object sender, Amazon.Runtime.StreamTransferProgressArgs args)
                    {
                        if (!throttle.ShouldReport()) return;

                        if (speedEstimator != null) speedEstimator.Report(args.TransferredBytes);
                        if (progress != null)
                        {
                            progress.Report(new UploadProgressInfo(
                                args.TransferredBytes,
                                item.FileSize,
                                speedEstimator == null ? 0 : speedEstimator.BytesPerSecond,
                                0, 0));
                        }
                    };

                request.StreamTransferProgress += handler;

                try
                {
                    PutObjectResponse response = await client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

                    if (progress != null)
                    {
                        progress.Report(new UploadProgressInfo(
                            item.FileSize, item.FileSize,
                            speedEstimator == null ? 0 : speedEstimator.BytesPerSecond, 0, 0));
                    }

                    if (_log != null)
                    {
                        _log.Info("PutObject",
                            "Uploaded key=" + objectKey + " size=" + item.FileSize +
                            " http=" + (int)response.HttpStatusCode + ".");
                    }

                    return response.ETag;
                }
                finally
                {
                    request.StreamTransferProgress -= handler;
                }
            }
        }
    }
}
