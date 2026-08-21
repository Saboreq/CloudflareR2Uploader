using System;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class R2BrowserResponseMapperTests
    {
        [TestMethod]
        public void Map_EmptyResponseReturnsAnEmptyCurrentPageWithoutFakeTotals()
        {
            R2BrowserPage page = R2BrowserResponseMapper.Map(
                new ListObjectsV2Response(),
                string.Empty,
                null);

            Assert.AreEqual(0, page.Items.Count);
            Assert.AreEqual(0, page.FolderCount);
            Assert.AreEqual(0, page.FileCount);
            Assert.IsFalse(page.IsTruncated);
        }

        [TestMethod]
        public void Map_SkipsObjectThatExactlyMatchesCurrentFolderPrefix()
        {
            ListObjectsV2Response response = new ListObjectsV2Response();
            response.S3Objects.Add(new S3Object { Key = "photos/", Size = 0 });
            response.S3Objects.Add(new S3Object { Key = "photos/image.jpg", Size = 24 });

            R2BrowserPage page = R2BrowserResponseMapper.Map(response, "photos/", null);

            Assert.AreEqual(1, page.Items.Count);
            Assert.AreEqual("photos/image.jpg", page.Items[0].Key);
        }

        [TestMethod]
        public void Map_CommonPrefixesBecomeFolderRowsWithOriginalPrefix()
        {
            ListObjectsV2Response response = new ListObjectsV2Response();
            response.CommonPrefixes.Add("photos/2026/");

            R2BrowserPage page = R2BrowserResponseMapper.Map(response, "photos/", null);
            R2BrowserItem folder = page.Items[0];

            Assert.IsTrue(folder.IsFolder);
            Assert.AreEqual("2026", folder.DisplayName);
            Assert.AreEqual("photos/2026/", folder.Prefix);
            Assert.AreEqual("photos/2026/", folder.Key);
            Assert.AreEqual(1, page.FolderCount);
        }

        [TestMethod]
        public void Map_ObjectKeysBecomeFileRowsWithoutAlteringOriginalKey()
        {
            DateTime modified = new DateTime(2026, 7, 1, 12, 30, 0, DateTimeKind.Utc);
            ListObjectsV2Response response = new ListObjectsV2Response();
            response.S3Objects.Add(new S3Object
            {
                Key = "photos/archive/photos/image.png",
                Size = 4096,
                LastModified = modified,
                ETag = "etag-value"
            });

            R2BrowserItem file = R2BrowserResponseMapper.Map(response, "photos/", null).Items[0];

            Assert.IsFalse(file.IsFolder);
            Assert.AreEqual("photos/archive/photos/image.png", file.Key);
            Assert.AreEqual("archive/photos/image.png", file.DisplayName);
            Assert.AreEqual(4096, file.Size);
            Assert.AreEqual(modified, file.LastModifiedUtc);
            Assert.AreEqual("etag-value", file.ETag);
        }

        [TestMethod]
        public void Map_SortsFoldersBeforeFilesThenByDisplayName()
        {
            ListObjectsV2Response response = new ListObjectsV2Response();
            response.S3Objects.Add(new S3Object { Key = "z.txt", Size = 1 });
            response.CommonPrefixes.Add("beta/");
            response.S3Objects.Add(new S3Object { Key = "a.txt", Size = 1 });
            response.CommonPrefixes.Add("Alpha/");

            R2BrowserPage page = R2BrowserResponseMapper.Map(response, string.Empty, null);

            Assert.AreEqual("Alpha", page.Items[0].DisplayName);
            Assert.AreEqual("beta", page.Items[1].DisplayName);
            Assert.AreEqual("a.txt", page.Items[2].DisplayName);
            Assert.AreEqual("z.txt", page.Items[3].DisplayName);
            Assert.AreEqual(2, page.FolderCount);
            Assert.AreEqual(2, page.FileCount);
        }

        [TestMethod]
        public void Map_UsesOriginalKeyAsDeterministicSecondarySort()
        {
            ListObjectsV2Response response = new ListObjectsV2Response();
            response.S3Objects.Add(new S3Object { Key = "name.txt", Size = 1 });
            response.S3Objects.Add(new S3Object { Key = "Name.txt", Size = 1 });

            R2BrowserPage page = R2BrowserResponseMapper.Map(response, string.Empty, null);

            Assert.AreEqual("Name.txt", page.Items[0].Key);
            Assert.AreEqual("name.txt", page.Items[1].Key);
        }

        [TestMethod]
        public void Map_PreservesOpaqueContinuationTokens()
        {
            const string requested = "opaque/requested+=token";
            const string next = "opaque/next==token";
            ListObjectsV2Response response = new ListObjectsV2Response
            {
                IsTruncated = true,
                NextContinuationToken = next
            };

            R2BrowserPage page = R2BrowserResponseMapper.Map(response, string.Empty, requested);

            Assert.IsTrue(page.IsTruncated);
            Assert.AreEqual(requested, page.RequestedContinuationToken);
            Assert.AreEqual(next, page.NextContinuationToken);
        }
    }
}
