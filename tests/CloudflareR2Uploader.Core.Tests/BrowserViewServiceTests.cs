using System;
using System.Collections.Generic;
using System.Linq;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class BrowserViewServiceTests
    {
        private static readonly BrowserViewService Service = new BrowserViewService();
        private static readonly string[] FolderIdentity = { "folder/" };
        private static readonly string[] PhotoIdentity = { "z/photo.JPG" };

        [TestMethod] public void Filter_IsCaseInsensitiveAndMatchesTypeExtensionAndKey()
        {
            List<R2BrowserItem> items = Items();
            CollectionAssert.AreEqual(FolderIdentity, Service.Apply(items, "FOLDER", BrowserSortColumn.Name, BrowserSortDirection.Ascending).Items.Select(x => x.Identity).ToArray());
            CollectionAssert.AreEqual(PhotoIdentity, Service.Apply(items, "image", BrowserSortColumn.Name, BrowserSortDirection.Ascending).Items.Select(x => x.Identity).ToArray());
            CollectionAssert.AreEqual(PhotoIdentity, Service.Apply(items, "jpg", BrowserSortColumn.Name, BrowserSortDirection.Ascending).Items.Select(x => x.Identity).ToArray());
            Assert.AreEqual(0, Service.Apply(items, "absent", BrowserSortColumn.Name, BrowserSortDirection.Ascending).Items.Count);
        }
        [TestMethod] public void Sort_IsTypedStableAndAlwaysFoldersFirst()
        {
            List<R2BrowserItem> items = Items();
            BrowserViewResult size = Service.Apply(items, null, BrowserSortColumn.Size, BrowserSortDirection.Descending);
            Assert.IsTrue(size.Items[0].IsFolder); Assert.AreEqual("z/photo.JPG", size.Items[1].Key); Assert.AreEqual("a.txt", size.Items[2].Key);
            BrowserViewResult modified = Service.Apply(items, null, BrowserSortColumn.LastModified, BrowserSortDirection.Ascending);
            Assert.AreEqual("a.txt", modified.Items[1].Key); Assert.AreEqual("z/photo.JPG", modified.Items[2].Key);
        }
        [TestMethod] public void Summary_ExcludesFolderContentsAndTracksDates()
        {
            BrowserSelectionSummary summary = Service.Summarize(Items());
            Assert.AreEqual(3, summary.SelectedCount); Assert.AreEqual(1, summary.FolderCount); Assert.AreEqual(2, summary.FileCount); Assert.AreEqual(110, summary.ListedFileSize);
            Assert.IsTrue(summary.EarliestModifiedUtc.HasValue); Assert.IsTrue(summary.LatestModifiedUtc.HasValue);
        }
        private static List<R2BrowserItem> Items()
        {
            return new List<R2BrowserItem> {
                new R2BrowserItem { IsFolder=true, Prefix="folder/", DisplayName="Folder", Size=9999 },
                new R2BrowserItem { Key="z/photo.JPG", DisplayName="photo.JPG", Size=100, LastModifiedUtc=null },
                new R2BrowserItem { Key="a.txt", DisplayName="a.txt", Size=10, LastModifiedUtc=new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc) }
            };
        }
    }
}
