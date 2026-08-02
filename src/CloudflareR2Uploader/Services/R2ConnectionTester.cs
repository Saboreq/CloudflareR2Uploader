using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Verifies that the configured credentials can reach the configured bucket.
    /// <para>
    /// Uses <c>ListObjectsV2</c> with <c>MaxKeys = 1</c> rather than <c>ListBuckets</c>, so a
    /// token scoped to a single bucket — which is what the README recommends creating —
    /// passes the test. It reads at most one key name and never writes anything.
    /// </para>
    /// </summary>
    public sealed class R2ConnectionTester
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

        private readonly ILoggingService _log;

        public R2ConnectionTester(ILoggingService log)
        {
            _log = log;
        }

        public async Task<ConnectionTestResult> TestAsync(
            AppSettings settings,
            R2Credentials credentials,
            CancellationToken cancellationToken)
        {
            ConnectionTestResult validation = ValidateLocally(settings, credentials);
            if (validation != null) return validation;

            AmazonS3Client client = null;
            try
            {
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TestTimeout);

                    client = R2ClientFactory.CreateClient(settings, credentials);

                    ListObjectsV2Request request = new ListObjectsV2Request
                    {
                        BucketName = settings.BucketName,
                        MaxKeys = 1
                    };

                    ListObjectsV2Response response =
                        await client.ListObjectsV2Async(request, timeout.Token).ConfigureAwait(false);

                    if (_log != null)
                    {
                        _log.Info("Connection.Test",
                            "Connected to bucket '" + settings.BucketName + "' at " +
                            R2ClientFactory.DescribeEndpoint(settings) + " (http=" + (int)response.HttpStatusCode + ").");
                    }

                    return new ConnectionTestResult(
                        ConnectionTestStatus.Success,
                        "Connected",
                        "The bucket \"" + settings.BucketName + "\" is reachable and the credentials work. Uploads are enabled.",
                        null);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new ConnectionTestResult(
                    ConnectionTestStatus.NetworkError,
                    "Cancelled",
                    "The connection test was cancelled.",
                    null);
            }
            catch (OperationCanceledException ex)
            {
                if (_log != null) _log.Warning("Connection.Test", "The connection test timed out after " + (int)TestTimeout.TotalSeconds + "s.");
                return new ConnectionTestResult(
                    ConnectionTestStatus.NetworkError,
                    "The connection timed out",
                    "R2 did not respond within " + (int)TestTimeout.TotalSeconds + " seconds. Check the network connection and any proxy settings.",
                    FriendlyErrorService.BuildTechnicalDetails(ex, settings, null));
            }
            catch (Exception ex)
            {
                return Interpret(ex, settings);
            }
            finally
            {
                if (client != null) client.Dispose();
            }
        }

        /// <summary>Field-level checks that need no network round trip.</summary>
        private static ConnectionTestResult ValidateLocally(AppSettings settings, R2Credentials credentials)
        {
            if (settings == null || credentials == null)
            {
                return new ConnectionTestResult(
                    ConnectionTestStatus.MissingConfiguration,
                    "Settings are incomplete",
                    "Fill in the connection details before testing.",
                    null);
            }

            bool hasCustomEndpoint = !string.IsNullOrWhiteSpace(settings.CustomEndpoint);

            if (string.IsNullOrWhiteSpace(settings.AccountId) && !hasCustomEndpoint)
            {
                return new ConnectionTestResult(
                    ConnectionTestStatus.MissingConfiguration,
                    "The Account ID is missing",
                    "Enter the Cloudflare Account ID, or set a custom endpoint instead.",
                    null);
            }

            if (string.IsNullOrWhiteSpace(settings.BucketName))
            {
                return new ConnectionTestResult(
                    ConnectionTestStatus.MissingConfiguration,
                    "The bucket name is missing",
                    "Enter the name of the R2 bucket to upload into.",
                    null);
            }

            if (string.IsNullOrWhiteSpace(credentials.AccessKeyId))
            {
                return new ConnectionTestResult(
                    ConnectionTestStatus.MissingConfiguration,
                    "The Access Key ID is missing",
                    "Enter the Access Key ID from the R2 API token's S3 credentials.",
                    null);
            }

            if (string.IsNullOrWhiteSpace(credentials.SecretAccessKey))
            {
                return new ConnectionTestResult(
                    ConnectionTestStatus.MissingConfiguration,
                    "The Secret Access Key is missing",
                    "Enter the Secret Access Key shown when the R2 API token was created.",
                    null);
            }

            string serviceUrl = settings.ResolveServiceUrl();
            Uri uri;
            if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out uri))
            {
                return new ConnectionTestResult(
                    ConnectionTestStatus.InvalidEndpoint,
                    "The endpoint is not a valid URL",
                    "Derived endpoint: " + serviceUrl + ". Check the Account ID, or clear the custom endpoint field.",
                    null);
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return new ConnectionTestResult(
                    ConnectionTestStatus.InvalidEndpoint,
                    "The endpoint must use HTTPS",
                    "Uploads to R2 send an unsigned payload, so plain HTTP is refused. Use an https:// endpoint.",
                    null);
            }

            return null;
        }

        /// <summary>Maps a failed test onto a specific, separately-worded status.</summary>
        private ConnectionTestResult Interpret(Exception exception, AppSettings settings)
        {
            FriendlyError friendly = FriendlyErrorService.Describe(exception, settings, null);
            int status = TransientErrorClassifier.GetStatusCode(exception);
            string code = TransientErrorClassifier.GetErrorCode(exception);

            if (_log != null)
            {
                _log.HttpFailure("Connection.Test", status, code,
                    "Connection test failed: " + friendly.Headline, null, null);
            }

            ConnectionTestStatus testStatus = ConnectionTestStatus.UnknownError;

            if (string.Equals(code, "InvalidAccessKeyId", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, "SignatureDoesNotMatch", StringComparison.OrdinalIgnoreCase) ||
                status == 401)
            {
                testStatus = ConnectionTestStatus.AuthenticationFailed;
            }
            else if (string.Equals(code, "NoSuchBucket", StringComparison.OrdinalIgnoreCase))
            {
                testStatus = ConnectionTestStatus.BucketNotFound;
            }
            else if (string.Equals(code, "AccessDenied", StringComparison.OrdinalIgnoreCase) || status == 403)
            {
                testStatus = ConnectionTestStatus.AccessDenied;
            }
            else if (status == 404)
            {
                testStatus = ConnectionTestStatus.BucketNotFound;
            }
            else if (status == 429 || status == 408)
            {
                testStatus = ConnectionTestStatus.RateLimited;
            }
            else if (exception is InvalidOperationException)
            {
                testStatus = ConnectionTestStatus.InvalidEndpoint;
            }
            else if (IsTlsFailure(exception))
            {
                testStatus = ConnectionTestStatus.TlsError;
            }
            else if (TransientErrorClassifier.Classify(exception, CancellationToken.None) == ErrorClassification.Transient)
            {
                testStatus = ConnectionTestStatus.NetworkError;
            }

            // A 403 with no bucket-level permission is easy to mistake for a wrong bucket name.
            string detail = friendly.Detail;
            if (testStatus == ConnectionTestStatus.AccessDenied)
            {
                detail += " If the bucket name is correct, re-create the R2 API token with \"Object Read & Write\" permission scoped to this bucket.";
            }

            return new ConnectionTestResult(testStatus, friendly.Headline, detail, friendly.TechnicalDetails);
        }

        private static bool IsTlsFailure(Exception exception)
        {
            int depth = 0;
            Exception current = exception;
            while (current != null && depth < 8)
            {
                if (current is System.Security.Authentication.AuthenticationException) return true;

                System.Net.WebException webException = current as System.Net.WebException;
                if (webException != null &&
                    (webException.Status == System.Net.WebExceptionStatus.TrustFailure ||
                     webException.Status == System.Net.WebExceptionStatus.SecureChannelFailure))
                {
                    return true;
                }

                current = current.InnerException;
                depth++;
            }
            return false;
        }
    }
}
