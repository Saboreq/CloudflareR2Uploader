using System;
using System.IO;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class PreviewCacheServiceTests
    {
        [TestMethod] public void SessionPathsStayUnderRootAndDisposeRemovesOnlySession()
        {
            using (TemporaryDirectory directory = new TemporaryDirectory())
            {
                string unrelated = directory.File("keep.txt"); File.WriteAllText(unrelated, "keep"); string session;
                using (PreviewCacheService cache = new PreviewCacheService(directory.Path)) { session = cache.SessionDirectory; string path = cache.CreatePath(".pdf"); StringAssert.StartsWith(path, session); Assert.AreEqual(".pdf", Path.GetExtension(path)); File.WriteAllText(path, "x"); }
                Assert.IsFalse(Directory.Exists(session)); Assert.IsTrue(File.Exists(unrelated));
            }
        }
    }
}
