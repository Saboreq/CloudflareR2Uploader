using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using Amazon.Runtime;

namespace CloudflareR2Uploader.Services
{
    /// <summary>How a failed operation should be handled.</summary>
    public enum ErrorClassification
    {
        /// <summary>Worth retrying after a backoff: the same request may well succeed.</summary>
        Transient = 0,

        /// <summary>Retrying cannot help: credentials, permissions, names or missing files.</summary>
        Permanent = 1,

        /// <summary>The user asked to stop; not an error at all.</summary>
        UserCancelled = 2
    }

    /// <summary>
    /// Decides whether an exception represents a temporary condition. Getting this wrong in
    /// either direction is costly: retrying a permanent auth failure wastes time and can
    /// trip rate limits, while giving up on a 503 loses an otherwise fine upload.
    /// </summary>
    public static class TransientErrorClassifier
    {
        private static readonly int[] TransientStatusCodes = { 408, 429, 500, 502, 503, 504 };

        /// <summary>
        /// AWS/S3 error codes that are permanent no matter what the HTTP status says.
        /// R2 returns the S3-compatible codes for these conditions.
        /// </summary>
        private static readonly string[] PermanentErrorCodes =
        {
            "AccessDenied",
            "AccountProblem",
            "AllAccessDisabled",
            "CredentialsNotSupported",
            "InvalidAccessKeyId",
            "InvalidArgument",
            "InvalidBucketName",
            "InvalidObjectState",
            "InvalidRequest",
            "InvalidSecurity",
            "KeyTooLongError",
            "MethodNotAllowed",
            "NoSuchBucket",
            "NoSuchKey",
            "NotSignedUp",
            "SignatureDoesNotMatch",
            "TokenRefreshRequired",
            "UnauthorizedAccess",
            "EntityTooLarge",
            "EntityTooSmall",
            "InvalidPart",
            "InvalidPartOrder",
            "MalformedXML"
        };

        /// <summary>
        /// AWS/S3 error codes that are explicitly temporary.
        /// <c>NoSuchUpload</c> is deliberately absent: a vanished multipart upload needs a
        /// restart, not a retry.
        /// </summary>
        private static readonly string[] TransientErrorCodes =
        {
            "InternalError",
            "RequestTimeout",
            "RequestTimeTooSkewed",
            "ServiceUnavailable",
            "SlowDown",
            "Throttling",
            "ThrottlingException",
            "TooManyRequests",
            "OperationAborted",
            "ExpiredToken"
        };

        /// <summary>
        /// Classifies <paramref name="exception"/>.
        /// <paramref name="userToken"/> is the token the user controls; a cancellation caused
        /// by it is <see cref="ErrorClassification.UserCancelled"/>, while a cancellation from
        /// anywhere else is an HTTP timeout and therefore transient.
        /// </summary>
        public static ErrorClassification Classify(Exception exception, CancellationToken userToken)
        {
            if (exception == null) return ErrorClassification.Permanent;

            if (exception is OperationCanceledException)
            {
                return userToken.IsCancellationRequested
                    ? ErrorClassification.UserCancelled
                    : ErrorClassification.Transient;   // the SDK's own request timeout
            }

            if (exception is AggregateException)
            {
                AggregateException aggregate = (AggregateException)exception;
                Exception inner = aggregate.Flatten().InnerException;
                if (inner != null) return Classify(inner, userToken);
                return ErrorClassification.Permanent;
            }

            // A local file that is missing, locked or unreadable will not fix itself.
            if (exception is FileNotFoundException ||
                exception is DirectoryNotFoundException ||
                exception is UnauthorizedAccessException ||
                exception is PathTooLongException ||
                exception is NotSupportedException ||
                exception is ArgumentException ||
                exception is ObjectDisposedException)
            {
                return ErrorClassification.Permanent;
            }

            // TLS problems are configuration or interception issues, not blips.
            if (exception is AuthenticationException) return ErrorClassification.Permanent;

            AmazonServiceException serviceException = exception as AmazonServiceException;
            if (serviceException != null) return ClassifyServiceException(serviceException, userToken);

            if (exception is WebException) return ClassifyWebException((WebException)exception);

            if (exception is SocketException) return ClassifySocketException((SocketException)exception);

            if (exception is TimeoutException) return ErrorClassification.Transient;

            // An IOException wrapping a socket failure is a dropped connection.
            if (exception is IOException)
            {
                if (exception.InnerException != null) return Classify(exception.InnerException, userToken);
                return ErrorClassification.Transient;
            }

            if (exception is System.Net.Http.HttpRequestException)
            {
                if (exception.InnerException != null) return Classify(exception.InnerException, userToken);
                return ErrorClassification.Transient;
            }

            if (exception.InnerException != null) return Classify(exception.InnerException, userToken);

            return ErrorClassification.Permanent;
        }

        private static ErrorClassification ClassifyServiceException(AmazonServiceException exception, CancellationToken userToken)
        {
            string code = exception.ErrorCode ?? string.Empty;

            if (Contains(PermanentErrorCodes, code)) return ErrorClassification.Permanent;
            if (Contains(TransientErrorCodes, code)) return ErrorClassification.Transient;

            int status = (int)exception.StatusCode;
            if (IsTransientStatusCode(status)) return ErrorClassification.Transient;

            // Any other 4xx is a request the service will keep rejecting.
            if (status >= 400 && status < 500) return ErrorClassification.Permanent;
            if (status >= 500) return ErrorClassification.Transient;

            // Status 0 means the request never got a response: look at what wrapped it.
            if (exception.InnerException != null) return Classify(exception.InnerException, userToken);

            // The SDK's own judgement is the last word.
            return exception.Retryable != null ? ErrorClassification.Transient : ErrorClassification.Permanent;
        }

        private static ErrorClassification ClassifyWebException(WebException exception)
        {
            switch (exception.Status)
            {
                case WebExceptionStatus.ConnectFailure:
                case WebExceptionStatus.ConnectionClosed:
                case WebExceptionStatus.KeepAliveFailure:
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.PipelineFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                case WebExceptionStatus.ReceiveFailure:
                case WebExceptionStatus.SendFailure:
                case WebExceptionStatus.Timeout:
                    return ErrorClassification.Transient;

                case WebExceptionStatus.TrustFailure:
                case WebExceptionStatus.SecureChannelFailure:
                    return ErrorClassification.Permanent;

                case WebExceptionStatus.ProtocolError:
                    HttpWebResponse response = exception.Response as HttpWebResponse;
                    if (response != null && IsTransientStatusCode((int)response.StatusCode))
                        return ErrorClassification.Transient;
                    return ErrorClassification.Permanent;

                default:
                    return ErrorClassification.Transient;
            }
        }

        private static ErrorClassification ClassifySocketException(SocketException exception)
        {
            switch (exception.SocketErrorCode)
            {
                case SocketError.ConnectionReset:
                case SocketError.ConnectionAborted:
                case SocketError.TimedOut:
                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                case SocketError.NetworkDown:
                case SocketError.NetworkReset:
                case SocketError.TryAgain:
                case SocketError.HostNotFound:      // transient DNS failure, e.g. while a VPN reconnects
                case SocketError.NoData:
                case SocketError.Interrupted:
                case SocketError.ConnectionRefused:
                    return ErrorClassification.Transient;

                default:
                    return ErrorClassification.Transient;
            }
        }

        public static bool IsTransientStatusCode(int statusCode)
        {
            foreach (int code in TransientStatusCodes)
            {
                if (code == statusCode) return true;
            }
            return false;
        }

        /// <summary>Extracts the HTTP status code from an SDK exception, or 0 if there is none.</summary>
        public static int GetStatusCode(Exception exception)
        {
            AmazonServiceException serviceException = exception as AmazonServiceException;
            if (serviceException != null) return (int)serviceException.StatusCode;

            WebException webException = exception as WebException;
            if (webException != null)
            {
                HttpWebResponse response = webException.Response as HttpWebResponse;
                if (response != null) return (int)response.StatusCode;
            }

            if (exception != null && exception.InnerException != null) return GetStatusCode(exception.InnerException);
            return 0;
        }

        /// <summary>Extracts the AWS/S3 error code, or an empty string.</summary>
        public static string GetErrorCode(Exception exception)
        {
            AmazonServiceException serviceException = exception as AmazonServiceException;
            if (serviceException != null) return serviceException.ErrorCode ?? string.Empty;

            if (exception != null && exception.InnerException != null) return GetErrorCode(exception.InnerException);
            return string.Empty;
        }

        private static bool Contains(string[] values, string candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return false;
            foreach (string value in values)
            {
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
