using System.IO;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class SettingsServiceTests
    {
        [TestMethod]
        public void NewSettings_DefaultClampAndCloneIncludeReleasePreferences()
        {
            AppSettings settings = new AppSettings { BrowserSortColumnValue = 99, BrowserSortDirectionValue = 99, DetailsPanelWidth = 9999, DetailsSelectedTab = 99 };
            settings.Clamp();
            Assert.AreEqual(BrowserSortColumn.Name, settings.BrowserSortColumn);
            Assert.AreEqual(BrowserSortDirection.Ascending, settings.BrowserSortDirection);
            Assert.AreEqual(700, settings.DetailsPanelWidth);
            Assert.AreEqual(0, settings.DetailsSelectedTab);
            Assert.IsTrue(settings.CloseToTray); Assert.IsTrue(settings.MinimizeToTray);
            Assert.IsTrue(settings.CheckForUpdatesAutomatically);
            AppSettings clone = settings.Clone(); Assert.AreEqual(settings.DetailsPanelWidth, clone.DetailsPanelWidth); Assert.AreEqual(settings.ShowTrayNotifications, clone.ShowTrayNotifications); Assert.AreEqual(settings.CheckForUpdatesAutomatically, clone.CheckForUpdatesAutomatically);
        }
        [TestMethod]
        public void SaveAndLoad_RoundTripsNonSecretSettingsOnly()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("settings.json");
                SettingsService service = new SettingsService(null, path);
                AppSettings expected = new AppSettings
                {
                    AccountId = "account-id",
                    BucketName = "bucket",
                    KeyPrefix = @"//releases\stable//",
                    MultipartThresholdMiB = 250,
                    PartSizeMiB = 80,
                    ParallelParts = 6,
                    RetryAttempts = 7,
                    RememberCredentials = true
                };

                Assert.IsTrue(service.Save(expected));
                string json = File.ReadAllText(path);
                Assert.IsFalse(json.Contains("accessKey"));
                Assert.IsFalse(json.Contains("secret"));

                AppSettings actual = service.Load();
                Assert.AreEqual("account-id", actual.AccountId);
                Assert.AreEqual("bucket", actual.BucketName);
                Assert.AreEqual("releases/stable", actual.KeyPrefix);
                Assert.AreEqual(250, actual.MultipartThresholdMiB);
                Assert.AreEqual(80, actual.PartSizeMiB);
                Assert.AreEqual(6, actual.ParallelParts);
                Assert.AreEqual(7, actual.RetryAttempts);
                Assert.IsTrue(actual.RememberCredentials);
            }
        }

        // Break caught: a replacement failure after serialization must not delete the last readable settings bytes.
        [TestMethod]
        public void Save_WhenAtomicReplacementFails_ReturnsFalseAndPreservesExistingBytes()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("settings.json");
                byte[] oldBytes = { 0x01, 0x13, 0x37, 0xF0 };
                File.WriteAllBytes(path, oldBytes);
                SettingsService service = new SettingsService(null, path, new AtomicFileWriter(new ThrowingCommitter()));

                Assert.IsFalse(service.Save(new AppSettings { BucketName = "new-bucket" }));

                CollectionAssert.AreEqual(oldBytes, File.ReadAllBytes(path));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.tmp").Length);
            }
        }

        [TestMethod]
        public void Load_CorruptFileReturnsDefaultsAndQuarantinesInput()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("settings.json");
                File.WriteAllText(path, "{ definitely not json");

                AppSettings settings = new SettingsService(null, path).Load();

                Assert.AreEqual(100, settings.MultipartThresholdMiB);
                Assert.IsFalse(File.Exists(path));
                Assert.IsTrue(File.Exists(path + ".invalid"));
            }
        }

        [TestMethod]
        public void Load_LegacyConnectionMigratesToOneActiveBucketProfile()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("settings.json");
                File.WriteAllText(path,
                    "{\"accountId\":\"legacy-account\",\"bucketName\":\"legacy-bucket\"," +
                    "\"customEndpoint\":\"\",\"publicBaseUrl\":\"\",\"multipartThresholdMiB\":100," +
                    "\"partSizeMiB\":64,\"parallelParts\":4,\"retryAttempts\":5," +
                    "\"rememberCredentials\":false,\"preserveFolderStructure\":true," +
                    "\"overwriteBehavior\":0,\"logRetentionDays\":14}");

                AppSettings actual = new SettingsService(null, path).Load();

                Assert.AreEqual(1, actual.BucketProfiles.Count);
                Assert.AreEqual(actual.BucketProfiles[0].Id, actual.ActiveBucketProfileId);
                Assert.AreEqual("legacy-account", actual.BucketProfiles[0].AccountId);
                Assert.AreEqual("legacy-bucket", actual.BucketProfiles[0].BucketName);
            }
        }

        [TestMethod]
        public void SaveAndLoad_PreservesMultipleProfilesWithoutSecrets()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("settings.json");
                SettingsService service = new SettingsService(null, path);
                AppSettings expected = new AppSettings();
                expected.Clamp();
                expected.BucketProfiles[0].AccountId = "account-a";
                expected.BucketProfiles[0].BucketName = "bucket-a";
                R2BucketProfile second = new R2BucketProfile
                {
                    AccountId = "account-b",
                    BucketName = "bucket-b"
                };
                expected.BucketProfiles.Add(second);
                expected.SelectBucketProfile(second.Id);

                Assert.IsTrue(service.Save(expected));
                string json = File.ReadAllText(path);
                Assert.IsFalse(json.Contains("accessKey"));
                Assert.IsFalse(json.Contains("secret"));

                AppSettings actual = service.Load();
                Assert.AreEqual(2, actual.BucketProfiles.Count);
                Assert.AreEqual("bucket-b", actual.BucketName);
                Assert.AreEqual(second.Id, actual.ActiveBucketProfileId);
            }
        }

        private sealed class ThrowingCommitter : IAtomicFileCommitter
        {
            public void Commit(string temporaryPath, string destinationPath) =>
                throw new IOException("synthetic replacement failure");
        }
    }
}
