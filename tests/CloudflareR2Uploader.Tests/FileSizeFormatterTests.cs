using System.Globalization;
using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class FileSizeFormatterTests
    {
        [TestMethod]
        public void Format_UsesReadableBinaryUnits()
        {
            using (new CultureScope(CultureInfo.InvariantCulture))
            {
                Assert.AreEqual("0 B", FileSizeFormatter.Format(-1));
                Assert.AreEqual("1.00 KB", FileSizeFormatter.Format(1024));
                Assert.AreEqual("1.50 GB", FileSizeFormatter.Format(3L * 1024 * 1024 * 1024 / 2));
                Assert.AreEqual("12.0 MB/s", FileSizeFormatter.FormatSpeed(12 * 1024 * 1024));
            }
        }
    }
}
