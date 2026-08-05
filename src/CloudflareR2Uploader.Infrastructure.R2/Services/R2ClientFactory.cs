using System;
using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Builds <see cref="AmazonS3Client"/> instances configured for the Cloudflare R2
    /// S3-compatible API.
    /// </summary>
    public static class R2ClientFactory
    {
        /// <summary>
        /// R2's documented signing region is <c>auto</c>. The S3 compatibility layer also
        /// accepts <c>us-east-1</c> as a fallback for clients that cannot use an arbitrary
        /// region string, but AWSSDK.S3 v3 supports <c>AuthenticationRegion = "auto"</c>.
        /// </summary>
        public const string SigningRegion = "auto";

        /// <summary>The region name R2 itself advertises.</summary>
        public const string R2RegionName = "auto";

        /// <summary>
        /// Generous enough for a 5 GiB part on a slow link, short enough that a wedged
        /// connection is eventually abandoned and retried.
        /// </summary>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(20);

        /// <summary>
        /// Ensures modern TLS is enabled. .NET Framework 4.8 defaults to the OS setting, but
        /// being explicit avoids handshake failures on machines with an old registry policy.
        /// Called once at startup; it is a process-wide setting.
        /// </summary>
        public static void ConfigureTransportSecurity()
        {
            try
            {
                SecurityProtocolType desired = SecurityProtocolType.Tls12;

                // Tls13 exists in .NET Framework 4.8 but is not a compile-time constant on all
                // servicing levels, so it is added by value when the OS supports it.
                const SecurityProtocolType Tls13 = (SecurityProtocolType)12288;
                if (Enum.IsDefined(typeof(SecurityProtocolType), Tls13)) desired |= Tls13;

                ServicePointManager.SecurityProtocol |= desired;

                // R2 uploads several parts at once over the same host.
                if (ServicePointManager.DefaultConnectionLimit < 32)
                    ServicePointManager.DefaultConnectionLimit = 32;

                ServicePointManager.Expect100Continue = false;
            }
            catch (NotSupportedException)
            {
                // Leave the platform default in place if the value is rejected.
            }
        }

        /// <summary>
        /// Creates the S3 configuration for R2: HTTPS-only account endpoint, SigV4, and
        /// timeouts suited to multi-gigabyte parts.
        /// </summary>
        public static AmazonS3Config CreateConfig(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            string serviceUrl = settings.ResolveServiceUrl();
            if (string.IsNullOrEmpty(serviceUrl))
                throw new InvalidOperationException("No R2 endpoint is configured. Enter the Cloudflare Account ID in Settings.");

            Uri uri;
            if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out uri))
                throw new InvalidOperationException("The R2 endpoint is not a valid URL: " + serviceUrl);

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                // Payload signing is disabled for R2, so integrity and confidentiality rest
                // entirely on TLS. Plain HTTP is refused rather than silently allowed.
                throw new InvalidOperationException(
                    "The R2 endpoint must use HTTPS. Uploads send an unsigned payload, which is only safe over TLS.");
            }

            AmazonS3Config config = new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = SigningRegion,
                SignatureVersion = "4",
                UseHttp = false,
                ForcePathStyle = true,          // R2 buckets are addressed as /{bucket}/{key}
                Timeout = RequestTimeout,
                MaxErrorRetry = 0,              // retries are handled by RetryService, not the SDK
                RetryMode = RequestRetryMode.Standard,
                DisableHostPrefixInjection = true
            };

            return config;
        }

        /// <summary>Creates a client. The caller owns it and must dispose it.</summary>
        public static AmazonS3Client CreateClient(AppSettings settings, R2Credentials credentials)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (credentials == null) throw new ArgumentNullException("credentials");

            if (!credentials.IsComplete)
                throw new InvalidOperationException("The R2 Access Key ID and Secret Access Key are both required.");

            AmazonS3Config config = CreateConfig(settings);
            BasicAWSCredentials basicCredentials = new BasicAWSCredentials(
                credentials.AccessKeyId.Trim(),
                credentials.SecretAccessKey);

            return new AmazonS3Client(basicCredentials, config);
        }

        /// <summary>Endpoint host shown in the UI and logs. Contains no secrets.</summary>
        public static string DescribeEndpoint(AppSettings settings)
        {
            if (settings == null) return string.Empty;

            string url = settings.ResolveServiceUrl();
            if (string.IsNullOrEmpty(url)) return "(not configured)";

            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) ? uri.Host : url;
        }
    }
}
