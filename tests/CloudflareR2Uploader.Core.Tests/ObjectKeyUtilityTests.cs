using System.Text;
using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class ObjectKeyUtilityTests
    {
        [TestMethod]
        public void Normalize_CollapsesSeparatorsAndResolvesDotSegments()
        {
            Assert.AreEqual("releases/final/file.zip",
                ObjectKeyUtility.Normalize(@"//releases\preview\..\final//./file.zip/"));
        }

        [TestMethod]
        public void AppendDuplicateSuffix_PreservesDirectoryAndFinalExtension()
        {
            Assert.AreEqual("archives/build.tar (2).gz",
                ObjectKeyUtility.AppendDuplicateSuffix("archives/build.tar.gz", 2));
            Assert.AreEqual(".gitignore (1)",
                ObjectKeyUtility.AppendDuplicateSuffix(".gitignore", 1));
        }

        [TestMethod]
        public void Validate_UsesUtf8ByteLimitAndRejectsMalformedKeys()
        {
            string reason;
            Assert.IsFalse(ObjectKeyUtility.TryValidate("/leading.txt", out reason));
            Assert.IsFalse(ObjectKeyUtility.TryValidate("a//b.txt", out reason));
            Assert.IsTrue(ObjectKeyUtility.TryValidate(new string('a', 1024), out reason));
            Assert.IsFalse(ObjectKeyUtility.TryValidate(new string('\u00E9', 513), out reason));
            Assert.AreEqual(1026, Encoding.UTF8.GetByteCount(new string('\u00E9', 513)));
        }

        [TestMethod]
        public void PublicUrl_EncodesUnicodeAndSpacesButPreservesSlashes()
        {
            Assert.AreEqual(
                "https://files.example.com/folder/hello%20%E2%98%83.txt",
                ObjectKeyUtility.BuildPublicUrl("files.example.com/", "folder/hello \u2603.txt"));
        }
    }
}
