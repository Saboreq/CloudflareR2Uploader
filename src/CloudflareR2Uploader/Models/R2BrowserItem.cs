using System;

namespace CloudflareR2Uploader.Models
{
    /// <summary>One virtual folder prefix or object returned by an R2 browser listing.</summary>
    public sealed class R2BrowserItem
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string Prefix { get; set; }
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        public DateTime? LastModifiedUtc { get; set; }
        public string ETag { get; set; }
    }
}
