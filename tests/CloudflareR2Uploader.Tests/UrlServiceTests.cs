using System;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class UrlServiceTests
    {
        [TestMethod] public void PublicUrl_HandlesFilesMissingBaseAndFolders()
        {
            PublicUrlService service = new PublicUrlService(); R2BrowserItem file = new R2BrowserItem { Key="a b/雪#?%.txt", DisplayName="x" };
            string url = service.Build("https://cdn.example.com/", file); StringAssert.Contains(url, "a%20b/"); StringAssert.Contains(url, "%23%3F%25");
            Assert.IsNull(service.Build(null, file)); Assert.IsNull(service.Build("https://x", new R2BrowserItem { IsFolder=true, Prefix="a/" }));
        }
        [TestMethod] public void PresignedUrl_IsHttpsSigV4GetAndValidatesExpirationWithoutNetwork()
        {
            AppSettings settings = new AppSettings { CustomEndpoint="https://example.r2.cloudflarestorage.com", BucketName="bucket" }; settings.Clamp();
            R2PresignedUrlService service = new R2PresignedUrlService();
            PresignedUrlResult result = service.Create(settings, new R2Credentials("test-access", "test-secret"), "folder/a b.txt", TimeSpan.FromMinutes(15));
            StringAssert.StartsWith(result.Url, "https://example.r2.cloudflarestorage.com/bucket/folder/a%20b.txt"); StringAssert.Contains(result.Url, "X-Amz-Algorithm=AWS4-HMAC-SHA256");
            Uri uri = new Uri(result.Url); string expiresValue = null; foreach (string part in uri.Query.TrimStart('?').Split('&')) { string[] pair = part.Split('='); if (pair.Length == 2 && pair[0] == "X-Amz-Expires") expiresValue = pair[1]; }
            int expiresSeconds; Assert.IsTrue(int.TryParse(expiresValue, out expiresSeconds)); Assert.IsTrue(expiresSeconds >= 895 && expiresSeconds <= 900, "The SDK-generated expiry should remain within clock precision of 15 minutes.");
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => service.Create(settings, new R2Credentials("a", "b"), "x", TimeSpan.FromDays(8)));
        }
    }
}
