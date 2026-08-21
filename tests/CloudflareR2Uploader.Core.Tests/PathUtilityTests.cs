using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class PathUtilityTests
    {
        [TestMethod]
        public void GetRelativePath_UsesForwardSlashes()
        {
            Assert.AreEqual("nested/file.txt",
                PathUtility.GetRelativePath(
                    Path.Combine(Path.DirectorySeparatorChar.ToString(), "uploads"),
                    Path.Combine(Path.DirectorySeparatorChar.ToString(), "uploads", "nested", "file.txt")));
        }

        [TestMethod]
        public void ExtendedPath_PrefixesLongDriveAndUncPaths()
        {
            string longTail = new string('a', 260);
            Assert.AreEqual(@"\\?\C:\" + longTail,
                PathUtility.ToExtendedLengthPath(@"C:\" + longTail));
            Assert.AreEqual(@"\\?\UNC\server\share\" + longTail,
                PathUtility.ToExtendedLengthPath(@"\\server\share\" + longTail));
        }

        [TestMethod]
        public void SanitizeForFileName_RemovesInvalidCharactersAndLimitsLength()
        {
            string sanitized = PathUtility.SanitizeForFileName(new string('x', 100) + ":bad");
            Assert.IsTrue(sanitized.Length <= 80);
            Assert.IsFalse(sanitized.Contains(':'));
        }
    }
}
