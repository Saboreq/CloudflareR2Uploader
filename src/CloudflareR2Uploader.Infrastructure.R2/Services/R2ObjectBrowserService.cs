using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>Performs one read-only, delimiter-based R2 object listing at a time.</summary>
    public sealed class R2ObjectBrowserService
    {
        public const int PageSize = 250;

        private readonly ILoggingService _log;

        public R2ObjectBrowserService(ILoggingService log)
        {
            _log = log;
        }

        public async Task<R2BrowserPage> ListPageAsync(
            AppSettings settings,
            R2Credentials credentials,
            string prefix,
            string continuationToken,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(credentials);

            string normalizedPrefix = R2BrowserPathUtility.NormalizePrefix(prefix);
            AmazonS3Client client = null;
            try
            {
                client = R2ClientFactory.CreateClient(settings, credentials);
                ListObjectsV2Request request = new ListObjectsV2Request
                {
                    BucketName = settings.BucketName,
                    Prefix = normalizedPrefix,
                    Delimiter = "/",
                    MaxKeys = PageSize,
                    ContinuationToken = continuationToken
                };

                ListObjectsV2Response response = await client
                    .ListObjectsV2Async(request, cancellationToken)
                    .ConfigureAwait(false);

                if (_log != null)
                {
                    _log.Info("Browser.List",
                        "Listed bucket '" + settings.BucketName + "' at " +
                        R2ClientFactory.DescribeEndpoint(settings) + " prefix='" + normalizedPrefix +
                        "' (http=" + (int)response.HttpStatusCode + ").");
                }

                return R2BrowserResponseMapper.Map(response, normalizedPrefix, continuationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (_log != null)
                {
                    int status = TransientErrorClassifier.GetStatusCode(ex);
                    string code = TransientErrorClassifier.GetErrorCode(ex);
                    _log.HttpFailure(
                        "Browser.List",
                        status,
                        code,
                        "Listing bucket '" + settings.BucketName + "' at " +
                        R2ClientFactory.DescribeEndpoint(settings) + " prefix='" + normalizedPrefix + "' failed.",
                        null,
                        null);
                }
                throw;
            }
            finally
            {
                if (client != null) client.Dispose();
            }
        }
    }
}
