using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>
    /// Builds and normalises S3/R2 object keys. R2 keys always use '/', never '\', must not
    /// start with '/', must not contain empty segments, and must not be empty.
    /// </summary>
    public static class ObjectKeyUtility
    {
        /// <summary>R2/S3 object keys are limited to 1024 bytes when UTF-8 encoded.</summary>
        public const int MaxKeyLengthBytes = 1024;

        /// <summary>
        /// Normalises an arbitrary user-supplied path fragment into a key body:
        /// backslashes become forward slashes, duplicate and leading/trailing slashes are
        /// removed, and "." / ".." segments are resolved away.
        /// </summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            string unified = value.Replace('\\', '/');
            string[] rawSegments = unified.Split('/');
            List<string> segments = new List<string>(rawSegments.Length);

            foreach (string raw in rawSegments)
            {
                string segment = raw.Trim();
                if (segment.Length == 0) continue;          // collapses "//" and leading/trailing "/"
                if (segment == ".") continue;
                if (segment == "..")
                {
                    if (segments.Count > 0) segments.RemoveAt(segments.Count - 1);
                    continue;                                // never allow escaping above the prefix
                }
                segments.Add(segment);
            }

            return string.Join("/", segments.ToArray());
        }

        /// <summary>
        /// Normalises a prefix for display and storage. Returns either an empty string or a
        /// value with no leading slash and exactly one trailing slash.
        /// </summary>
        public static string NormalizePrefix(string prefix)
        {
            string body = Normalize(prefix);
            return body.Length == 0 ? string.Empty : body + "/";
        }

        /// <summary>
        /// Joins a prefix and a relative path into a final object key.
        /// </summary>
        public static string Combine(string prefix, string relativePath)
        {
            string left = Normalize(prefix);
            string right = Normalize(relativePath);

            if (left.Length == 0) return right;
            if (right.Length == 0) return left;
            return left + "/" + right;
        }

        /// <summary>
        /// True when the key is acceptable for an R2 PUT: non-empty, no leading slash, no
        /// empty segments, no control characters, and within the byte-length limit.
        /// </summary>
        public static bool IsValidKey(string key)
        {
            string reason;
            return TryValidate(key, out reason);
        }

        /// <summary>Validates a key and explains the first problem found.</summary>
        public static bool TryValidate(string key, out string reason)
        {
            if (string.IsNullOrEmpty(key))
            {
                reason = "The object key is empty.";
                return false;
            }
            if (key[0] == '/')
            {
                reason = "The object key must not start with '/'.";
                return false;
            }
            if (key.EndsWith('/'))
            {
                reason = "The object key must not end with '/'.";
                return false;
            }
            if (key.Contains("//", StringComparison.Ordinal))
            {
                reason = "The object key must not contain an empty path segment ('//').";
                return false;
            }
            if (key.Contains('\\'))
            {
                reason = "The object key must use '/' as its separator, not '\\'.";
                return false;
            }

            foreach (char c in key)
            {
                if (c < 0x20 || c == 0x7F)
                {
                    reason = "The object key contains a control character.";
                    return false;
                }
            }

            int byteLength = Encoding.UTF8.GetByteCount(key);
            if (byteLength > MaxKeyLengthBytes)
            {
                reason = string.Format(
                    CultureInfo.CurrentCulture,
                    "The object key is {0} bytes; the maximum is {1} bytes.",
                    byteLength, MaxKeyLengthBytes);
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// Produces the next candidate when auto-renaming on conflict:
        /// <c>photos/trip.tar.gz</c> becomes <c>photos/trip (1).tar.gz</c>.
        /// The extension is the final dot-suffix only, so multi-dot names stay intact.
        /// </summary>
        public static string AppendDuplicateSuffix(string key, int index)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key must not be empty.", nameof(key));
            if (index < 1) throw new ArgumentOutOfRangeException(nameof(index), "Duplicate index starts at 1.");

            int lastSlash = key.LastIndexOf('/');
            string directory = lastSlash >= 0 ? key.Substring(0, lastSlash + 1) : string.Empty;
            string name = lastSlash >= 0 ? key.Substring(lastSlash + 1) : key;

            // A leading dot means a dotfile (".gitignore"), which has no extension.
            int lastDot = name.LastIndexOf('.');
            string stem = (lastDot > 0) ? name.Substring(0, lastDot) : name;
            string extension = (lastDot > 0) ? name.Substring(lastDot) : string.Empty;

            return directory + stem + " (" + index.ToString(CultureInfo.InvariantCulture) + ")" + extension;
        }

        /// <summary>
        /// Percent-encodes a key for use in a URL while keeping '/' as a path separator, so
        /// the displayed public link is clickable.
        /// </summary>
        public static string EncodeForUrl(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            StringBuilder builder = new StringBuilder(key.Length + 16);
            byte[] bytes = Encoding.UTF8.GetBytes(key);
            foreach (byte b in bytes)
            {
                char c = (char)b;
                bool unreserved =
                    (c >= 'A' && c <= 'Z') ||
                    (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') ||
                    c == '-' || c == '_' || c == '.' || c == '~' || c == '/';

                if (unreserved) builder.Append(c);
                else builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        /// <summary>Builds a display URL from an optional public base domain and a key.</summary>
        public static string BuildPublicUrl(string publicBaseUrl, string key)
        {
            if (string.IsNullOrWhiteSpace(publicBaseUrl) || string.IsNullOrEmpty(key)) return null;

            string baseUrl = publicBaseUrl.Trim();
            if (baseUrl.IndexOf("://", StringComparison.Ordinal) < 0) baseUrl = "https://" + baseUrl;
            baseUrl = baseUrl.TrimEnd('/');

            return baseUrl + "/" + EncodeForUrl(key);
        }
    }
}
