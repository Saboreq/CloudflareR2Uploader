using System;
using System.Collections.Generic;

namespace CloudflareR2Uploader.Models
{
    public enum BrowserSortColumn
    {
        Name = 0,
        Type = 1,
        Size = 2,
        LastModified = 3
    }

    public enum BrowserSortDirection
    {
        Ascending = 0,
        Descending = 1
    }

    public sealed class BrowserSelectionSummary
    {
        public int SelectedCount { get; set; }
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
        public long ListedFileSize { get; set; }
        public DateTime? EarliestModifiedUtc { get; set; }
        public DateTime? LatestModifiedUtc { get; set; }
    }

    public sealed class BrowserViewResult
    {
        public BrowserViewResult()
        {
            Items = new List<R2BrowserItem>();
        }

        public List<R2BrowserItem> Items { get; private set; }
        public int TotalCount { get; set; }
        public string FilterText { get; set; }
    }
}
