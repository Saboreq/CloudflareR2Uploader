using CloudflareR2Uploader.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Wpf.Tests
{
    [TestClass]
    public sealed class ApplicationWindowCloseRouterTests
    {
        [TestMethod]
        public void Evaluate_HidesForTrayPreferenceOrRunningUpload()
        {
            ApplicationWindowCloseRouter router = new();

            Assert.AreEqual(WindowCloseDisposition.Hide, router.Evaluate(closeToTray: true, uploadRunning: false));
            Assert.AreEqual(WindowCloseDisposition.Hide, router.Evaluate(closeToTray: false, uploadRunning: true));
            Assert.IsFalse(router.ShouldShutdownAfterClosed);
        }

        [TestMethod]
        public void Evaluate_RequestsCoordinatedExitUntilCommitThenAllowsCloseAndShutdown()
        {
            ApplicationWindowCloseRouter router = new();

            Assert.AreEqual(WindowCloseDisposition.RequestExit, router.Evaluate(closeToTray: false, uploadRunning: false));
            Assert.IsFalse(router.ShouldShutdownAfterClosed);

            router.MarkExitCommitted();

            Assert.AreEqual(WindowCloseDisposition.AllowClose, router.Evaluate(closeToTray: true, uploadRunning: true));
            Assert.IsTrue(router.ShouldShutdownAfterClosed);
        }
    }
}
