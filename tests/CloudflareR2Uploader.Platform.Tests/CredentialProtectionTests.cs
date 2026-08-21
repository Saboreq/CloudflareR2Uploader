using System.IO;
using System.Collections.Generic;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class CredentialProtectionTests
    {
        [TestMethod]
        public void SaveAndLoad_RoundTripsThroughCurrentUserDpapiWithoutPlaintext()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("credentials.dat");
                CredentialProtectionService service = new CredentialProtectionService(null, path);
                R2Credentials expected = new R2Credentials(
                    "test-access-key-" + System.Guid.NewGuid().ToString("N"),
                    "test-secret-" + System.Guid.NewGuid().ToString("N"));

                Assert.IsTrue(service.Save(expected));
                byte[] raw = File.ReadAllBytes(path);
                string rawText = System.Text.Encoding.UTF8.GetString(raw);
                Assert.IsFalse(rawText.Contains(expected.AccessKeyId));
                Assert.IsFalse(rawText.Contains(expected.SecretAccessKey));

                R2Credentials actual = service.Load();
                Assert.IsNotNull(actual);
                Assert.AreEqual(expected.AccessKeyId, actual.AccessKeyId);
                Assert.AreEqual(expected.SecretAccessKey, actual.SecretAccessKey);
                Assert.IsTrue(service.Clear());
                Assert.IsFalse(File.Exists(path));
            }
        }

        [TestMethod]
        public void SaveProfiles_RoundTripsSeparateEncryptedCredentialsPerBucket()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("credentials.dat");
                CredentialProtectionService service = new CredentialProtectionService(null, path);
                Dictionary<string, R2Credentials> expected = new Dictionary<string, R2Credentials>
                {
                    ["profile-a"] = new R2Credentials("access-a", "secret-a"),
                    ["profile-b"] = new R2Credentials("access-b", "secret-b")
                };

                Assert.IsTrue(service.SaveProfiles(expected));

                string rawText = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));
                Assert.IsFalse(rawText.Contains("profile-a"));
                Assert.IsFalse(rawText.Contains("access-a"));
                Assert.IsFalse(rawText.Contains("secret-b"));

                Dictionary<string, R2Credentials> actual = service.LoadProfiles("unused");
                Assert.AreEqual(2, actual.Count);
                Assert.AreEqual("access-a", actual["profile-a"].AccessKeyId);
                Assert.AreEqual("secret-a", actual["profile-a"].SecretAccessKey);
                Assert.AreEqual("access-b", actual["profile-b"].AccessKeyId);
                Assert.AreEqual("secret-b", actual["profile-b"].SecretAccessKey);
            }
        }

        [TestMethod]
        public void LoadProfiles_AssignsLegacyCredentialBlobToMigratedProfile()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("credentials.dat");
                CredentialProtectionService service = new CredentialProtectionService(null, path);
                Assert.IsTrue(service.Save(new R2Credentials("legacy-access", "legacy-secret")));

                Dictionary<string, R2Credentials> actual = service.LoadProfiles("migrated-profile");

                Assert.AreEqual(1, actual.Count);
                Assert.AreEqual("legacy-access", actual["migrated-profile"].AccessKeyId);
                Assert.AreEqual("legacy-secret", actual["migrated-profile"].SecretAccessKey);
            }
        }

        // Break caught: a replacement failure after DPAPI encryption must preserve the last credential blob bytes.
        [TestMethod]
        public void Save_WhenAtomicReplacementFails_ReturnsFalseAndPreservesExistingBytes()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string path = directory.File("credentials.dat");
                byte[] oldBytes = { 1, 2, 3, 4 };
                File.WriteAllBytes(path, oldBytes);
                CredentialProtectionService service = new CredentialProtectionService(
                    null, path, new AtomicFileWriter(new ThrowingCommitter()));

                Assert.IsFalse(service.Save(new R2Credentials("new-access", "new-secret")));

                CollectionAssert.AreEqual(oldBytes, File.ReadAllBytes(path));
                Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.tmp").Length);
            }
        }

        private sealed class ThrowingCommitter : IAtomicFileCommitter
        {
            public void Commit(string temporaryPath, string destinationPath) =>
                throw new IOException("synthetic replacement failure");
        }
    }
}
