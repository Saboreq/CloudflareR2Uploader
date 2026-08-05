using System.Collections.Generic;

namespace CloudflareR2Uploader.Models
{
    /// <summary>A single, deliberately non-totalled page from ListObjectsV2.</summary>
    public sealed class R2BrowserPage
    {
        public R2BrowserPage()
        {
            Items = new List<R2BrowserItem>();
        }

        public string Prefix { get; set; }
        public IList<R2BrowserItem> Items { get; set; }
        public string RequestedContinuationToken { get; set; }
        public string NextContinuationToken { get; set; }
        public bool IsTruncated { get; set; }
        public int FolderCount { get; set; }
        public int FileCount { get; set; }
    }
}
