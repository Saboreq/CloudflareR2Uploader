using System;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class SdkContractTests
    {
        [TestMethod]
        public void ClientConfig_UsesHttpsSigV4AndR2CompatiblePathStyle()
        {
            AppSettings settings = new AppSettings
            {
                AccountId = "0123456789abcdef0123456789abcdef"
            };

            AmazonS3Config config = R2ClientFactory.CreateConfig(settings);

            Assert.AreEqual(
                new Uri("https://0123456789abcdef0123456789abcdef.r2.cloudflarestorage.com"),
                new Uri(config.ServiceURL));
            Assert.AreEqual("4", config.SignatureVersion);
            Assert.AreEqual(R2ClientFactory.SigningRegion, config.AuthenticationRegion);
            Assert.IsTrue(config.ForcePathStyle);
            Assert.IsFalse(config.UseHttp);
            Assert.AreEqual(0, config.MaxErrorRetry);
        }

        [TestMethod]
        public void ClientConfig_RejectsHttpBecausePayloadSigningIsDisabled()
        {
            AppSettings settings = new AppSettings { CustomEndpoint = "http://localhost:9000" };
            InvalidOperationException error = Assert.ThrowsException<InvalidOperationException>(
                () => R2ClientFactory.CreateConfig(settings));
            StringAssert.Contains(error.Message, "HTTPS");
        }

        [TestMethod]
        public void AwsSdkV3_ExposesRequiredR2UploadFlags()
        {
            PutObjectRequest put = new PutObjectRequest
            {
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true,
                UseChunkEncoding = false
            };
            UploadPartRequest part = new UploadPartRequest
            {
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true,
                UseChunkEncoding = false
            };

            Assert.IsTrue(put.DisablePayloadSigning);
            Assert.IsTrue(put.DisableDefaultChecksumValidation);
            Assert.IsFalse(put.UseChunkEncoding);
            Assert.IsTrue(part.DisablePayloadSigning);
            Assert.IsTrue(part.DisableDefaultChecksumValidation);
            Assert.IsFalse(part.UseChunkEncoding);
        }

        [TestMethod]
        public void AwsSdkV3_ExposesRequiredListObjectsV2BrowserFields()
        {
            ListObjectsV2Request request = new ListObjectsV2Request
            {
                BucketName = "bucket",
                Prefix = "photos/",
                Delimiter = "/",
                MaxKeys = 250,
                ContinuationToken = "opaque-token"
            };

            Assert.AreEqual("photos/", request.Prefix);
            Assert.AreEqual("/", request.Delimiter);
            Assert.AreEqual(250, request.MaxKeys);
            Assert.AreEqual("opaque-token", request.ContinuationToken);
        }

        [TestMethod]
        public void AwsSdkV3_ExposesRequiredBrowserObjectOperationRequests()
        {
            CopyObjectRequest copy = new CopyObjectRequest
            {
                SourceBucket = "bucket",
                SourceKey = "source.bin",
                DestinationBucket = "bucket",
                DestinationKey = "destination.bin"
            };
            CopyPartRequest copyPart = new CopyPartRequest
            {
                SourceBucket = "bucket",
                SourceKey = "large.bin",
                DestinationBucket = "bucket",
                DestinationKey = "large-copy.bin",
                UploadId = "upload-id",
                PartNumber = 1,
                FirstByte = 0,
                LastByte = 1023
            };
            DeleteObjectsRequest delete = new DeleteObjectsRequest
            {
                BucketName = "bucket",
                Quiet = true
            };
            delete.Objects.Add(new KeyVersion { Key = "old.bin" });
            GetObjectRequest download = new GetObjectRequest
            {
                BucketName = "bucket",
                Key = "download.bin"
            };
            PutObjectRequest folderMarker = new PutObjectRequest
            {
                BucketName = "bucket",
                Key = "photos/",
                InputStream = new System.IO.MemoryStream(new byte[0]),
                ContentType = "application/x-directory",
                DisablePayloadSigning = true,
                DisableDefaultChecksumValidation = true,
                UseChunkEncoding = false
            };

            Assert.AreEqual("source.bin", copy.SourceKey);
            Assert.AreEqual("destination.bin", copy.DestinationKey);
            Assert.AreEqual(0, copyPart.FirstByte);
            Assert.AreEqual(1023, copyPart.LastByte);
            Assert.AreEqual(1, delete.Objects.Count);
            Assert.IsTrue(delete.Quiet);
            Assert.AreEqual("download.bin", download.Key);
            Assert.AreEqual("photos/", folderMarker.Key);
            Assert.AreEqual(0, folderMarker.InputStream.Length);
            Assert.IsTrue(folderMarker.DisablePayloadSigning);
            Assert.IsTrue(folderMarker.DisableDefaultChecksumValidation);
            Assert.IsFalse(folderMarker.UseChunkEncoding);
            folderMarker.InputStream.Dispose();
        }
    }
}
