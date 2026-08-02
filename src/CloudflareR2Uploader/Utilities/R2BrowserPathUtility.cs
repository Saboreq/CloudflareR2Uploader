using System;
using System.Collections.Generic;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>R2 virtual-folder path operations. These do not validate object keys.</summary>
    public static class R2BrowserPathUtility
    {
        public static string NormalizePrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return string.Empty;

            int start = 0;
            while (start < prefix.Length && prefix[start] == '/') start++;

            int end = prefix.Length;
            while (end > start && prefix[end - 1] == '/') end--;

            if (end == start) return string.Empty;
            return prefix.Substring(start, end - start) + "/";
        }

        public static string GetParentPrefix(string prefix)
        {
            string normalized = NormalizePrefix(prefix);
            if (normalized.Length == 0) return string.Empty;

            string withoutTrailingSlash = normalized.Substring(0, normalized.Length - 1);
            int separator = withoutTrailingSlash.LastIndexOf('/');
            return separator < 0 ? string.Empty : withoutTrailingSlash.Substring(0, separator + 1);
        }

        public static string GetRelativeDisplayName(string value, string currentPrefix)
        {
            string source = value ?? string.Empty;
            string prefix = currentPrefix ?? string.Empty;

            return prefix.Length > 0 && source.StartsWith(prefix, StringComparison.Ordinal)
                ? source.Substring(prefix.Length)
                : source;
        }

        public static IList<R2BrowserBreadcrumbSegment> CreateBreadcrumbSegments(string prefix)
        {
            List<R2BrowserBreadcrumbSegment> result = new List<R2BrowserBreadcrumbSegment>();
            result.Add(new R2BrowserBreadcrumbSegment("Bucket root", string.Empty));

            string normalized = NormalizePrefix(prefix);
            if (normalized.Length == 0) return result;

            string current = string.Empty;
            string[] parts = normalized.TrimEnd('/').Split('/');
            foreach (string part in parts)
            {
                if (part.Length == 0) continue;
                current += part + "/";
                result.Add(new R2BrowserBreadcrumbSegment(part, current));
            }

            return result;
        }

        public static string FormatBreadcrumb(string prefix)
        {
            IList<R2BrowserBreadcrumbSegment> segments = CreateBreadcrumbSegments(prefix);
            string result = string.Empty;
            for (int i = 0; i < segments.Count; i++)
            {
                if (i > 0) result += "  /  ";
                result += segments[i].DisplayName;
            }
            return result;
        }
    }
}
