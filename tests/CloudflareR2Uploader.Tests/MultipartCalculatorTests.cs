using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class MultipartCalculatorTests
    {
        [TestMethod]
        public void CalculatePartSize_UsesConfiguredSizeForNormalFiles()
        {
            long size = MultipartCalculator.CalculatePartSize(
                300 * MultipartCalculator.MiB,
                64 * MultipartCalculator.MiB);

            Assert.AreEqual(64 * MultipartCalculator.MiB, size);
            Assert.AreEqual(5, MultipartCalculator.CalculatePartCount(300 * MultipartCalculator.MiB, size));
        }

        [TestMethod]
        public void CalculatePartSize_GrowsAndRoundsToStayUnderBudget()
        {
            long fileSize = 1000L * MultipartCalculator.GiB;
            long size = MultipartCalculator.CalculatePartSize(
                fileSize,
                MultipartCalculator.MinPartSize);

            Assert.AreEqual(103 * MultipartCalculator.MiB, size);
            Assert.IsTrue(MultipartCalculator.CalculatePartCount(fileSize, size) <= MultipartCalculator.PartCountBudget);
        }

        [TestMethod]
        public void PartGeometry_CoversFileWithoutOverlap()
        {
            long fileSize = 129 * MultipartCalculator.MiB + 17;
            long partSize = 64 * MultipartCalculator.MiB;

            Assert.AreEqual(3, MultipartCalculator.CalculatePartCount(fileSize, partSize));
            Assert.AreEqual(0, MultipartCalculator.GetPartOffset(1, partSize));
            Assert.AreEqual(128 * MultipartCalculator.MiB, MultipartCalculator.GetPartOffset(3, partSize));
            Assert.AreEqual(MultipartCalculator.MiB + 17,
                MultipartCalculator.GetPartLength(3, partSize, fileSize));
        }
    }
}
