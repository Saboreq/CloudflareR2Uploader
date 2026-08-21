using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class R2BrowserPaginationStateTests
    {
        [TestMethod]
        public void PageTokenHistory_SupportsNextAndPreviousWithoutChangingTokens()
        {
            R2BrowserPaginationState state = new R2BrowserPaginationState();
            state.SetNextContinuationToken("opaque-page-2");

            Assert.IsTrue(state.MoveNext());
            Assert.AreEqual(1, state.PageIndex);
            Assert.AreEqual("opaque-page-2", state.CurrentToken);

            state.SetNextContinuationToken("opaque-page-3");
            Assert.IsTrue(state.MoveNext());
            Assert.AreEqual("opaque-page-3", state.CurrentToken);

            Assert.IsTrue(state.MovePrevious());
            Assert.AreEqual("opaque-page-2", state.CurrentToken);
            Assert.IsTrue(state.MovePrevious());
            Assert.IsNull(state.CurrentToken);
            Assert.IsFalse(state.MovePrevious());
        }

        [TestMethod]
        public void ResetForChangedPrefix_DiscardsPaginationHistory()
        {
            R2BrowserPaginationState state = new R2BrowserPaginationState();
            state.SetNextContinuationToken("page-2");
            state.MoveNext();

            state.Reset("photos/2026");

            Assert.AreEqual("photos/2026/", state.Prefix);
            Assert.AreEqual(0, state.PageIndex);
            Assert.AreEqual(1, state.PageTokens.Count);
            Assert.IsNull(state.CurrentToken);
            Assert.IsNull(state.NextContinuationToken);
        }
    }
}
