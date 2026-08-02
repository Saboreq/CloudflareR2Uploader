using System;
using System.IO;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class UploadStateStoreTests
    {
        [TestMethod]
        public void SaveAndLoad_RoundTripsResumeStateWithoutCredentials()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string localFile = directory.File("source.bin");
                File.WriteAllBytes(localFile, new byte[] { 1, 2, 3, 4 });
                FileInfo info = new FileInfo(localFile);
                string stateDirectory = directory.File("state");
                UploadStateStore store = new UploadStateStore(null, stateDirectory);
                string stateId = UploadStateStore.BuildStateId(localFile, "bucket", "path/source.bin");

                MultipartUploadState expected = new MultipartUploadState
                {
                    StateId = stateId,
                    LocalFilePath = localFile,
                    FileSize = info.Length,
                    BucketName = "bucket",
                    ObjectKey = "path/source.bin",
                    UploadId = "upload-id",
                    PartSize = MultipartCalculator.MinPartSize,
                    ContentType = "application/octet-stream"
                };
                expected.LastWriteUtc = info.LastWriteTimeUtc;
                expected.CreatedUtc = DateTime.UtcNow;
                expected.SetPart(new CompletedPartState(1, "\"etag\"", info.Length));

                Assert.IsTrue(store.Save(expected));
                MultipartUploadState actual = store.Load(stateId);

                Assert.IsNotNull(actual);
                Assert.AreEqual(expected.UploadId, actual.UploadId);
                Assert.AreEqual(1, actual.Parts.Count);
                Assert.AreEqual("\"etag\"", actual.Parts[0].ETag);
                Assert.AreEqual(info.Length, actual.CompletedBytes);

                string stateJson = File.ReadAllText(Directory.GetFiles(stateDirectory, "*.json")[0]);
                Assert.IsFalse(stateJson.Contains("secret"));
                Assert.IsFalse(stateJson.Contains("accessKey"));
            }
        }

        [TestMethod]
        public void CanResume_RejectsChangedSource()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string localFile = directory.File("source.bin");
                File.WriteAllBytes(localFile, new byte[] { 1, 2, 3 });
                FileInfo info = new FileInfo(localFile);
                UploadQueueItem item = new UploadQueueItem(localFile, "source.bin", info.Length, info.LastWriteTimeUtc)
                {
                    ObjectKey = "source.bin"
                };
                MultipartUploadState state = new MultipartUploadState
                {
                    LocalFilePath = localFile,
                    FileSize = info.Length,
                    BucketName = "bucket",
                    ObjectKey = "source.bin",
                    UploadId = "upload-id",
                    PartSize = MultipartCalculator.MinPartSize
                };
                state.LastWriteUtc = info.LastWriteTimeUtc;

                File.AppendAllText(localFile, "changed");
                string reason;
                Assert.IsFalse(UploadStateStore.CanResume(state, item, out reason));
                StringAssert.Contains(reason, "size changed");
            }
        }
    }
}
