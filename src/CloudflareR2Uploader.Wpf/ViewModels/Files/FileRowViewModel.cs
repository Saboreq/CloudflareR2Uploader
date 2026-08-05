using System;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CloudflareR2Uploader.Wpf.ViewModels.Files
{
    /// <summary>
    /// One row of the Files table.
    /// <para>
    /// Deliberately a thin, immutable projection of <see cref="R2BrowserItem"/>: rows are
    /// recreated when a page is listed and are otherwise read-only, so the DataGrid can
    /// recycle containers freely and no per-row change notification is needed.
    /// </para>
    /// </summary>
    public sealed partial class FileRowViewModel : ObservableObject
    {
        public FileRowViewModel(R2BrowserItem item)
        {
            Item = item;
            DisplayName = item.DisplayName ?? string.Empty;
            TypeText = BrowserViewService.GetDisplayedType(item);
            IsFolder = item.IsFolder;

            // A folder prefix has no meaningful size: R2 would have to enumerate every object
            // under it to invent one. The design shows an em dash, and so does this.
            SizeText = item.IsFolder ? "—" : FileSizeFormatter.Format(item.Size);

            ModifiedText = item.LastModifiedUtc.HasValue
                ? item.LastModifiedUtc.Value.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)
                : "—";

            Identity = item.Identity;
            KeyOrPrefix = item.IsFolder ? item.Prefix ?? string.Empty : item.Key ?? string.Empty;
            IconKey = ResolveIconKey(item);
        }

        public R2BrowserItem Item { get; }

        public string Identity { get; }

        public string DisplayName { get; }

        public string KeyOrPrefix { get; }

        public string TypeText { get; }

        public string SizeText { get; }

        public string ModifiedText { get; }

        public bool IsFolder { get; }

        /// <summary>Resource key of the icon geometry for this row's kind.</summary>
        public string IconKey { get; }

        /// <summary>Bound to the row checkbox; the view syncs it with the grid's selection.</summary>
        [ObservableProperty]
        private bool _isSelected;

        /// <summary>Full key shown as the row tooltip when the name is trimmed.</summary>
        public string ToolTipText => KeyOrPrefix;

        public string AutomationName =>
            IsFolder
                ? "Folder " + DisplayName + ", modified " + ModifiedText
                : DisplayName + ", " + TypeText + ", " + SizeText + ", modified " + ModifiedText;

        private static string ResolveIconKey(R2BrowserItem item)
        {
            if (item.IsFolder) return "Icon.Folder";

            string extension = Path.GetExtension(item.DisplayName ?? item.Key ?? string.Empty)
                .TrimStart('.')
                .ToLowerInvariant();

            return extension switch
            {
                "jpg" or "jpeg" or "png" or "gif" or "webp" or "bmp" or "tif" or "tiff" or "svg" or "ico"
                    => "Icon.FileImage",
                "mp4" or "mov" or "avi" or "mkv" or "webm" or "m4v" or "mpeg" or "mpg"
                    => "Icon.FileVideo",
                "mp3" or "wav" or "flac" or "ogg" or "m4a" or "aac" or "opus"
                    => "Icon.FileAudio",
                "zip" or "7z" or "rar" or "gz" or "tar" or "tgz" or "bz2" or "xz" or "zst"
                    => "Icon.FileArchive",
                "exe" or "msi" or "appimage" or "deb" or "rpm" or "apk" or "bin"
                    => "Icon.FileBinary",
                "dmg" or "iso" or "img"
                    => "Icon.FileDisk",
                "sig" or "asc" or "pem" or "pub" or "cer" or "crt"
                    => "Icon.FileSignature",
                _ => "Icon.File"
            };
        }
    }
}
