using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;
using MetadataExtractor;

namespace CloudflareR2Uploader.Services
{
    public sealed class R2ObjectDetailsService
    {
        private const int CacheCapacity = 150;
        private readonly Dictionary<string, R2ObjectProperties> _cache = new Dictionary<string, R2ObjectProperties>(StringComparer.Ordinal);
        private readonly Queue<string> _order = new Queue<string>();
        private readonly object _gate = new object();

        public async Task<R2ObjectProperties> GetAsync(AppSettings settings, R2Credentials credentials, R2BrowserItem item, CancellationToken cancellationToken)
        {
            if (item == null) throw new ArgumentNullException("item");
            if (item.IsFolder) return CreateFolder(settings, item);
            string cacheKey = BuildCacheKey(settings, item);
            lock (_gate)
            {
                R2ObjectProperties cached;
                if (_cache.TryGetValue(cacheKey, out cached)) return cached;
            }

            using (AmazonS3Client client = R2ClientFactory.CreateClient(settings, credentials))
            {
                GetObjectMetadataResponse response = await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = settings.BucketName,
                    Key = item.Key
                }, cancellationToken).ConfigureAwait(false);
                R2ObjectProperties value = Map(settings, item, response);
                Add(cacheKey, value);
                return value;
            }
        }

        public void Invalidate(string profileId, string bucket, string keyOrPrefix)
        {
            lock (_gate)
            {
                List<string> keys = new List<string>();
                foreach (string key in _cache.Keys)
                    if (key.StartsWith((profileId ?? string.Empty) + "|" + (bucket ?? string.Empty) + "|", StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrEmpty(keyOrPrefix) || key.IndexOf("|" + keyOrPrefix, StringComparison.Ordinal) >= 0)) keys.Add(key);
                foreach (string key in keys) _cache.Remove(key);
            }
        }

        public void Clear() { lock (_gate) { _cache.Clear(); _order.Clear(); } }

        private void Add(string key, R2ObjectProperties value)
        {
            lock (_gate)
            {
                if (_cache.ContainsKey(key)) return;
                _cache[key] = value;
                _order.Enqueue(key);
                while (_cache.Count > CacheCapacity) _cache.Remove(_order.Dequeue());
            }
        }

        private static string BuildCacheKey(AppSettings settings, R2BrowserItem item)
        {
            return (settings.ActiveBucketProfileId ?? string.Empty) + "|" + (settings.BucketName ?? string.Empty) + "|" +
                   (item.Key ?? string.Empty) + "|" + (item.ETag ?? string.Empty) + "|" +
                   (item.LastModifiedUtc.HasValue ? item.LastModifiedUtc.Value.Ticks.ToString() : string.Empty);
        }

        private static R2ObjectProperties CreateFolder(AppSettings settings, R2BrowserItem item)
        {
            return new R2ObjectProperties
            {
                Name = item.DisplayName,
                KeyOrPrefix = item.Prefix,
                IsFolder = true,
                Bucket = settings.BucketName,
                ProfileName = settings.GetActiveBucketProfile().DisplayName,
                EndpointHost = R2ClientFactory.DescribeEndpoint(settings),
                Type = "Virtual folder prefix"
            };
        }

        internal static R2ObjectProperties Map(AppSettings settings, R2BrowserItem item, GetObjectMetadataResponse response)
        {
            R2ObjectProperties value = new R2ObjectProperties
            {
                Name = item.DisplayName,
                KeyOrPrefix = item.Key,
                Bucket = settings.BucketName,
                ProfileName = settings.GetActiveBucketProfile().DisplayName,
                EndpointHost = R2ClientFactory.DescribeEndpoint(settings),
                Type = BrowserViewService.GetDisplayedType(item),
                Extension = Path.GetExtension(item.DisplayName ?? item.Key ?? string.Empty),
                Size = response.ContentLength,
                LastModifiedUtc = response.LastModified == default(DateTime) ? item.LastModifiedUtc : response.LastModified.ToUniversalTime(),
                ETag = NormalizeETag(response.ETag),
                ContentType = response.Headers.ContentType,
                CacheControl = response.Headers.CacheControl,
                ContentDisposition = response.Headers.ContentDisposition,
                ContentEncoding = response.Headers.ContentEncoding,
                ContentLanguage = ReadOptional(response.Headers, "ContentLanguage"),
                ExpiresUtc = ParseDate(response.ExpiresString),
                VersionId = response.VersionId,
                StorageClass = ReadOptional(response, "StorageClass"),
                Checksum = ReadChecksums(response),
                PublicUrl = string.IsNullOrWhiteSpace(settings.PublicBaseUrl) ? null : ObjectKeyUtility.BuildPublicUrl(settings.PublicBaseUrl, item.Key)
            };
            foreach (string key in response.Metadata.Keys) value.CustomMetadata[key] = response.Metadata[key];
            return value;
        }

        internal static string NormalizeETag(string etag) { return string.IsNullOrWhiteSpace(etag) ? string.Empty : etag.Trim().Trim('"'); }
        internal static void AddExtractedMetadata(R2ObjectProperties properties, string localPath)
        {
            if (properties == null || string.IsNullOrEmpty(localPath)) return;
            try
            {
                string[] useful = { "Image Width", "Image Height", "Make", "Model", "Date/Time Original", "Orientation", "Color Space", "Compression Type", "Format" };
                foreach (MetadataExtractor.Directory directory in ImageMetadataReader.ReadMetadata(localPath))
                {
                    foreach (MetadataExtractor.Tag tag in directory.Tags)
                    {
                        foreach (string name in useful)
                        {
                            if (!string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                            string key = directory.Name + " · " + tag.Name;
                            if (!properties.ExtractedMetadata.ContainsKey(key)) properties.ExtractedMetadata[key] = tag.Description ?? string.Empty;
                            break;
                        }
                    }
                }
            }
            catch (ImageProcessingException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
        private static DateTime? ParseDate(string value) { DateTime parsed; return DateTime.TryParse(value, out parsed) ? parsed.ToUniversalTime() : (DateTime?)null; }
        private static string ReadOptional(object value, string propertyName)
        {
            PropertyInfo property = value.GetType().GetProperty(propertyName);
            object result = property == null ? null : property.GetValue(value, null);
            return result == null ? string.Empty : Convert.ToString(result);
        }
        private static string ReadChecksums(object value)
        {
            string[] names = { "ChecksumCRC32", "ChecksumCRC32C", "ChecksumSHA1", "ChecksumSHA256" };
            List<string> results = new List<string>();
            foreach (string name in names)
            {
                string text = ReadOptional(value, name);
                if (text.Length > 0) results.Add(name.Replace("Checksum", string.Empty) + ": " + text);
            }
            return string.Join(", ", results.ToArray());
        }
    }
}
