using System;
using System.Collections.Generic;

namespace CloudflareR2Uploader.Models
{
    public sealed class PresignedUrlOptions
    {
        public static readonly TimeSpan MinimumExpiration = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan MaximumExpiration = TimeSpan.FromDays(7);

        public TimeSpan Expiration { get; set; }
    }

    public sealed class PresignedUrlResult
    {
        public string Url { get; set; }
        public DateTime ExpiresLocal { get; set; }
    }

    public sealed class R2ObjectProperties
    {
        public R2ObjectProperties()
        {
            CustomMetadata = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ExtractedMetadata = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public string Name { get; set; }
        public string KeyOrPrefix { get; set; }
        public bool IsFolder { get; set; }
        public string Bucket { get; set; }
        public string ProfileName { get; set; }
        public string EndpointHost { get; set; }
        public string Type { get; set; }
        public string Extension { get; set; }
        public long? Size { get; set; }
        public DateTime? LastModifiedUtc { get; set; }
        public string ETag { get; set; }
        public string ContentType { get; set; }
        public string CacheControl { get; set; }
        public string ContentDisposition { get; set; }
        public string ContentEncoding { get; set; }
        public string ContentLanguage { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public string VersionId { get; set; }
        public string StorageClass { get; set; }
        public string Checksum { get; set; }
        public string PublicUrl { get; set; }
        public SortedDictionary<string, string> CustomMetadata { get; private set; }
        public SortedDictionary<string, string> ExtractedMetadata { get; private set; }
    }

    public enum R2PreviewKind
    {
        Unsupported = 0,
        Text = 1,
        Json = 2,
        Xml = 3,
        Csv = 4,
        Markdown = 5,
        Image = 6,
        Pdf = 7,
        HtmlSource = 8,
        SvgSource = 9
    }

    public sealed class R2PreviewResult
    {
        public R2PreviewKind Kind { get; set; }
        public string Text { get; set; }
        public string LocalPath { get; set; }
        public string ContentType { get; set; }
        public bool Truncated { get; set; }
        public string Warning { get; set; }
        public int? PixelWidth { get; set; }
        public int? PixelHeight { get; set; }
    }
}
