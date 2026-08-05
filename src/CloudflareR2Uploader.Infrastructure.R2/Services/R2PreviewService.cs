using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using MetadataExtractor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CloudflareR2Uploader.Services
{
    public sealed class R2PreviewService
    {
        public const long TextLimitBytes = 1L * 1024L * 1024L;
        public const long ImageLimitBytes = 20L * 1024L * 1024L;
        public const long PdfLimitBytes = 25L * 1024L * 1024L;
        private readonly PreviewCacheService _cache;

        public R2PreviewService(PreviewCacheService cache) { _cache = cache ?? throw new ArgumentNullException("cache"); }

        public async Task<R2PreviewResult> GetAsync(AppSettings settings, R2Credentials credentials, R2BrowserItem item, string contentType, CancellationToken cancellationToken)
        {
            R2PreviewKind kind = Classify(contentType, item == null ? null : item.Key);
            if (item == null || item.IsFolder) return new R2PreviewResult { Kind = R2PreviewKind.Unsupported, Warning = "Virtual folders are R2 key prefixes and do not have preview content." };
            long limit = kind == R2PreviewKind.Image ? ImageLimitBytes : kind == R2PreviewKind.Pdf ? PdfLimitBytes : TextLimitBytes;
            if (kind == R2PreviewKind.Unsupported) return new R2PreviewResult { Kind = kind, ContentType = contentType, Warning = "No safe preview is available for this object type." };
            if (item.Size > limit && (kind == R2PreviewKind.Image || kind == R2PreviewKind.Pdf))
                return new R2PreviewResult { Kind = kind, ContentType = contentType, Warning = "This object is too large to preview safely. Download it instead." };

            if (kind == R2PreviewKind.Image || kind == R2PreviewKind.Pdf)
            {
                string path = _cache.CreatePath(Path.GetExtension(item.Key));
                await DownloadFileAsync(settings, credentials, item.Key, path, cancellationToken).ConfigureAwait(false);
                _cache.EnforceBound();
                R2PreviewResult result = new R2PreviewResult { Kind = kind, ContentType = contentType, LocalPath = path };
                if (kind == R2PreviewKind.Image) ExtractImageDetails(result, path);
                return result;
            }

            byte[] data = await DownloadTextPrefixAsync(settings, credentials, item.Key, limit, cancellationToken).ConfigureAwait(false);
            R2PreviewResult textResult = FormatText(kind, data, item.Size > data.LongLength);
            textResult.ContentType = contentType;
            return textResult;
        }

        public static R2PreviewKind Classify(string contentType, string key)
        {
            string mime = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
            string extension = Path.GetExtension(key ?? string.Empty).ToLowerInvariant();
            if (mime == "application/pdf" || extension == ".pdf") return R2PreviewKind.Pdf;
            if (mime == "application/json" || mime.EndsWith("+json", StringComparison.Ordinal) || extension == ".json") return R2PreviewKind.Json;
            if (mime == "image/svg+xml" || extension == ".svg") return R2PreviewKind.SvgSource;
            if (mime == "application/xml" || mime == "text/xml" || mime.EndsWith("+xml", StringComparison.Ordinal) || extension == ".xml") return R2PreviewKind.Xml;
            if (mime == "text/csv" || extension == ".csv") return R2PreviewKind.Csv;
            if (extension == ".md" || extension == ".markdown") return R2PreviewKind.Markdown;
            if (mime == "text/html" || extension == ".html" || extension == ".htm") return R2PreviewKind.HtmlSource;
            if (mime.StartsWith("image/", StringComparison.Ordinal) || new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff" }.Contains(extension)) return R2PreviewKind.Image;
            if (mime.StartsWith("text/", StringComparison.Ordinal) || new[] { ".txt", ".log", ".ini", ".cfg", ".conf", ".cs", ".js", ".ts", ".css", ".ps1", ".yml", ".yaml" }.Contains(extension)) return R2PreviewKind.Text;
            return R2PreviewKind.Unsupported;
        }

        internal static R2PreviewResult FormatText(R2PreviewKind kind, byte[] bytes, bool truncated)
        {
            if (IsProbablyBinary(bytes)) return new R2PreviewResult { Kind = R2PreviewKind.Unsupported, Warning = "The object appears to contain binary data." };
            string text = Decode(bytes ?? new byte[0]);
            string warning = null;
            if (kind == R2PreviewKind.Json)
            {
                try { text = JToken.Parse(text).ToString(Newtonsoft.Json.Formatting.Indented); }
                catch (JsonException) { warning = "The JSON is invalid, so the original text is shown."; }
            }
            else if (kind == R2PreviewKind.Xml)
            {
                try
                {
                    XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                    XmlDocument document = new XmlDocument { XmlResolver = null };
                    using (StringReader input = new StringReader(text))
                    using (XmlReader reader = XmlReader.Create(input, settings)) document.Load(reader);
                    StringBuilder output = new StringBuilder();
                    using (XmlWriter writer = XmlWriter.Create(output, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false })) document.Save(writer);
                    text = output.ToString();
                }
                catch (XmlException) { warning = "The XML could not be formatted safely, so the original text is shown."; }
            }
            return new R2PreviewResult { Kind = kind, Text = text, Truncated = truncated, Warning = warning };
        }

        internal static bool IsProbablyBinary(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return false;
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))) return false;
            int control = 0;
            int sample = Math.Min(bytes.Length, 8192);
            for (int i = 0; i < sample; i++)
            {
                byte value = bytes[i];
                if (value == 0)
                {
                    return true;
                }
                if (value < 8 || (value > 13 && value < 32)) control++;
            }
            return control > sample / 20;
        }

        internal static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            int offset = 0;
            Encoding encoding = new UTF8Encoding(false, true);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { encoding = Encoding.UTF8; offset = 3; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { encoding = Encoding.Unicode; offset = 2; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { encoding = Encoding.BigEndianUnicode; offset = 2; }
            try { return encoding.GetString(bytes, offset, bytes.Length - offset).Replace("\0", "\uFFFD"); }
            catch (DecoderFallbackException) { return Encoding.Default.GetString(bytes, offset, bytes.Length - offset).Replace("\0", "\uFFFD"); }
        }

        private static async Task<byte[]> DownloadTextPrefixAsync(AppSettings settings, R2Credentials credentials, string key, long limit, CancellationToken cancellationToken)
        {
            using (AmazonS3Client client = R2ClientFactory.CreateClient(settings, credentials))
            using (GetObjectResponse response = await client.GetObjectAsync(new GetObjectRequest { BucketName = settings.BucketName, Key = key, ByteRange = new ByteRange(0, limit - 1) }, cancellationToken).ConfigureAwait(false))
            using (MemoryStream memory = new MemoryStream())
            {
                await response.ResponseStream.CopyToAsync(memory, 81920, cancellationToken).ConfigureAwait(false);
                return memory.ToArray();
            }
        }

        private static async Task DownloadFileAsync(AppSettings settings, R2Credentials credentials, string key, string path, CancellationToken cancellationToken)
        {
            try
            {
                using (AmazonS3Client client = R2ClientFactory.CreateClient(settings, credentials))
                using (GetObjectResponse response = await client.GetObjectAsync(new GetObjectRequest { BucketName = settings.BucketName, Key = key }, cancellationToken).ConfigureAwait(false))
                using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 81920, true))
                    await response.ResponseStream.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
            }
            catch { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } throw; }
        }

        private static void ExtractImageDetails(R2PreviewResult result, string path)
        {
            try
            {
                int width;
                int height;
                if (ImageDimensionReader.TryRead(path, out width, out height)) { result.PixelWidth = width; result.PixelHeight = height; }
                IReadOnlyList<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(path);
                MetadataExtractor.Directory camera = directories.FirstOrDefault(value => value.Name.IndexOf("Exif", StringComparison.OrdinalIgnoreCase) >= 0);
                if (camera != null)
                {
                    string make = camera.Tags.Where(value => value.Name.IndexOf("Make", StringComparison.OrdinalIgnoreCase) >= 0).Select(value => value.Description).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(make)) result.Warning = "Camera: " + make;
                }
            }
            catch (ImageProcessingException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (OutOfMemoryException) { }
        }
    }
}
