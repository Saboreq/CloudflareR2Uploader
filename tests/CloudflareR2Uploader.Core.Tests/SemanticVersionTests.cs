using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class SemanticVersionTests
    {
        [DataTestMethod]
        [DataRow("1.0.0-01")]
        [DataRow("1.0.0-alpha..1")]
        [DataRow("1.0.0-")]
        [DataRow("1.0.0+")]
        [DataRow("1.0.0+build..1")]
        [DataRow("1.0.0-alpha_1")]
        [DataRow("1.0.0+build_1")]
        [DataRow(" 1.0.0")]
        [DataRow("1.0.0 ")]
        [DataRow("1.0.0\n")]
        [DataRow("1.0.0\r\n")]
        public void TryParse_RejectsNonSemVerIdentifiersAndWhitespace(string value)
        {
            Assert.IsFalse(SemanticVersion.TryParse(value, out _), value);
        }

        [TestMethod]
        public void TryParse_AcceptsArbitrarilyLargeCoreAndPrereleaseNumbers()
        {
            Assert.IsTrue(SemanticVersion.TryParse(
                "999999999999999999999999.888888888888888888888888.777777777777777777777777-" +
                "123456789012345678901234567890+build.0001",
                out _));
        }

        [TestMethod]
        public void CompareTo_OrdersArbitrarilyLargeNumericPrereleaseIdentifiersByMagnitude()
        {
            SemanticVersion lower = Parse("1.0.0-999999999999999999999999999999999999");
            SemanticVersion higher = Parse("1.0.0-1000000000000000000000000000000000000");
            SemanticVersion equalLengthHigher = Parse("1.0.0-2000000000000000000000000000000000000");
            SemanticVersion nonNumeric = Parse("1.0.0-alpha");

            Assert.IsTrue(lower.CompareTo(higher) < 0);
            Assert.IsTrue(higher.CompareTo(equalLengthHigher) < 0);
            Assert.IsTrue(equalLengthHigher.CompareTo(nonNumeric) < 0);
            Assert.IsTrue(lower.CompareTo(nonNumeric) < 0, "Prerelease ordering must remain transitive.");
        }

        [TestMethod]
        public void CompareTo_IgnoresBuildMetadataPrecedence()
        {
            SemanticVersion first = Parse("1.2.3-rc.1+build.7");
            SemanticVersion second = Parse("1.2.3-rc.1+other.999");

            Assert.AreEqual(0, first.CompareTo(second));
            Assert.AreEqual(0, second.CompareTo(first));
        }

        [TestMethod]
        public void TryGetFileVersion_RequiresWindowsUshortCoreComponents()
        {
            SemanticVersion maximum = Parse("65535.65535.65535-rc.1+build.7");
            SemanticVersion tooLarge = Parse("65536.0.0");

            Assert.IsTrue(maximum.TryGetFileVersion(out int major, out int minor, out int patch));
            Assert.AreEqual(65535, major);
            Assert.AreEqual(65535, minor);
            Assert.AreEqual(65535, patch);
            Assert.IsFalse(tooLarge.TryGetFileVersion(out _, out _, out _));
        }

        private static SemanticVersion Parse(string value)
        {
            Assert.IsTrue(SemanticVersion.TryParse(value, out SemanticVersion version), value);
            return version;
        }
    }
}
