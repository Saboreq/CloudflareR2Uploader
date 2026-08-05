namespace CloudflareR2Uploader.Models
{
    /// <summary>A display label paired with the exact virtual prefix it represents.</summary>
    public sealed class R2BrowserBreadcrumbSegment
    {
        public R2BrowserBreadcrumbSegment(string displayName, string prefix)
        {
            DisplayName = displayName;
            Prefix = prefix;
        }

        public string DisplayName { get; private set; }
        public string Prefix { get; private set; }
    }
}
