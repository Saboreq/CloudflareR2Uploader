using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class BulkOperationServiceTests
    {
        [TestMethod] public async Task Plan_DeduplicatesOverlapsFollowsPaginationAndIncludesMarkers()
        {
            FakeSource source = new FakeSource();
            source.Pages["root/|"] = Page("next", Entry("root/", 0, true), Entry("root/a.txt", 10, false));
            source.Pages["root/|next"] = Page(null, Entry("root/nested/b.txt", 20, false));
            R2BulkOperationService service = new R2BulkOperationService(null, source);
            R2BrowserItem[] selected = {
                new R2BrowserItem { IsFolder=true, Prefix="root/", DisplayName="root" },
                new R2BrowserItem { IsFolder=true, Prefix="root/nested/", DisplayName="nested" },
                new R2BrowserItem { Key="root/a.txt", DisplayName="a.txt", Size=10 },
                new R2BrowserItem { Key="outside.txt", DisplayName="outside.txt", Size=5 }
            };
            BulkObjectOperationPlan plan = await service.PlanAsync(Settings(), Credentials(), selected, null, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "outside.txt", "root/", "root/a.txt", "root/nested/b.txt" }, plan.Objects.Select(x => x.Key).ToArray());
            Assert.AreEqual(35, plan.TotalBytes); Assert.AreEqual(2, source.ListCalls); Assert.IsTrue(plan.Objects.Single(x => x.Key == "root/").IsFolderMarker);
        }
        [TestMethod] public async Task Plan_RecordsSelectedEmptyFolderAndHonorsCancellation()
        {
            FakeSource source = new FakeSource(); source.Pages["empty/|"] = Page(null);
            R2BulkOperationService service = new R2BulkOperationService(null, source);
            BulkObjectOperationPlan plan = await service.PlanAsync(Settings(), Credentials(), new[] { new R2BrowserItem { IsFolder=true, Prefix="empty/" } }, null, CancellationToken.None);
            CollectionAssert.AreEqual(new[] { "empty" }, plan.EmptyDirectoryPaths.ToArray());
            CancellationTokenSource cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => service.PlanAsync(Settings(), Credentials(), new[] { new R2BrowserItem { IsFolder=true, Prefix="empty/" } }, null, cancellation.Token));
        }
        private static AppSettings Settings() { AppSettings settings = new AppSettings { CustomEndpoint="https://example.com", BucketName="bucket" }; settings.Clamp(); return settings; }
        private static R2Credentials Credentials() { return new R2Credentials("a", "b"); }
        private static BulkObjectEntry Entry(string key, long size, bool marker) { return new BulkObjectEntry { Key=key, Size=size, IsFolderMarker=marker }; }
        private static BulkListingPage Page(string token, params BulkObjectEntry[] entries) { BulkListingPage page = new BulkListingPage { NextContinuationToken=token }; page.Objects.AddRange(entries); return page; }
        private sealed class FakeSource : IR2BulkObjectSource
        {
            public readonly Dictionary<string,BulkListingPage> Pages = new Dictionary<string,BulkListingPage>(); public int ListCalls;
            public Task<BulkListingPage> ListAsync(AppSettings s,R2Credentials c,string p,string t,CancellationToken ct) { ct.ThrowIfCancellationRequested(); ListCalls++; return Task.FromResult(Pages[p+"|"+(t??"")]); }
            public Task<long?> DownloadAsync(AppSettings s,R2Credentials c,string k,Stream d,CancellationToken ct) { return Task.FromResult<long?>(0); }
            public Task<BulkDeleteBatchResult> DeleteAsync(AppSettings s,R2Credentials c,IList<string> k,CancellationToken ct) { return Task.FromResult(new BulkDeleteBatchResult()); }
        }
    }
}
