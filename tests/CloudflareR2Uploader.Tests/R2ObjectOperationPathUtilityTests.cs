using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class R2ObjectOperationPathUtilityTests
    {
        [TestMethod]
        public void GetLeafName_HandlesObjectsAndFolderPrefixes()
        {
            Assert.AreEqual("image.png", R2ObjectOperationPathUtility.GetLeafName("photos/image.png", false));
            Assert.AreEqual("2026", R2ObjectOperationPathUtility.GetLeafName("photos/2026/", true));
        }

        [TestMethod]
        public void CombineWithPrefix_PreservesFileAndFolderShapes()
        {
            Assert.AreEqual("archive/image.png",
                R2ObjectOperationPathUtility.CombineWithPrefix("archive/", "image.png", false));
            Assert.AreEqual("archive/photos/",
                R2ObjectOperationPathUtility.CombineWithPrefix("archive", "photos", true));
            Assert.AreEqual("archive/ file.txt",
                R2ObjectOperationPathUtility.CombineWithPrefix("archive/", " file.txt", false));
        }

        [TestMethod]
        public void AppendCopySuffix_PreservesFileExtensionAndFolderSlash()
        {
            Assert.AreEqual("photos/image - Copy.png",
                R2ObjectOperationPathUtility.AppendCopySuffix("photos/image.png", false, 1));
            Assert.AreEqual("photos/image - Copy (2).png",
                R2ObjectOperationPathUtility.AppendCopySuffix("photos/image.png", false, 2));
            Assert.AreEqual("archive/photos - Copy/",
                R2ObjectOperationPathUtility.AppendCopySuffix("archive/photos/", true, 1));
        }

        [TestMethod]
        public void TryValidateLeafName_RejectsPathsAndNavigationNames()
        {
            string reason;
            Assert.IsTrue(R2ObjectOperationPathUtility.TryValidateLeafName("New folder", out reason));
            Assert.IsTrue(R2ObjectOperationPathUtility.TryValidateLeafName(" file ", out reason));
            Assert.IsFalse(R2ObjectOperationPathUtility.TryValidateLeafName("", out reason));
            Assert.IsFalse(R2ObjectOperationPathUtility.TryValidateLeafName("..", out reason));
            Assert.IsFalse(R2ObjectOperationPathUtility.TryValidateLeafName("nested/folder", out reason));
            Assert.IsFalse(R2ObjectOperationPathUtility.TryValidateLeafName("nested\\folder", out reason));
            Assert.IsFalse(R2ObjectOperationPathUtility.TryValidateLeafName("bad\u0001name", out reason));
        }

        [TestMethod]
        public void IsSameOrDescendantPrefix_RejectsRecursiveFolderCopies()
        {
            Assert.IsTrue(R2ObjectOperationPathUtility.IsSameOrDescendantPrefix("photos/", "photos/"));
            Assert.IsTrue(R2ObjectOperationPathUtility.IsSameOrDescendantPrefix("photos/", "photos/2026/"));
            Assert.IsFalse(R2ObjectOperationPathUtility.IsSameOrDescendantPrefix("photos/", "archive/photos/"));
        }

        [TestMethod]
        public void OperationProgress_ClampsAndCalculatesPercent()
        {
            R2ObjectOperationProgress progress = new R2ObjectOperationProgress(
                "Downloading", 0, "file.bin", 50, 200);
            Assert.AreEqual(25, progress.Percent);

            R2ObjectOperationProgress over = new R2ObjectOperationProgress(
                "Downloading", 0, "file.bin", 300, 200);
            Assert.AreEqual(100, over.Percent);
        }
    }
}
