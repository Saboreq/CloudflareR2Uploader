using System;
using System.Collections.Generic;
using System.IO;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Maps file extensions to content types. Unknown extensions fall back to
    /// <see cref="DefaultContentType"/> rather than being left unset, so R2 never has to guess.
    /// </summary>
    public static class MimeTypeService
    {
        public const string DefaultContentType = "application/octet-stream";

        private static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Images
                { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" }, { ".jpe", "image/jpeg" },
                { ".png", "image/png" }, { ".gif", "image/gif" }, { ".bmp", "image/bmp" },
                { ".webp", "image/webp" }, { ".svg", "image/svg+xml" }, { ".ico", "image/x-icon" },
                { ".tif", "image/tiff" }, { ".tiff", "image/tiff" }, { ".avif", "image/avif" },
                { ".heic", "image/heic" }, { ".heif", "image/heif" }, { ".jxl", "image/jxl" },
                { ".psd", "image/vnd.adobe.photoshop" }, { ".raw", "image/x-panasonic-raw" },
                { ".cr2", "image/x-canon-cr2" }, { ".nef", "image/x-nikon-nef" }, { ".dng", "image/x-adobe-dng" },

                // Video
                { ".mp4", "video/mp4" }, { ".m4v", "video/x-m4v" }, { ".mov", "video/quicktime" },
                { ".avi", "video/x-msvideo" }, { ".wmv", "video/x-ms-wmv" }, { ".mkv", "video/x-matroska" },
                { ".webm", "video/webm" }, { ".flv", "video/x-flv" }, { ".mpeg", "video/mpeg" },
                { ".mpg", "video/mpeg" }, { ".3gp", "video/3gpp" }, { ".ts", "video/mp2t" },
                { ".mts", "video/mp2t" }, { ".m2ts", "video/mp2t" }, { ".ogv", "video/ogg" },

                // Audio
                { ".mp3", "audio/mpeg" }, { ".wav", "audio/wav" }, { ".flac", "audio/flac" },
                { ".aac", "audio/aac" }, { ".m4a", "audio/mp4" }, { ".ogg", "audio/ogg" },
                { ".oga", "audio/ogg" }, { ".opus", "audio/opus" }, { ".wma", "audio/x-ms-wma" },
                { ".mid", "audio/midi" }, { ".midi", "audio/midi" }, { ".aiff", "audio/aiff" },
                { ".aif", "audio/aiff" }, { ".weba", "audio/webm" },

                // Archives
                { ".zip", "application/zip" }, { ".rar", "application/vnd.rar" },
                { ".7z", "application/x-7z-compressed" }, { ".tar", "application/x-tar" },
                { ".gz", "application/gzip" }, { ".tgz", "application/gzip" },
                { ".bz2", "application/x-bzip2" }, { ".xz", "application/x-xz" },
                { ".zst", "application/zstd" }, { ".cab", "application/vnd.ms-cab-compressed" },
                { ".iso", "application/x-iso9660-image" }, { ".dmg", "application/x-apple-diskimage" },

                // Executables and packages
                { ".exe", "application/vnd.microsoft.portable-executable" },
                { ".dll", "application/vnd.microsoft.portable-executable" },
                { ".msi", "application/x-msi" }, { ".appx", "application/appx" },
                { ".msix", "application/msix" }, { ".apk", "application/vnd.android.package-archive" },
                { ".deb", "application/vnd.debian.binary-package" }, { ".rpm", "application/x-rpm" },
                { ".jar", "application/java-archive" }, { ".wasm", "application/wasm" },

                // Documents
                { ".pdf", "application/pdf" },
                { ".doc", "application/msword" },
                { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
                { ".xls", "application/vnd.ms-excel" },
                { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
                { ".ppt", "application/vnd.ms-powerpoint" },
                { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
                { ".odt", "application/vnd.oasis.opendocument.text" },
                { ".ods", "application/vnd.oasis.opendocument.spreadsheet" },
                { ".odp", "application/vnd.oasis.opendocument.presentation" },
                { ".rtf", "application/rtf" }, { ".epub", "application/epub+zip" },

                // Text and data
                { ".json", "application/json" }, { ".jsonl", "application/x-ndjson" },
                { ".ndjson", "application/x-ndjson" },
                { ".xml", "application/xml" }, { ".yaml", "application/yaml" }, { ".yml", "application/yaml" },
                { ".toml", "application/toml" }, { ".csv", "text/csv" }, { ".tsv", "text/tab-separated-values" },
                { ".txt", "text/plain" }, { ".log", "text/plain" }, { ".md", "text/markdown" },
                { ".html", "text/html" }, { ".htm", "text/html" }, { ".css", "text/css" },
                { ".js", "text/javascript" }, { ".mjs", "text/javascript" }, { ".ics", "text/calendar" },
                { ".sql", "application/sql" },

                // Fonts
                { ".woff", "font/woff" }, { ".woff2", "font/woff2" },
                { ".ttf", "font/ttf" }, { ".otf", "font/otf" }, { ".eot", "application/vnd.ms-fontobject" }
            };

        /// <summary>Content type for a path, based on its extension only.</summary>
        public static string GetContentType(string path)
        {
            if (string.IsNullOrEmpty(path)) return DefaultContentType;

            string extension;
            try
            {
                extension = Path.GetExtension(path);
            }
            catch (ArgumentException)
            {
                // Invalid characters in the path: fall back rather than throw.
                return DefaultContentType;
            }

            if (string.IsNullOrEmpty(extension)) return DefaultContentType;

            string contentType;
            return Map.TryGetValue(extension, out contentType) ? contentType : DefaultContentType;
        }

        /// <summary>Number of extensions currently mapped. Used by the tests.</summary>
        public static int MappedExtensionCount { get { return Map.Count; } }
    }
}
