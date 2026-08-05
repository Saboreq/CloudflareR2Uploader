using System;
using System.Runtime.Serialization;

namespace CloudflareR2Uploader.Models
{
    /// <summary>
    /// Non-secret connection details for one R2 upload target. Credentials are stored
    /// separately by <c>CredentialProtectionService</c> and are keyed by <see cref="Id"/>.
    /// </summary>
    [DataContract(Name = "R2BucketProfile", Namespace = "")]
    public sealed class R2BucketProfile
    {
        public R2BucketProfile()
        {
            Id = Guid.NewGuid().ToString("N");
            AccountId = string.Empty;
            BucketName = string.Empty;
            CustomEndpoint = string.Empty;
            PublicBaseUrl = string.Empty;
        }

        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; }

        [DataMember(Name = "accountId", Order = 2)]
        public string AccountId { get; set; }

        [DataMember(Name = "bucketName", Order = 3)]
        public string BucketName { get; set; }

        [DataMember(Name = "customEndpoint", Order = 4)]
        public string CustomEndpoint { get; set; }

        [DataMember(Name = "publicBaseUrl", Order = 5)]
        public string PublicBaseUrl { get; set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(BucketName)) return BucketName.Trim();
                if (!string.IsNullOrWhiteSpace(AccountId)) return "Bucket on " + AccountId.Trim();
                return "New bucket";
            }
        }

        public void Clamp()
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
            AccountId = (AccountId ?? string.Empty).Trim();
            BucketName = (BucketName ?? string.Empty).Trim();
            CustomEndpoint = (CustomEndpoint ?? string.Empty).Trim();
            PublicBaseUrl = (PublicBaseUrl ?? string.Empty).Trim();
        }

        public R2BucketProfile Clone()
        {
            return new R2BucketProfile
            {
                Id = Id,
                AccountId = AccountId,
                BucketName = BucketName,
                CustomEndpoint = CustomEndpoint,
                PublicBaseUrl = PublicBaseUrl
            };
        }
    }
}
