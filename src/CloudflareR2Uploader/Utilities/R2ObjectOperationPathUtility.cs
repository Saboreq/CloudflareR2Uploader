using System;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>Pure destination-key calculations for browser copy, paste and move actions.</summary>
    public static class R2ObjectOperationPathUtility
    {
        public static string GetLeafName(string keyOrPrefix, bool isFolder)
        {
            string value = keyOrPrefix ?? string.Empty;
            if (isFolder) value = value.TrimEnd('/');
            int slash = value.LastIndexOf('/');
            return slash >= 0 ? value.Substring(slash + 1) : value;
        }

        public static string CombineWithPrefix(string targetPrefix, string leafName, bool isFolder)
        {
            string prefix = R2BrowserPathUtility.NormalizePrefix(targetPrefix);
            string leaf = leafName ?? string.Empty;
            if (leaf.Length == 0 || leaf.IndexOf('/') >= 0 || leaf.IndexOf('\\') >= 0)
                throw new ArgumentException("A single object or folder name is required.", "leafName");
            string combined = prefix + leaf;
            return isFolder ? R2BrowserPathUtility.NormalizePrefix(combined) : combined;
        }

        public static bool TryValidateLeafName(string leafName, out string reason)
        {
            if (string.IsNullOrWhiteSpace(leafName))
            {
                reason = "Enter a name.";
                return false;
            }
            if (string.Equals(leafName, ".", StringComparison.Ordinal) ||
                string.Equals(leafName, "..", StringComparison.Ordinal))
            {
                reason = "The name cannot be '.' or '..'.";
                return false;
            }
            if (leafName.IndexOf('/') >= 0 || leafName.IndexOf('\\') >= 0)
            {
                reason = "Use a single name without '/' or '\\'.";
                return false;
            }
            foreach (char value in leafName)
            {
                if (value < 0x20 || value == 0x7F)
                {
                    reason = "The name contains a control character.";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public static string AppendCopySuffix(string destination, bool isFolder, int index)
        {
            if (index < 1) throw new ArgumentOutOfRangeException("index");

            string value = isFolder ? R2BrowserPathUtility.NormalizePrefix(destination).TrimEnd('/') : destination;
            int slash = value.LastIndexOf('/');
            string directory = slash >= 0 ? value.Substring(0, slash + 1) : string.Empty;
            string name = slash >= 0 ? value.Substring(slash + 1) : value;
            string suffix = index == 1 ? " - Copy" : " - Copy (" + index + ")";

            if (isFolder) return directory + name + suffix + "/";

            int dot = name.LastIndexOf('.');
            string stem = dot > 0 ? name.Substring(0, dot) : name;
            string extension = dot > 0 ? name.Substring(dot) : string.Empty;
            return directory + stem + suffix + extension;
        }

        public static bool IsSameOrDescendantPrefix(string sourcePrefix, string destinationPrefix)
        {
            string source = R2BrowserPathUtility.NormalizePrefix(sourcePrefix);
            string destination = R2BrowserPathUtility.NormalizePrefix(destinationPrefix);
            return source.Length > 0 && destination.StartsWith(source, StringComparison.Ordinal);
        }
    }
}
