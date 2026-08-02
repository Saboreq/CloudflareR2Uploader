using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class RetryServiceTests
    {
        [TestMethod]
        public void CalculateDelay_StaysWithinJitterWindowAndCapsAtThirtySeconds()
        {
            for (int attempt = 1; attempt <= 20; attempt++)
            {
                TimeSpan ceiling = RetryService.CalculateDelayCeiling(attempt);
                TimeSpan delay = RetryService.CalculateDelay(attempt, new Random(attempt));

                Assert.IsTrue(delay.TotalMilliseconds >= ceiling.TotalMilliseconds * 0.1);
                Assert.IsTrue(delay <= ceiling);
                Assert.IsTrue(ceiling <= TimeSpan.FromSeconds(30));
            }
        }

        [TestMethod]
        public async Task ExecuteAsync_RetriesTransientFailureAndStopsOnSuccess()
        {
            RetryService service = new RetryService(null, 3);
            int calls = 0;

            int result = await service.ExecuteAsync(
                "test",
                token =>
                {
                    calls++;
                    if (calls == 1) throw new IOException("connection reset");
                    return Task.FromResult(42);
                },
                CancellationToken.None);

            Assert.AreEqual(42, result);
            Assert.AreEqual(2, calls);
        }
    }
}
