using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    public sealed class BrowserViewService
    {
        public BrowserViewResult Apply(
            IEnumerable<R2BrowserItem> source,
            string filterText,
            BrowserSortColumn sortColumn,
            BrowserSortDirection sortDirection)
        {
            List<IndexedItem> items = new List<IndexedItem>();
            int index = 0;
            if (source != null)
            {
                foreach (R2BrowserItem item in source)
                {
                    if (item != null) items.Add(new IndexedItem(item.Clone(), index));
                    index++;
                }
            }

            string filter = (filterText ?? string.Empty).Trim();
            BrowserViewResult result = new BrowserViewResult { TotalCount = items.Count, FilterText = filter };
            IEnumerable<IndexedItem> filtered = items.Where(value => Matches(value.Item, filter));
            foreach (IndexedItem value in filtered.OrderBy(value => value, new ItemComparer(sortColumn, sortDirection)))
                result.Items.Add(value.Item);
            return result;
        }

        public BrowserSelectionSummary Summarize(IEnumerable<R2BrowserItem> selection)
        {
            BrowserSelectionSummary summary = new BrowserSelectionSummary();
            if (selection == null) return summary;
            foreach (R2BrowserItem item in selection)
            {
                if (item == null) continue;
                summary.SelectedCount++;
                if (item.IsFolder) summary.FolderCount++;
                else
                {
                    summary.FileCount++;
                    if (item.Size > 0) summary.ListedFileSize += item.Size;
                    if (item.LastModifiedUtc.HasValue)
                    {
                        DateTime modified = item.LastModifiedUtc.Value;
                        if (!summary.EarliestModifiedUtc.HasValue || modified < summary.EarliestModifiedUtc.Value) summary.EarliestModifiedUtc = modified;
                        if (!summary.LatestModifiedUtc.HasValue || modified > summary.LatestModifiedUtc.Value) summary.LatestModifiedUtc = modified;
                    }
                }
            }
            return summary;
        }

        public static string GetDisplayedType(R2BrowserItem item)
        {
            if (item == null) return string.Empty;
            if (item.IsFolder) return "Folder";
            string extension = Path.GetExtension(item.DisplayName ?? item.Key ?? string.Empty).TrimStart('.').ToLowerInvariant();
            switch (extension)
            {
                case "jpg": case "jpeg": case "png": case "gif": case "webp": case "svg": case "bmp": return "Image";
                case "mp4": case "mov": case "avi": case "mkv": case "webm": return "Video";
                case "mp3": case "wav": case "ogg": case "flac": case "m4a": return "Audio";
                case "zip": case "7z": case "rar": case "gz": case "tar": return "Archive";
                case "txt": case "md": case "log": case "csv": return "Text";
                case "pdf": return "PDF document";
                case "exe": case "msi": return "Application";
                default: return extension.Length == 0 || extension.Length > 10 ? "File" : extension.ToUpperInvariant() + " file";
            }
        }

        private static bool Matches(R2BrowserItem item, string filter)
        {
            if (filter.Length == 0) return true;
            string extension = Path.GetExtension(item.DisplayName ?? item.Key ?? string.Empty).TrimStart('.');
            return Contains(item.DisplayName, filter) || Contains(item.IsFolder ? item.Prefix : item.Key, filter) ||
                   Contains(GetDisplayedType(item), filter) || Contains(extension, filter);
        }

        private static bool Contains(string value, string filter)
        {
            return (value ?? string.Empty).IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private sealed class IndexedItem
        {
            public IndexedItem(R2BrowserItem item, int index) { Item = item; Index = index; }
            public R2BrowserItem Item { get; private set; }
            public int Index { get; private set; }
        }

        private sealed class ItemComparer : IComparer<IndexedItem>
        {
            private readonly BrowserSortColumn _column;
            private readonly BrowserSortDirection _direction;
            public ItemComparer(BrowserSortColumn column, BrowserSortDirection direction) { _column = column; _direction = direction; }

            public int Compare(IndexedItem left, IndexedItem right)
            {
                if (left.Item.IsFolder != right.Item.IsFolder) return left.Item.IsFolder ? -1 : 1;
                int comparison;
                bool applyDirection = true;
                switch (_column)
                {
                    case BrowserSortColumn.Type:
                        comparison = CompareText(GetDisplayedType(left.Item), GetDisplayedType(right.Item));
                        break;
                    case BrowserSortColumn.Size:
                        comparison = left.Item.IsFolder ? 0 : left.Item.Size.CompareTo(right.Item.Size);
                        break;
                    case BrowserSortColumn.LastModified:
                        comparison = CompareNullableDate(left.Item.LastModifiedUtc, right.Item.LastModifiedUtc);
                        if (left.Item.LastModifiedUtc.HasValue != right.Item.LastModifiedUtc.HasValue) applyDirection = false;
                        break;
                    default:
                        comparison = CompareText(left.Item.DisplayName, right.Item.DisplayName);
                        break;
                }
                if (applyDirection && _direction == BrowserSortDirection.Descending) comparison = -comparison;
                if (comparison == 0) comparison = CompareText(left.Item.DisplayName, right.Item.DisplayName);
                if (comparison == 0) comparison = string.Compare(left.Item.Identity, right.Item.Identity, StringComparison.Ordinal);
                if (comparison == 0) comparison = left.Index.CompareTo(right.Index);
                return comparison;
            }

            private static int CompareText(string left, string right) { return string.Compare(left ?? string.Empty, right ?? string.Empty, StringComparison.CurrentCultureIgnoreCase); }
            private static int CompareNullableDate(DateTime? left, DateTime? right)
            {
                if (!left.HasValue && !right.HasValue) return 0;
                if (!left.HasValue) return 1;
                if (!right.HasValue) return -1;
                return left.Value.CompareTo(right.Value);
            }
        }
    }
}
