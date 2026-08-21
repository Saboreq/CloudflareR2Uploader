using System;
using System.Collections.Generic;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>Pure response-to-row mapping kept separate from network access for testing.</summary>
    public static class R2BrowserResponseMapper
    {
        public static R2BrowserPage Map(
            ListObjectsV2Response response,
            string requestedPrefix,
            string requestedContinuationToken)
        {
            ArgumentNullException.ThrowIfNull(response);

            string prefix = R2BrowserPathUtility.NormalizePrefix(requestedPrefix);
            List<R2BrowserItem> items = new List<R2BrowserItem>();

            if (response.CommonPrefixes != null)
            {
                foreach (string returnedPrefix in response.CommonPrefixes)
                {
                    if (string.IsNullOrEmpty(returnedPrefix)) continue;

                    string displayName = R2BrowserPathUtility
                        .GetRelativeDisplayName(returnedPrefix, prefix)
                        .TrimEnd('/');
                    if (displayName.Length == 0) continue;

                    items.Add(new R2BrowserItem
                    {
                        Key = returnedPrefix,
                        Prefix = returnedPrefix,
                        DisplayName = displayName,
                        IsFolder = true,
                        Size = 0,
                        LastModifiedUtc = null,
                        ETag = string.Empty
                    });
                }
            }

            if (response.S3Objects != null)
            {
                foreach (S3Object source in response.S3Objects)
                {
                    if (source == null || string.IsNullOrEmpty(source.Key)) continue;
                    if (string.Equals(source.Key, prefix, StringComparison.Ordinal)) continue;

                    string displayName = R2BrowserPathUtility.GetRelativeDisplayName(source.Key, prefix);
                    if (displayName.Length == 0) continue;

                    DateTime modified = source.LastModified;
                    DateTime modifiedUtc = modified.Kind == DateTimeKind.Utc
                        ? modified
                        : modified.ToUniversalTime();

                    items.Add(new R2BrowserItem
                    {
                        Key = source.Key,
                        Prefix = prefix,
                        DisplayName = displayName,
                        IsFolder = false,
                        Size = source.Size,
                        LastModifiedUtc = modified == default(DateTime) ? (DateTime?)null : modifiedUtc,
                        ETag = source.ETag ?? string.Empty
                    });
                }
            }

            items.Sort(CompareItems);

            int folders = 0;
            foreach (R2BrowserItem item in items)
                if (item.IsFolder) folders++;

            return new R2BrowserPage
            {
                Prefix = prefix,
                Items = items,
                RequestedContinuationToken = requestedContinuationToken,
                NextContinuationToken = response.NextContinuationToken,
                IsTruncated = response.IsTruncated,
                FolderCount = folders,
                FileCount = items.Count - folders
            };
        }

        private static int CompareItems(R2BrowserItem left, R2BrowserItem right)
        {
            if (left.IsFolder != right.IsFolder) return left.IsFolder ? -1 : 1;

            int display = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            if (display != 0) return display;

            return StringComparer.Ordinal.Compare(left.Key ?? string.Empty, right.Key ?? string.Empty);
        }
    }
}
