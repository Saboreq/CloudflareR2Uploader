using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    /// <summary>A message pair: what to show the user, and what to put behind "Copy technical details".</summary>
    public sealed class FriendlyError
    {
        public FriendlyError(string headline, string detail, string technicalDetails, bool isRetryable)
        {
            Headline = headline;
            Detail = detail;
            TechnicalDetails = technicalDetails;
            IsRetryable = isRetryable;
        }

        public string Headline { get; private set; }
        public string Detail { get; private set; }
        public string TechnicalDetails { get; private set; }
        public bool IsRetryable { get; private set; }

        public string FullMessage
        {
            get { return string.IsNullOrEmpty(Detail) ? Headline : Headline + " " + Detail; }
        }
    }

    /// <summary>
    /// Turns SDK and network exceptions into messages a person can act on, and produces a
    /// sanitised technical block. Stack traces never reach a normal message box.
    /// </summary>
    public static class FriendlyErrorService
    {
        public static FriendlyError Describe(Exception exception, AppSettings settings, string objectKey = null)
        {
            if (exception == null)
                return new FriendlyError("Unknown error", "No further information is available.", string.Empty, false);

            if (exception is OperationCanceledException)
            {
                return new FriendlyError(
                    "Cancelled",
                    "The operation was cancelled.",
                    BuildTechnicalDetails(exception, settings, objectKey),
                    true);
            }

            AmazonS3Exception s3Exception = Unwrap<AmazonS3Exception>(exception);
            if (s3Exception != null) return DescribeS3(s3Exception, settings, objectKey);

            AmazonServiceException serviceException = Unwrap<AmazonServiceException>(exception);
            if (serviceException != null) return DescribeService(serviceException, settings, objectKey);

            FriendlyError network = DescribeNetwork(exception, settings, objectKey);
            if (network != null) return network;

            if (exception is FileNotFoundException)
            {
                return new FriendlyError(
                    "The file was not found",
                    "It may have been moved, renamed or deleted since it was added to the queue.",
                    BuildTechnicalDetails(exception, settings, objectKey),
                    false);
            }

            if (exception is UnauthorizedAccessException)
            {
                return new FriendlyError(
                    "The file could not be read",
                    "Windows denied access to it. Check the file's permissions, or whether another program has it open exclusively.",
                    BuildTechnicalDetails(exception, settings, objectKey),
                    false);
            }

            if (exception is IOException)
            {
                return new FriendlyError(
                    "The file could not be read",
                    "It may be locked by another program, on a disconnected drive, or changing while it is being uploaded.",
                    BuildTechnicalDetails(exception, settings, objectKey),
                    true);
            }

            if (exception is InvalidOperationException)
            {
                return new FriendlyError(
                    "The upload could not start",
                    LoggingService.Sanitize(exception.Message),
                    BuildTechnicalDetails(exception, settings, objectKey),
                    false);
            }

            return new FriendlyError(
                "Unexpected error",
                LoggingService.Sanitize(exception.Message),
                BuildTechnicalDetails(exception, settings, objectKey),
                false);
        }

        private static FriendlyError DescribeS3(AmazonS3Exception exception, AppSettings settings, string objectKey)
        {
            string technical = BuildTechnicalDetails(exception, settings, objectKey);
            int status = (int)exception.StatusCode;
            string code = exception.ErrorCode ?? string.Empty;

            switch (code)
            {
                case "InvalidAccessKeyId":
                    return new FriendlyError(
                        "The Access Key ID was not recognised",
                        "Check the Access Key ID in Settings. It must come from an R2 API token's S3 credentials, not from a Cloudflare dashboard API token string.",
                        technical, false);

                case "SignatureDoesNotMatch":
                    return new FriendlyError(
                        "The Secret Access Key is incorrect",
                        "Re-enter the Secret Access Key. It is shown only once when the R2 API token is created; if it was lost, create a new token.",
                        technical, false);

                case "AccessDenied":
                    return new FriendlyError(
                        "Access denied",
                        "The credentials are valid but the token does not permit this operation on this bucket. The token needs Object Read & Write permission scoped to the bucket.",
                        technical, false);

                case "NoSuchBucket":
                    return new FriendlyError(
                        "The bucket does not exist",
                        "No bucket named \"" + SafeBucket(settings) + "\" was found in this R2 account. Check the spelling and the Account ID.",
                        technical, false);

                case "InvalidBucketName":
                    return new FriendlyError(
                        "The bucket name is not valid",
                        "R2 bucket names use lower-case letters, digits and hyphens.",
                        technical, false);

                case "NoSuchUpload":
                    return new FriendlyError(
                        "The multipart upload no longer exists",
                        "R2 has discarded the incomplete upload, so it cannot be resumed. Start this file again.",
                        technical, false);

                case "EntityTooSmall":
                    return new FriendlyError(
                        "A part was rejected as too small",
                        "Every part except the last must be at least 5 MiB. Increase the part size in Settings and upload the file again.",
                        technical, false);

                case "InvalidPart":
                case "InvalidPartOrder":
                    return new FriendlyError(
                        "The multipart upload could not be completed",
                        "R2 rejected the list of parts. The saved resume state no longer matches what is stored, so this file needs to be uploaded again.",
                        technical, false);

                case "KeyTooLongError":
                    return new FriendlyError(
                        "The object key is too long",
                        "Object keys are limited to 1024 bytes. Shorten the destination folder or the file name.",
                        technical, false);

                case "SlowDown":
                case "TooManyRequests":
                case "Throttling":
                    return new FriendlyError(
                        "R2 is rate limiting these requests",
                        "The upload will back off and retry. Lowering the parallel part count in Settings makes this less likely.",
                        technical, true);
            }

            switch (status)
            {
                case 401:
                    return new FriendlyError(
                        "Authentication failed",
                        "R2 rejected the credentials. Check the Access Key ID and Secret Access Key in Settings.",
                        technical, false);

                case 403:
                    return new FriendlyError(
                        "Access denied",
                        "The token does not have permission for this bucket, or the Account ID belongs to a different account.",
                        technical, false);

                case 404:
                    return new FriendlyError(
                        "Not found",
                        "The bucket or object could not be found. Check the bucket name in Settings.",
                        technical, false);

                case 408:
                case 429:
                    return new FriendlyError(
                        "R2 asked the client to slow down",
                        "The request will be retried automatically after a short delay.",
                        technical, true);
            }

            if (status >= 500)
            {
                return new FriendlyError(
                    "R2 returned a server error",
                    "This is usually temporary. The request will be retried automatically.",
                    technical, true);
            }

            return new FriendlyError(
                "R2 rejected the request",
                DescribeCodeAndStatus(code, status),
                technical, false);
        }

        private static FriendlyError DescribeService(AmazonServiceException exception, AppSettings settings, string objectKey)
        {
            string technical = BuildTechnicalDetails(exception, settings, objectKey);

            // A status of 0 means the request never reached R2.
            if ((int)exception.StatusCode == 0 && exception.InnerException != null)
            {
                FriendlyError network = DescribeNetwork(exception.InnerException, settings, objectKey);
                if (network != null) return network;
            }

            return new FriendlyError(
                "The request to R2 failed",
                DescribeCodeAndStatus(exception.ErrorCode, (int)exception.StatusCode),
                technical,
                TransientErrorClassifier.IsTransientStatusCode((int)exception.StatusCode));
        }

        private static FriendlyError DescribeNetwork(Exception exception, AppSettings settings, string objectKey)
        {
            string technical = BuildTechnicalDetails(exception, settings, objectKey);

            AuthenticationException authentication = Unwrap<AuthenticationException>(exception);
            if (authentication != null)
            {
                return new FriendlyError(
                    "The secure connection could not be established",
                    "TLS negotiation with R2 failed. This is often caused by an intercepting proxy or antivirus TLS scanning. Check that the machine trusts Cloudflare's certificate chain.",
                    technical, false);
            }

            WebException webException = Unwrap<WebException>(exception);
            if (webException != null)
            {
                switch (webException.Status)
                {
                    case WebExceptionStatus.NameResolutionFailure:
                    case WebExceptionStatus.ProxyNameResolutionFailure:
                        return new FriendlyError(
                            "The R2 endpoint could not be resolved",
                            "DNS lookup for " + R2ClientFactory.DescribeEndpoint(settings) + " failed. Check the network connection and that the Account ID is correct.",
                            technical, true);

                    case WebExceptionStatus.TrustFailure:
                    case WebExceptionStatus.SecureChannelFailure:
                        return new FriendlyError(
                            "The secure connection could not be established",
                            "The TLS certificate for the R2 endpoint was not trusted. Check for an intercepting proxy or antivirus TLS scanning.",
                            technical, false);

                    case WebExceptionStatus.ConnectFailure:
                    case WebExceptionStatus.ConnectionClosed:
                    case WebExceptionStatus.KeepAliveFailure:
                    case WebExceptionStatus.ReceiveFailure:
                    case WebExceptionStatus.SendFailure:
                        return new FriendlyError(
                            "The connection to R2 was lost",
                            "The network dropped mid-request. The upload will be retried automatically.",
                            technical, true);

                    case WebExceptionStatus.Timeout:
                        return new FriendlyError(
                            "The request to R2 timed out",
                            "The network may be slow or unstable. The upload will be retried automatically.",
                            technical, true);
                }
            }

            SocketException socketException = Unwrap<SocketException>(exception);
            if (socketException != null)
            {
                if (socketException.SocketErrorCode == SocketError.HostNotFound ||
                    socketException.SocketErrorCode == SocketError.NoData ||
                    socketException.SocketErrorCode == SocketError.TryAgain)
                {
                    return new FriendlyError(
                        "The R2 endpoint could not be resolved",
                        "DNS lookup failed. Check the network connection and the Account ID.",
                        technical, true);
                }

                if (socketException.SocketErrorCode == SocketError.NetworkDown ||
                    socketException.SocketErrorCode == SocketError.NetworkUnreachable ||
                    socketException.SocketErrorCode == SocketError.HostUnreachable)
                {
                    return new FriendlyError(
                        "The network is unavailable",
                        "Windows reports no route to the internet. Reconnect and retry the failed files.",
                        technical, true);
                }

                return new FriendlyError(
                    "The connection to R2 was interrupted",
                    "The connection was reset. The upload will be retried automatically.",
                    technical, true);
            }

            if (Unwrap<TimeoutException>(exception) != null)
            {
                return new FriendlyError(
                    "The request to R2 timed out",
                    "The network may be slow or unstable. The upload will be retried automatically.",
                    technical, true);
            }

            return null;
        }

        private static string DescribeCodeAndStatus(string code, int status)
        {
            StringBuilder builder = new StringBuilder();
            if (status > 0) builder.Append("HTTP ").Append(status.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(code))
            {
                if (builder.Length > 0) builder.Append(", ");
                builder.Append("error code ").Append(code);
            }
            if (builder.Length == 0) return "R2 did not provide a reason.";
            return builder.ToString() + ".";
        }

        /// <summary>
        /// Builds the block shown behind "Copy technical details". Everything goes through
        /// <see cref="LoggingService.Sanitize"/>, and no credential value is ever included.
        /// </summary>
        public static string BuildTechnicalDetails(Exception exception, AppSettings settings, string objectKey)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Cloudflare R2 Uploader — technical details");
            builder.AppendLine("Timestamp (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

            if (settings != null)
            {
                builder.AppendLine("Endpoint: " + R2ClientFactory.DescribeEndpoint(settings));
                builder.AppendLine("Bucket: " + SafeBucket(settings));
                builder.AppendLine("Multipart threshold: " + settings.MultipartThresholdMiB + " MiB");
                builder.AppendLine("Part size: " + settings.PartSizeMiB + " MiB");
                builder.AppendLine("Parallel parts: " + settings.ParallelParts);
                builder.AppendLine("Retry attempts: " + settings.RetryAttempts);
            }

            if (!string.IsNullOrEmpty(objectKey)) builder.AppendLine("Object key: " + objectKey);

            if (exception != null)
            {
                int status = TransientErrorClassifier.GetStatusCode(exception);
                string code = TransientErrorClassifier.GetErrorCode(exception);
                if (status > 0) builder.AppendLine("HTTP status: " + status.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(code)) builder.AppendLine("Error code: " + code);

                AmazonServiceException serviceException = Unwrap<AmazonServiceException>(exception);
                if (serviceException != null && !string.IsNullOrEmpty(serviceException.RequestId))
                    builder.AppendLine("Request ID: " + serviceException.RequestId);

                builder.AppendLine("Exception: " + exception.GetType().FullName);
                builder.AppendLine("Message: " + LoggingService.Sanitize(exception.Message));

                Exception inner = exception.InnerException;
                int depth = 0;
                while (inner != null && depth < 5)
                {
                    builder.AppendLine("  Caused by: " + inner.GetType().FullName + ": " + LoggingService.Sanitize(inner.Message));
                    inner = inner.InnerException;
                    depth++;
                }
            }

            builder.AppendLine();
            builder.AppendLine("No credentials are included in this report.");

            // Final safety pass: sanitise line by line so the redaction rules apply to every
            // line while the layout survives.
            string[] lines = builder.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++) lines[i] = LoggingService.Sanitize(lines[i]);
            return string.Join(Environment.NewLine, lines);
        }

        private static string SafeBucket(AppSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.BucketName)) return "(not set)";
            return settings.BucketName;
        }

        private static T Unwrap<T>(Exception exception) where T : Exception
        {
            int depth = 0;
            Exception current = exception;
            while (current != null && depth < 8)
            {
                T match = current as T;
                if (match != null) return match;

                AggregateException aggregate = current as AggregateException;
                if (aggregate != null)
                {
                    current = aggregate.Flatten().InnerException;
                }
                else
                {
                    current = current.InnerException;
                }
                depth++;
            }
            return null;
        }
    }
}
