using System;
using CloudflareR2Uploader.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class ProgressAggregationTests
    {
        [TestMethod]
        public void QueueItem_AppliesAggregatedMultipartProgress()
        {
            UploadQueueItem item = new UploadQueueItem(
                @"C:\data\large.bin", "large.bin", 1000, DateTime.UtcNow);

            item.ApplyProgress(new UploadProgressInfo(375, 1000, 125, 3, 8));

            Assert.AreEqual(375, item.TransferredBytes);
            Assert.AreEqual(125d, item.BytesPerSecond);
            Assert.AreEqual(3, item.CompletedParts);
            Assert.AreEqual(8, item.TotalParts);
            Assert.AreEqual(38, item.Percent);
        }

        [TestMethod]
        public void OverallProgress_ClampsFractionsAndComputesRemainingBytes()
        {
            OverallProgressInfo progress = new OverallProgressInfo(
                1200, 1000, 50, TimeSpan.FromSeconds(3), 1, 0, 0, 0, 0, 0);

            Assert.AreEqual(1d, progress.Fraction);
            Assert.AreEqual(100, progress.Percent);
            Assert.AreEqual(0, progress.RemainingBytes);
        }
    }
}
