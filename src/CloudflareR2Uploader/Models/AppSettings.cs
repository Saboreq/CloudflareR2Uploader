using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Models
{
    /// <summary>
    /// Non-secret application settings. This is the exact shape written to
    /// <c>%LocalAppData%\CloudflareR2Uploader\settings.json</c>.
    /// It contains no credential fields at all, by design.
    /// </summary>
    [DataContract(Name = "AppSettings", Namespace = "")]
    public sealed class AppSettings
    {
        public const int MinParallelParts = 1;
        public const int MaxParallelParts = 8;
        public const int MinRetryAttempts = 1;
        public const int MaxRetryAttempts = 10;
        public const int MinThresholdMiB = 5;
        public const int MaxThresholdMiB = 5 * 1024;
        public const int MinPartSizeMiB = 5;
        public const int MaxPartSizeMiB = 5 * 1024;

        public AppSettings()
        {
            ApplyDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            // Fields missing from an older settings file keep their defaults.
            ApplyDefaults();
        }

        private void ApplyDefaults()
        {
            AccountId = string.Empty;
            BucketName = string.Empty;
            CustomEndpoint = string.Empty;
            PublicBaseUrl = string.Empty;
            KeyPrefix = string.Empty;
            MultipartThresholdMiB = 100;
            PartSizeMiB = 64;
            ParallelParts = 4;
            RetryAttempts = 5;
            RememberCredentials = false;
            PreserveFolderStructure = true;
            OverwriteBehaviorValue = (int)OverwriteBehavior.Ask;
            LogRetentionDays = 14;
            BucketProfiles = new List<R2BucketProfile>();
            ActiveBucketProfileId = string.Empty;
        }

        [DataMember(Name = "accountId", Order = 1)]
        public string AccountId { get; set; }

        [DataMember(Name = "bucketName", Order = 2)]
        public string BucketName { get; set; }

        /// <summary>Normally empty; overrides the derived <c>{accountId}.r2.cloudflarestorage.com</c> URL.</summary>
        [DataMember(Name = "customEndpoint", Order = 3)]
        public string CustomEndpoint { get; set; }

        /// <summary>Display only. Used to build a shareable URL; never used for uploading.</summary>
        [DataMember(Name = "publicBaseUrl", Order = 4)]
        public string PublicBaseUrl { get; set; }

        [DataMember(Name = "keyPrefix", Order = 5)]
        public string KeyPrefix { get; set; }

        [DataMember(Name = "multipartThresholdMiB", Order = 6)]
        public int MultipartThresholdMiB { get; set; }

        [DataMember(Name = "partSizeMiB", Order = 7)]
        public int PartSizeMiB { get; set; }

        [DataMember(Name = "parallelParts", Order = 8)]
        public int ParallelParts { get; set; }

        [DataMember(Name = "retryAttempts", Order = 9)]
        public int RetryAttempts { get; set; }

        [DataMember(Name = "rememberCredentials", Order = 10)]
        public bool RememberCredentials { get; set; }

        [DataMember(Name = "preserveFolderStructure", Order = 11)]
        public bool PreserveFolderStructure { get; set; }

        /// <summary>
        /// Stored as an int so an unknown future value in the file cannot make deserialisation
        /// throw. Use <see cref="OverwriteBehavior"/> in code.
        /// </summary>
        [DataMember(Name = "overwriteBehavior", Order = 12)]
        public int OverwriteBehaviorValue { get; set; }

        [DataMember(Name = "logRetentionDays", Order = 13)]
        public int LogRetentionDays { get; set; }

        /// <summary>
        /// Saved upload targets. These records deliberately contain no access key or secret.
        /// The four legacy connection fields above mirror the active profile so older app
        /// builds and the upload services continue to have a stable settings contract.
        /// </summary>
        [DataMember(Name = "bucketProfiles", Order = 14)]
        public List<R2BucketProfile> BucketProfiles { get; set; }

        [DataMember(Name = "activeBucketProfileId", Order = 15)]
        public string ActiveBucketProfileId { get; set; }

        public OverwriteBehavior OverwriteBehavior
        {
            get
            {
                return Enum.IsDefined(typeof(OverwriteBehavior), OverwriteBehaviorValue)
                    ? (OverwriteBehavior)OverwriteBehaviorValue
                    : OverwriteBehavior.Ask;
            }
            set { OverwriteBehaviorValue = (int)value; }
        }

        public long MultipartThresholdBytes { get { return (long)MultipartThresholdMiB * MultipartCalculator.MiB; } }

        public long PartSizeBytes { get { return (long)PartSizeMiB * MultipartCalculator.MiB; } }

        /// <summary>The endpoint the S3 client will use, honouring any override.</summary>
        public string ResolveServiceUrl()
        {
            if (!string.IsNullOrWhiteSpace(CustomEndpoint))
            {
                string custom = CustomEndpoint.Trim().TrimEnd('/');
                if (custom.IndexOf("://", StringComparison.Ordinal) < 0) custom = "https://" + custom;
                return custom;
            }

            string account = (AccountId ?? string.Empty).Trim();
            if (account.Length == 0) return string.Empty;

            return "https://" + account + ".r2.cloudflarestorage.com";
        }

        /// <summary>Forces every value back into its supported range after loading or editing.</summary>
        public void Clamp()
        {
            AccountId = (AccountId ?? string.Empty).Trim();
            BucketName = (BucketName ?? string.Empty).Trim();
            CustomEndpoint = (CustomEndpoint ?? string.Empty).Trim();
            PublicBaseUrl = (PublicBaseUrl ?? string.Empty).Trim();
            KeyPrefix = ObjectKeyUtility.Normalize(KeyPrefix ?? string.Empty);

            MultipartThresholdMiB = Clamp(MultipartThresholdMiB, MinThresholdMiB, MaxThresholdMiB, 100);
            PartSizeMiB = Clamp(PartSizeMiB, MinPartSizeMiB, MaxPartSizeMiB, 64);
            ParallelParts = Clamp(ParallelParts, MinParallelParts, MaxParallelParts, 4);
            RetryAttempts = Clamp(RetryAttempts, MinRetryAttempts, MaxRetryAttempts, 5);
            LogRetentionDays = Clamp(LogRetentionDays, 1, 365, 14);

            if (!Enum.IsDefined(typeof(OverwriteBehavior), OverwriteBehaviorValue))
                OverwriteBehaviorValue = (int)OverwriteBehavior.Ask;

            NormalizeBucketProfiles();
        }

        /// <summary>
        /// Migrates legacy single-bucket settings and makes the selected profile authoritative.
        /// Calling this method is safe more than once.
        /// </summary>
        public void NormalizeBucketProfiles()
        {
            if (BucketProfiles == null) BucketProfiles = new List<R2BucketProfile>();

            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = BucketProfiles.Count - 1; i >= 0; i--)
            {
                R2BucketProfile profile = BucketProfiles[i];
                if (profile == null)
                {
                    BucketProfiles.RemoveAt(i);
                    continue;
                }

                profile.Clamp();
                while (!ids.Add(profile.Id))
                    profile.Id = Guid.NewGuid().ToString("N");
            }

            if (BucketProfiles.Count == 0)
            {
                R2BucketProfile migrated = new R2BucketProfile
                {
                    AccountId = AccountId,
                    BucketName = BucketName,
                    CustomEndpoint = CustomEndpoint,
                    PublicBaseUrl = PublicBaseUrl
                };
                migrated.Clamp();
                BucketProfiles.Add(migrated);
            }

            R2BucketProfile active = GetBucketProfile(ActiveBucketProfileId);
            if (active == null) active = BucketProfiles[0];

            ActiveBucketProfileId = active.Id;
            ApplyProfileToLegacyFields(active);
        }

        public R2BucketProfile GetActiveBucketProfile()
        {
            NormalizeBucketProfiles();
            return GetBucketProfile(ActiveBucketProfileId);
        }

        public R2BucketProfile GetBucketProfile(string profileId)
        {
            if (BucketProfiles == null || string.IsNullOrWhiteSpace(profileId)) return null;

            foreach (R2BucketProfile profile in BucketProfiles)
            {
                if (profile != null &&
                    string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
                    return profile;
            }

            return null;
        }

        public bool SelectBucketProfile(string profileId)
        {
            NormalizeBucketProfiles();
            R2BucketProfile profile = GetBucketProfile(profileId);
            if (profile == null) return false;

            ActiveBucketProfileId = profile.Id;
            ApplyProfileToLegacyFields(profile);
            return true;
        }

        public void SaveLegacyFieldsToActiveProfile()
        {
            if (BucketProfiles == null || BucketProfiles.Count == 0)
            {
                NormalizeBucketProfiles();
                return;
            }

            R2BucketProfile profile = GetBucketProfile(ActiveBucketProfileId);
            if (profile == null) return;

            profile.AccountId = (AccountId ?? string.Empty).Trim();
            profile.BucketName = (BucketName ?? string.Empty).Trim();
            profile.CustomEndpoint = (CustomEndpoint ?? string.Empty).Trim();
            profile.PublicBaseUrl = (PublicBaseUrl ?? string.Empty).Trim();
            profile.Clamp();
        }

        private void ApplyProfileToLegacyFields(R2BucketProfile profile)
        {
            AccountId = profile.AccountId;
            BucketName = profile.BucketName;
            CustomEndpoint = profile.CustomEndpoint;
            PublicBaseUrl = profile.PublicBaseUrl;
        }

        private static int Clamp(int value, int min, int max, int fallback)
        {
            if (value <= 0) return fallback;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public AppSettings Clone()
        {
            AppSettings clone = new AppSettings
            {
                AccountId = AccountId,
                BucketName = BucketName,
                CustomEndpoint = CustomEndpoint,
                PublicBaseUrl = PublicBaseUrl,
                KeyPrefix = KeyPrefix,
                MultipartThresholdMiB = MultipartThresholdMiB,
                PartSizeMiB = PartSizeMiB,
                ParallelParts = ParallelParts,
                RetryAttempts = RetryAttempts,
                RememberCredentials = RememberCredentials,
                PreserveFolderStructure = PreserveFolderStructure,
                OverwriteBehaviorValue = OverwriteBehaviorValue,
                LogRetentionDays = LogRetentionDays,
                ActiveBucketProfileId = ActiveBucketProfileId,
                BucketProfiles = new List<R2BucketProfile>()
            };

            if (BucketProfiles != null)
            {
                foreach (R2BucketProfile profile in BucketProfiles)
                    if (profile != null) clone.BucketProfiles.Add(profile.Clone());
            }

            return clone;
        }
    }
}
