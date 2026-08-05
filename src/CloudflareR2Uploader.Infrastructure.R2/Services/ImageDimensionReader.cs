using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using MetadataExtractor;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Reads pixel dimensions from an image file header.
    /// <para>
    /// This replaces the previous <c>System.Drawing.Image.FromFile</c> call. GDI+ decodes
    /// the whole bitmap just to report two integers, is Windows-only on modern .NET, and
    /// would add a <c>System.Drawing.Common</c> dependency. MetadataExtractor is already a
    /// dependency, reads only the header, and never allocates the pixel buffer.
    /// </para>
    /// </summary>
    public static class ImageDimensionReader
    {
        // Tag names are stable across MetadataExtractor's PNG, JPEG, GIF, BMP, WebP and
        // Exif directories, so matching on the name avoids binding to per-format tag ids.
        private static readonly string[] WidthTagNames = { "Image Width", "Exif Image Width", "Width" };
        private static readonly string[] HeightTagNames = { "Image Height", "Exif Image Height", "Height" };

        private static readonly Regex LeadingInteger = new Regex(@"^\s*(\d{1,7})", RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns true when both dimensions could be read. Returns false — without throwing —
        /// for unreadable, unsupported or corrupt files.
        /// </summary>
        public static bool TryRead(string localPath, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (string.IsNullOrEmpty(localPath)) return false;

            try
            {
                IReadOnlyList<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(localPath);
                foreach (MetadataExtractor.Directory directory in directories)
                {
                    foreach (MetadataExtractor.Tag tag in directory.Tags)
                    {
                        if (width == 0 && Matches(tag.Name, WidthTagNames)) width = ParseDimension(tag.Description);
                        else if (height == 0 && Matches(tag.Name, HeightTagNames)) height = ParseDimension(tag.Description);

                        if (width > 0 && height > 0) return true;
                    }
                }
            }
            catch (ImageProcessingException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }

            if (width > 0 && height > 0) return true;

            width = 0;
            height = 0;
            return false;
        }

        private static bool Matches(string tagName, string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (string.Equals(tagName, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Descriptions are localised strings such as "512 pixels" or "512", so the leading
        /// integer is taken rather than the whole value being parsed.
        /// </summary>
        private static int ParseDimension(string description)
        {
            if (string.IsNullOrEmpty(description)) return 0;

            Match match = LeadingInteger.Match(description);
            if (!match.Success) return 0;

            int value;
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out value)) return 0;
            return value > 0 ? value : 0;
        }
    }
}
