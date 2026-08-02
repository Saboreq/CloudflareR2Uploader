using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class R2BrowserPathUtilityTests
    {
        [TestMethod]
        public void NormalizePrefix_UsesEmptyStringForRoot()
        {
            Assert.AreEqual(string.Empty, R2BrowserPathUtility.NormalizePrefix(null));
            Assert.AreEqual(string.Empty, R2BrowserPathUtility.NormalizePrefix(string.Empty));
            Assert.AreEqual(string.Empty, R2BrowserPathUtility.NormalizePrefix("///"));
        }

        [TestMethod]
        public void NormalizePrefix_AddsExactlyOneTrailingSlash()
        {
            Assert.AreEqual("photos/", R2BrowserPathUtility.NormalizePrefix("photos"));
            Assert.AreEqual("photos/", R2BrowserPathUtility.NormalizePrefix("photos/"));
            Assert.AreEqual("photos/", R2BrowserPathUtility.NormalizePrefix("/photos///"));
        }

        [TestMethod]
        public void GetParentPrefix_ReturnsVirtualParentOrRoot()
        {
            Assert.AreEqual("photos/", R2BrowserPathUtility.GetParentPrefix("photos/2026/"));
            Assert.AreEqual(string.Empty, R2BrowserPathUtility.GetParentPrefix("photos/"));
            Assert.AreEqual(string.Empty, R2BrowserPathUtility.GetParentPrefix(string.Empty));
        }

        [TestMethod]
        public void GetRelativeDisplayName_RemovesOnlyTheLeadingCurrentPrefix()
        {
            Assert.AreEqual(
                "image.png",
                R2BrowserPathUtility.GetRelativeDisplayName("photos/2026/image.png", "photos/2026/"));
            Assert.AreEqual(
                "archive/photos/2026/image.png",
                R2BrowserPathUtility.GetRelativeDisplayName(
                    "photos/2026/archive/photos/2026/image.png",
                    "photos/2026/"));
        }

        [TestMethod]
        public void CreateBreadcrumbSegments_PreservesEachNavigablePrefix()
        {
            var segments = R2BrowserPathUtility.CreateBreadcrumbSegments("photos/2026/");

            Assert.AreEqual(3, segments.Count);
            Assert.AreEqual(string.Empty, segments[0].Prefix);
            Assert.AreEqual("photos/", segments[1].Prefix);
            Assert.AreEqual("photos/2026/", segments[2].Prefix);
        }
    }
}
