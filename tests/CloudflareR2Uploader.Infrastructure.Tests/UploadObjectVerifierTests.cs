using System;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class UploadObjectVerifierTests
    {
        [TestMethod]
        public void Verify_QuotedETagsWithDifferentCase_Succeeds()
        {
            UploadQueueItem item = Item();
            AppSettings settings = Settings(true);
            UploadObjectVerifier verifier = new UploadObjectVerifier(null);

            UploadResult result = verifier.Verify(
                item, settings, "input.bin", 4, "\"AaBb\"", "aabb", false);

            Assert.AreEqual(UploadOutcome.Succeeded, result.Outcome);
            Assert.AreEqual(UploadItemStatus.Completed, item.Status);
            Assert.AreEqual("aabb", item.ETag);
            Assert.AreEqual(4, item.RemoteLength);
        }

        [TestMethod]
        public void Verify_SameSizeButDifferentETag_FailsWhenEnabled()
        {
            UploadQueueItem item = Item();
            AppSettings settings = Settings(true);
            UploadObjectVerifier verifier = new UploadObjectVerifier(null);

            UploadResult result = verifier.Verify(
                item, settings, "input.bin", 4, "\"aaaa\"", "\"bbbb\"", false);

            Assert.AreEqual(UploadOutcome.Failed, result.Outcome);
            Assert.AreEqual(UploadItemStatus.Failed, item.Status);
        }

        [TestMethod]
        public void Verify_MissingUploadETag_FailsWhenEnabled()
        {
            UploadQueueItem item = Item();
            UploadObjectVerifier verifier = new UploadObjectVerifier(null);

            UploadResult result = verifier.Verify(
                item, Settings(true), "input.bin", 4, null, "\"aaaa\"", false);

            Assert.AreEqual(UploadOutcome.Failed, result.Outcome);
            Assert.AreEqual(UploadItemStatus.Failed, item.Status);
        }

        [TestMethod]
        public void Verify_MissingHeadETag_FailsWhenEnabled()
        {
            UploadQueueItem item = Item();
            UploadObjectVerifier verifier = new UploadObjectVerifier(null);

            UploadResult result = verifier.Verify(
                item, Settings(true), "input.bin", 4, "\"aaaa\"", "  ", false);

            Assert.AreEqual(UploadOutcome.Failed, result.Outcome);
            Assert.AreEqual(UploadItemStatus.Failed, item.Status);
        }

        [TestMethod]
        public void Verify_SizeMismatch_Fails()
        {
            UploadQueueItem item = Item();
            UploadObjectVerifier verifier = new UploadObjectVerifier(null);

            UploadResult result = verifier.Verify(
                item, Settings(true), "input.bin", 3, "\"aaaa\"", "\"aaaa\"", false);

            Assert.AreEqual(UploadOutcome.Failed, result.Outcome);
            Assert.AreEqual(UploadItemStatus.Failed, item.Status);
        }

        [TestMethod]
        public void Verify_DifferentETag_SucceedsWhenDisabled()
        {
            UploadQueueItem item = Item();
            UploadObjectVerifier verifier = new UploadObjectVerifier(null);

            UploadResult result = verifier.Verify(
                item, Settings(false), "input.bin", 4, "\"aaaa\"", "\"bbbb\"", false);

            Assert.AreEqual(UploadOutcome.Succeeded, result.Outcome);
            Assert.AreEqual(UploadItemStatus.Completed, item.Status);
            Assert.AreEqual("\"bbbb\"", result.ETag);
        }

        [TestMethod]
        public void Verify_SizeMismatch_FailsWhenETagVerificationDisabled()
        {
            UploadQueueItem item = Item();
            UploadObjectVerifier verifier = new UploadObjectVerifier(null);

            UploadResult result = verifier.Verify(
                item, Settings(false), "input.bin", 5, "\"aaaa\"", "\"bbbb\"", false);

            Assert.AreEqual(UploadOutcome.Failed, result.Outcome);
            Assert.AreEqual(UploadItemStatus.Failed, item.Status);
        }

        private static UploadQueueItem Item()
        {
            return new UploadQueueItem("C:\\input.bin", "input.bin", 4, DateTime.UtcNow);
        }

        private static AppSettings Settings(bool verifyETag)
        {
            return new AppSettings
            {
                BucketName = "bucket",
                PublicBaseUrl = "https://cdn.example.com",
                VerifyETagAfterUpload = verifyETag
            };
        }
    }
}
