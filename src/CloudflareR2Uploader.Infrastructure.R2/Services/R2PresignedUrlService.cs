using System;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    internal interface ISystemClock { DateTime UtcNow { get; } }
    internal sealed class SystemClock : ISystemClock { public DateTime UtcNow { get { return DateTime.UtcNow; } } }

    public sealed class R2PresignedUrlService
    {
        private readonly ISystemClock _clock;
        public R2PresignedUrlService() : this(new SystemClock()) { }
        internal R2PresignedUrlService(ISystemClock clock) { _clock = clock; }

        public PresignedUrlResult Create(AppSettings settings, R2Credentials credentials, string objectKey, TimeSpan expiration)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (credentials == null) throw new ArgumentNullException("credentials");
            if (string.IsNullOrEmpty(objectKey)) throw new ArgumentException("An exact object key is required.", "objectKey");
            if (expiration < PresignedUrlOptions.MinimumExpiration || expiration > PresignedUrlOptions.MaximumExpiration)
                throw new ArgumentOutOfRangeException("expiration", "Expiration must be between one second and seven days.");

            DateTime expiresUtc = _clock.UtcNow.Add(expiration);
            using (AmazonS3Client client = R2ClientFactory.CreateClient(settings, credentials))
            {
                string url = client.GetPreSignedURL(new GetPreSignedUrlRequest
                {
                    BucketName = settings.BucketName,
                    Key = objectKey,
                    Verb = HttpVerb.GET,
                    Expires = expiresUtc,
                    Protocol = Protocol.HTTPS
                });
                return new PresignedUrlResult { Url = url, ExpiresLocal = expiresUtc.ToLocalTime() };
            }
        }
    }
}
