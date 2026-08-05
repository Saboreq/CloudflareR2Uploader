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

        /// <summary>
        /// Schema written by this build. A file without <c>schemaVersion</c> was produced by
        /// a build older than the WPF front end and is treated as version 1.
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        public const int MinTemporaryLinkExpiryHours = 1;

        /// <summary>R2 refuses a presigned URL that lives longer than seven days.</summary>
        public const int MaxTemporaryLinkExpiryHours = 24 * 7;

        public const int MinActivityRetentionDays = 1;
        public const int MaxActivityRetentionDays = 365;

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
            BrowserSortColumnValue = (int)BrowserSortColumn.Name;
            BrowserSortDirectionValue = (int)BrowserSortDirection.Ascending;
            DetailsPanelVisible = true;
            DetailsPanelWidth = 360;
            DetailsSelectedTab = 0;
            CloseToTray = true;
            MinimizeToTray = true;
            StartWithWindows = false;
            StartMinimized = false;
            ShowTrayNotifications = true;
            CheckForUpdatesAutomatically = true;

            // Defaults below reproduce the behaviour of the pre-WPF build exactly, so a
            // settings file written by that build behaves identically after migration.
            SchemaVersion = 0;
            ThemeValue = (int)AppTheme.Dark;
            TemporaryLinkExpiryHours = 24;
            PreferPublicUrls = false;
            VerifyETagAfterUpload = true;
            ContinueTransfersWhileMinimized = true;
            ConfirmDeletes = true;
            ForgetCredentialsOnExit = false;
            ActivityRetentionDays = 30;
            NotifyOnTransferComplete = true;
            NotifyOnTransferFailure = true;
            NotifyOnConnectionError = true;
            NotifyOnUpdateAvailable = true;
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

        [DataMember(Name = "browserSortColumn", Order = 16)]
        public int BrowserSortColumnValue { get; set; }

        [DataMember(Name = "browserSortDirection", Order = 17)]
        public int BrowserSortDirectionValue { get; set; }

        [DataMember(Name = "detailsPanelVisible", Order = 18)]
        public bool DetailsPanelVisible { get; set; }

        [DataMember(Name = "detailsPanelWidth", Order = 19)]
        public int DetailsPanelWidth { get; set; }

        [DataMember(Name = "detailsSelectedTab", Order = 20)]
        public int DetailsSelectedTab { get; set; }

        [DataMember(Name = "closeToTray", Order = 21)]
        public bool CloseToTray { get; set; }

        [DataMember(Name = "minimizeToTray", Order = 22)]
        public bool MinimizeToTray { get; set; }

        [DataMember(Name = "startWithWindows", Order = 23)]
        public bool StartWithWindows { get; set; }

        [DataMember(Name = "startMinimized", Order = 24)]
        public bool StartMinimized { get; set; }

        [DataMember(Name = "showTrayNotifications", Order = 25)]
        public bool ShowTrayNotifications { get; set; }

        [DataMember(Name = "checkForUpdatesAutomatically", Order = 26)]
        public bool CheckForUpdatesAutomatically { get; set; }

        // ------------------------------------------------------------------ schema 2 fields
        //
        // Everything below was introduced with the WPF front end. The WinForms build shares
        // this type, so it round-trips these members untouched and neither front end loses
        // the other's preferences.

        [DataMember(Name = "schemaVersion", Order = 27)]
        public int SchemaVersion { get; set; }

        /// <summary>Stored as an int so an unknown future theme cannot break deserialisation.</summary>
        [DataMember(Name = "theme", Order = 28)]
        public int ThemeValue { get; set; }

        [DataMember(Name = "temporaryLinkExpiryHours", Order = 29)]
        public int TemporaryLinkExpiryHours { get; set; }

        /// <summary>Copy the public CDN URL rather than a signed endpoint URL where both exist.</summary>
        [DataMember(Name = "preferPublicUrls", Order = 30)]
        public bool PreferPublicUrls { get; set; }

        [DataMember(Name = "verifyETagAfterUpload", Order = 31)]
        public bool VerifyETagAfterUpload { get; set; }

        [DataMember(Name = "continueTransfersWhileMinimized", Order = 32)]
        public bool ContinueTransfersWhileMinimized { get; set; }

        [DataMember(Name = "confirmDeletes", Order = 33)]
        public bool ConfirmDeletes { get; set; }

        /// <summary>Clears the in-memory and on-disk credential vault when the app exits.</summary>
        [DataMember(Name = "forgetCredentialsOnExit", Order = 34)]
        public bool ForgetCredentialsOnExit { get; set; }

        [DataMember(Name = "activityRetentionDays", Order = 35)]
        public int ActivityRetentionDays { get; set; }

        [DataMember(Name = "notifyOnTransferComplete", Order = 36)]
        public bool NotifyOnTransferComplete { get; set; }

        [DataMember(Name = "notifyOnTransferFailure", Order = 37)]
        public bool NotifyOnTransferFailure { get; set; }

        [DataMember(Name = "notifyOnConnectionError", Order = 38)]
        public bool NotifyOnConnectionError { get; set; }

        [DataMember(Name = "notifyOnUpdateAvailable", Order = 39)]
        public bool NotifyOnUpdateAvailable { get; set; }

        public AppTheme Theme
        {
            get { return Enum.IsDefined(typeof(AppTheme), ThemeValue) ? (AppTheme)ThemeValue : AppTheme.Dark; }
            set { ThemeValue = (int)value; }
        }

        public TimeSpan TemporaryLinkExpiry
        {
            get { return TimeSpan.FromHours(TemporaryLinkExpiryHours); }
        }

        public BrowserSortColumn BrowserSortColumn
        {
            get { return Enum.IsDefined(typeof(BrowserSortColumn), BrowserSortColumnValue) ? (BrowserSortColumn)BrowserSortColumnValue : BrowserSortColumn.Name; }
            set { BrowserSortColumnValue = (int)value; }
        }

        public BrowserSortDirection BrowserSortDirection
        {
            get { return Enum.IsDefined(typeof(BrowserSortDirection), BrowserSortDirectionValue) ? (BrowserSortDirection)BrowserSortDirectionValue : BrowserSortDirection.Ascending; }
            set { BrowserSortDirectionValue = (int)value; }
        }

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
            if (!Enum.IsDefined(typeof(BrowserSortColumn), BrowserSortColumnValue))
                BrowserSortColumnValue = (int)BrowserSortColumn.Name;
            if (!Enum.IsDefined(typeof(BrowserSortDirection), BrowserSortDirectionValue))
                BrowserSortDirectionValue = (int)BrowserSortDirection.Ascending;
            DetailsPanelWidth = Clamp(DetailsPanelWidth, 240, 700, 360);
            if (DetailsSelectedTab < 0 || DetailsSelectedTab > 1) DetailsSelectedTab = 0;

            if (!Enum.IsDefined(typeof(AppTheme), ThemeValue)) ThemeValue = (int)AppTheme.Dark;
            TemporaryLinkExpiryHours = Clamp(
                TemporaryLinkExpiryHours, MinTemporaryLinkExpiryHours, MaxTemporaryLinkExpiryHours, 24);
            ActivityRetentionDays = Clamp(
                ActivityRetentionDays, MinActivityRetentionDays, MaxActivityRetentionDays, 30);
            // A version higher than this build's is deliberately left alone: SettingsMigrator
            // must be able to see it and refuse to downgrade the file.
            if (SchemaVersion < 0) SchemaVersion = 0;

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
                BrowserSortColumnValue = BrowserSortColumnValue,
                BrowserSortDirectionValue = BrowserSortDirectionValue,
                DetailsPanelVisible = DetailsPanelVisible,
                DetailsPanelWidth = DetailsPanelWidth,
                DetailsSelectedTab = DetailsSelectedTab,
                CloseToTray = CloseToTray,
                MinimizeToTray = MinimizeToTray,
                StartWithWindows = StartWithWindows,
                StartMinimized = StartMinimized,
                ShowTrayNotifications = ShowTrayNotifications,
                CheckForUpdatesAutomatically = CheckForUpdatesAutomatically,
                SchemaVersion = SchemaVersion,
                ThemeValue = ThemeValue,
                TemporaryLinkExpiryHours = TemporaryLinkExpiryHours,
                PreferPublicUrls = PreferPublicUrls,
                VerifyETagAfterUpload = VerifyETagAfterUpload,
                ContinueTransfersWhileMinimized = ContinueTransfersWhileMinimized,
                ConfirmDeletes = ConfirmDeletes,
                ForgetCredentialsOnExit = ForgetCredentialsOnExit,
                ActivityRetentionDays = ActivityRetentionDays,
                NotifyOnTransferComplete = NotifyOnTransferComplete,
                NotifyOnTransferFailure = NotifyOnTransferFailure,
                NotifyOnConnectionError = NotifyOnConnectionError,
                NotifyOnUpdateAvailable = NotifyOnUpdateAvailable,
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
