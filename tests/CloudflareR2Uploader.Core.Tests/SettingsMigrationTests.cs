#nullable enable

using System;
using System.IO;
using System.Text;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class SettingsMigrationTests
    {
        /// <summary>
        /// A real settings.json as the 1.0.0 WinForms build wrote it: no schemaVersion, no
        /// WPF fields, and the legacy connection fields mirroring the active profile.
        /// </summary>
        private const string LegacySettingsJson = @"{
  ""accountId"": ""a1b2c3d4e5f60718293a4b5c6d7e8f90"",
  ""bucketName"": ""storage"",
  ""customEndpoint"": """",
  ""publicBaseUrl"": ""https://cdn.example.com"",
  ""keyPrefix"": ""assets/builds"",
  ""multipartThresholdMiB"": 64,
  ""partSizeMiB"": 8,
  ""parallelParts"": 2,
  ""retryAttempts"": 5,
  ""rememberCredentials"": true,
  ""preserveFolderStructure"": true,
  ""overwriteBehavior"": 0,
  ""logRetentionDays"": 14,
  ""bucketProfiles"": [
    {
      ""id"": ""7f1c2b3a4d5e6f708192a3b4c5d6e7f8"",
      ""accountId"": ""a1b2c3d4e5f60718293a4b5c6d7e8f90"",
      ""bucketName"": ""storage"",
      ""customEndpoint"": """",
      ""publicBaseUrl"": ""https://cdn.example.com""
    },
    {
      ""id"": ""8e2d3c4b5a6f70819243b5c6d7e8f901"",
      ""accountId"": ""a1b2c3d4e5f60718293a4b5c6d7e8f90"",
      ""bucketName"": ""gfnos"",
      ""customEndpoint"": """",
      ""publicBaseUrl"": """"
    }
  ],
  ""activeBucketProfileId"": ""7f1c2b3a4d5e6f708192a3b4c5d6e7f8"",
  ""browserSortColumn"": 2,
  ""browserSortDirection"": 1,
  ""detailsPanelVisible"": true,
  ""detailsPanelWidth"": 420,
  ""detailsSelectedTab"": 1,
  ""closeToTray"": true,
  ""minimizeToTray"": false,
  ""startWithWindows"": true,
  ""startMinimized"": true,
  ""showTrayNotifications"": false,
  ""checkForUpdatesAutomatically"": false
}";

        [TestMethod]
        public void Migrate_TreatsAFileWithoutSchemaVersionAsSchemaOne()
        {
            AppSettings settings = new AppSettings { SchemaVersion = 0 };

            SettingsMigrationResult result = SettingsMigrator.Migrate(settings);

            Assert.AreEqual(1, result.FromVersion);
            Assert.AreEqual(AppSettings.CurrentSchemaVersion, result.ToVersion);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        }

        [TestMethod]
        public void Migrate_IsIdempotent()
        {
            AppSettings settings = new AppSettings { SchemaVersion = 0, PublicBaseUrl = "https://cdn.example.com" };
            SettingsMigrator.Migrate(settings);

            SettingsMigrationResult second = SettingsMigrator.Migrate(settings);

            Assert.IsFalse(second.Changed);
            Assert.AreEqual(AppSettings.CurrentSchemaVersion, second.FromVersion);
            Assert.AreEqual(AppSettings.CurrentSchemaVersion, second.ToVersion);
        }

        [TestMethod]
        public void Migrate_NeverDowngradesAFileWrittenByANewerBuild()
        {
            int futureVersion = AppSettings.CurrentSchemaVersion + 5;
            AppSettings settings = new AppSettings { SchemaVersion = futureVersion };

            SettingsMigrationResult result = SettingsMigrator.Migrate(settings);

            Assert.AreEqual(futureVersion, result.ToVersion);
            Assert.AreEqual(futureVersion, settings.SchemaVersion);
        }

        [TestMethod]
        public void Migrate_SeedsWpfFieldsWithTheWinFormsBehaviour()
        {
            AppSettings settings = new AppSettings
            {
                SchemaVersion = 0,
                PublicBaseUrl = "https://cdn.example.com",
                ShowTrayNotifications = false,
                CheckForUpdatesAutomatically = true
            };

            SettingsMigrator.Migrate(settings);

            Assert.AreEqual(AppTheme.Dark, settings.Theme, "the WinForms build was dark only");
            Assert.AreEqual(24, settings.TemporaryLinkExpiryHours);
            Assert.IsTrue(settings.PreferPublicUrls, "a public base URL was configured");
            Assert.IsTrue(settings.VerifyETagAfterUpload);
            Assert.IsTrue(settings.ContinueTransfersWhileMinimized);
            Assert.IsTrue(settings.ConfirmDeletes);
            Assert.IsFalse(settings.ForgetCredentialsOnExit);
            Assert.AreEqual(30, settings.ActivityRetentionDays);

            Assert.IsFalse(settings.NotifyOnTransferComplete, "the aggregate tray switch was off");
            Assert.IsFalse(settings.NotifyOnTransferFailure);
            Assert.IsFalse(settings.NotifyOnConnectionError);
            Assert.IsTrue(settings.NotifyOnUpdateAvailable, "automatic update checks were on");
        }

        [TestMethod]
        public void Migrate_LeavesPreferPublicUrlsOffWithoutAPublicBaseUrl()
        {
            AppSettings settings = new AppSettings { SchemaVersion = 0, PublicBaseUrl = string.Empty };

            SettingsMigrator.Migrate(settings);

            Assert.IsFalse(settings.PreferPublicUrls);
        }

        [TestMethod]
        public void Load_MigratesARealLegacyFileWithoutLosingAnyExistingValue()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("settings.json");
                File.WriteAllText(path, LegacySettingsJson, new UTF8Encoding(false));

                AppSettings settings = new SettingsService(null, path).Load();

                // Nothing the old build stored may be lost or reinterpreted.
                Assert.AreEqual("a1b2c3d4e5f60718293a4b5c6d7e8f90", settings.AccountId);
                Assert.AreEqual("storage", settings.BucketName);
                Assert.AreEqual("https://cdn.example.com", settings.PublicBaseUrl);
                Assert.AreEqual("assets/builds", settings.KeyPrefix);
                Assert.AreEqual(64, settings.MultipartThresholdMiB);
                Assert.AreEqual(8, settings.PartSizeMiB);
                Assert.AreEqual(2, settings.ParallelParts);
                Assert.AreEqual(5, settings.RetryAttempts);
                Assert.IsTrue(settings.RememberCredentials);
                Assert.AreEqual(BrowserSortColumn.Size, settings.BrowserSortColumn);
                Assert.AreEqual(BrowserSortDirection.Descending, settings.BrowserSortDirection);
                Assert.AreEqual(420, settings.DetailsPanelWidth);
                Assert.IsTrue(settings.StartWithWindows);
                Assert.IsTrue(settings.StartMinimized);
                Assert.IsFalse(settings.MinimizeToTray);

                // Both profiles survive, and the active one is still selected.
                Assert.AreEqual(2, settings.BucketProfiles.Count);
                Assert.AreEqual("7f1c2b3a4d5e6f708192a3b4c5d6e7f8", settings.ActiveBucketProfileId);
                Assert.AreEqual("gfnos", settings.BucketProfiles[1].BucketName);

                Assert.AreEqual(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
            }
        }

        [TestMethod]
        public void Load_KeepsACopyOfThePreMigrationFileSoADowngradeStaysPossible()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("settings.json");
                File.WriteAllText(path, LegacySettingsJson, new UTF8Encoding(false));

                new SettingsService(null, path).Load();

                string backup = path + ".schema1.bak";
                Assert.IsTrue(File.Exists(backup), "the original file should be preserved once");
                StringAssert.Contains(File.ReadAllText(backup), "\"activeBucketProfileId\"");
            }
        }

        [TestMethod]
        public void SaveThenLoad_RoundTripsEverySchemaTwoField()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("settings.json");
                SettingsService service = new SettingsService(null, path);

                AppSettings written = new AppSettings
                {
                    AccountId = "a1b2c3d4e5f60718293a4b5c6d7e8f90",
                    BucketName = "storage",
                    Theme = AppTheme.System,
                    TemporaryLinkExpiryHours = 72,
                    PreferPublicUrls = true,
                    VerifyETagAfterUpload = false,
                    ContinueTransfersWhileMinimized = false,
                    ConfirmDeletes = false,
                    ForgetCredentialsOnExit = true,
                    ActivityRetentionDays = 90,
                    NotifyOnTransferComplete = false,
                    NotifyOnTransferFailure = true,
                    NotifyOnConnectionError = false,
                    NotifyOnUpdateAvailable = true,
                    SchemaVersion = AppSettings.CurrentSchemaVersion
                };

                Assert.IsTrue(service.Save(written));
                AppSettings read = service.Load();

                Assert.AreEqual(AppTheme.System, read.Theme);
                Assert.AreEqual(72, read.TemporaryLinkExpiryHours);
                Assert.IsTrue(read.PreferPublicUrls);
                Assert.IsFalse(read.VerifyETagAfterUpload);
                Assert.IsFalse(read.ContinueTransfersWhileMinimized);
                Assert.IsFalse(read.ConfirmDeletes);
                Assert.IsTrue(read.ForgetCredentialsOnExit);
                Assert.AreEqual(90, read.ActivityRetentionDays);
                Assert.IsFalse(read.NotifyOnTransferComplete);
                Assert.IsTrue(read.NotifyOnTransferFailure);
                Assert.IsFalse(read.NotifyOnConnectionError);
                Assert.IsTrue(read.NotifyOnUpdateAvailable);
            }
        }

        [TestMethod]
        public void Clamp_ForcesTemporaryLinkExpiryInsideTheRangeR2Accepts()
        {
            AppSettings settings = new AppSettings { TemporaryLinkExpiryHours = 24 * 30 };
            settings.Clamp();
            Assert.AreEqual(AppSettings.MaxTemporaryLinkExpiryHours, settings.TemporaryLinkExpiryHours);

            settings.TemporaryLinkExpiryHours = -4;
            settings.Clamp();
            Assert.AreEqual(24, settings.TemporaryLinkExpiryHours, "a nonsense value falls back to the default");

            Assert.AreEqual(TimeSpan.FromDays(7), TimeSpan.FromHours(AppSettings.MaxTemporaryLinkExpiryHours));
        }

        [TestMethod]
        public void Clamp_ForcesActivityRetentionInsideTheSupportedRange()
        {
            AppSettings settings = new AppSettings { ActivityRetentionDays = 5000 };
            settings.Clamp();
            Assert.AreEqual(AppSettings.MaxActivityRetentionDays, settings.ActivityRetentionDays);
        }

        [TestMethod]
        public void Clone_CarriesEverySchemaTwoField()
        {
            AppSettings settings = new AppSettings
            {
                Theme = AppTheme.Light,
                TemporaryLinkExpiryHours = 6,
                PreferPublicUrls = true,
                VerifyETagAfterUpload = false,
                ContinueTransfersWhileMinimized = false,
                ConfirmDeletes = false,
                ForgetCredentialsOnExit = true,
                ActivityRetentionDays = 7,
                NotifyOnTransferComplete = false,
                NotifyOnTransferFailure = false,
                NotifyOnConnectionError = false,
                NotifyOnUpdateAvailable = false,
                SchemaVersion = AppSettings.CurrentSchemaVersion
            };

            AppSettings clone = settings.Clone();

            Assert.AreEqual(AppTheme.Light, clone.Theme);
            Assert.AreEqual(6, clone.TemporaryLinkExpiryHours);
            Assert.IsTrue(clone.PreferPublicUrls);
            Assert.IsFalse(clone.VerifyETagAfterUpload);
            Assert.IsFalse(clone.ContinueTransfersWhileMinimized);
            Assert.IsFalse(clone.ConfirmDeletes);
            Assert.IsTrue(clone.ForgetCredentialsOnExit);
            Assert.AreEqual(7, clone.ActivityRetentionDays);
            Assert.IsFalse(clone.NotifyOnUpdateAvailable);
            Assert.AreEqual(AppSettings.CurrentSchemaVersion, clone.SchemaVersion);
        }
    }
}
