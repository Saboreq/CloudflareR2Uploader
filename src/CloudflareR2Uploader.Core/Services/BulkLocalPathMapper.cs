using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    public sealed class BulkLocalPathMapper
    {
        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CLOCK$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public void Map(BulkObjectOperationPlan plan, string destinationRoot)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            string root = Path.GetFullPath(destinationRoot ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Dictionary<string, int> used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (BulkObjectEntry entry in plan.Objects)
            {
                string relative = SanitizeRelativePath(entry.RelativePath);
                string candidate = EnsureUnique(relative, used);
                string full = Path.GetFullPath(Path.Combine(root, candidate));
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("An object key mapped outside the selected destination.");
                entry.RelativePath = candidate;
                entry.LocalPath = full;
            }

            for (int i = 0; i < plan.EmptyDirectoryPaths.Count; i++)
                plan.EmptyDirectoryPaths[i] = Path.GetFullPath(Path.Combine(root, SanitizeRelativePath(plan.EmptyDirectoryPaths[i])));
            plan.DestinationDirectory = root.TrimEnd(Path.DirectorySeparatorChar);
        }

        public string SanitizeRelativePath(string relativePath)
        {
            string unified = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
            List<string> safe = new List<string>();
            foreach (string raw in unified.Split('/'))
            {
                string segment = raw;
                if (segment == "." || segment == ".." || segment.Length == 0) segment = "_";
                char[] invalid = Path.GetInvalidFileNameChars();
                char[] chars = segment.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                    if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ':') chars[i] = '_';
                segment = new string(chars).TrimEnd(' ', '.');
                if (segment.Length == 0) segment = "_";
                string stem = Path.GetFileNameWithoutExtension(segment);
                if (ReservedNames.Contains(stem)) segment = "_" + segment;
                if (segment.Length > 120) segment = ShortenSegment(segment);
                safe.Add(segment);
            }
            return string.Join(Path.DirectorySeparatorChar.ToString(), safe.ToArray());
        }

        private static string ShortenSegment(string segment)
        {
            string extension = Path.GetExtension(segment);
            if (extension.Length > 16) extension = string.Empty;
            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(segment));
                StringBuilder text = new StringBuilder(12);
                for (int i = 0; i < 6; i++) text.Append(bytes[i].ToString("x2"));
                hash = text.ToString();
            }
            int stemLength = Math.Max(1, 120 - extension.Length - hash.Length - 1);
            return Path.GetFileNameWithoutExtension(segment).Substring(0, Math.Min(stemLength, Path.GetFileNameWithoutExtension(segment).Length)) + "-" + hash + extension;
        }

        private static string EnsureUnique(string relative, IDictionary<string, int> used)
        {
            int existing;
            if (!used.TryGetValue(relative, out existing))
            {
                used[relative] = 0;
                return relative;
            }

            string directory = Path.GetDirectoryName(relative) ?? string.Empty;
            string fileName = Path.GetFileNameWithoutExtension(relative);
            string extension = Path.GetExtension(relative);
            int index = existing + 1;
            string candidate;
            do
            {
                candidate = Path.Combine(directory, fileName + " (" + index + ")" + extension);
                index++;
            }
            while (used.ContainsKey(candidate));
            used[relative] = index - 1;
            used[candidate] = 0;
            return candidate;
        }
    }

    public static class BulkConflictNameUtility
    {
        public static string FindAvailable(string path, Func<string, bool> exists)
        {
            if (exists == null) throw new ArgumentNullException("exists");
            if (!exists(path)) return path;
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int index = 1; ; index++)
            {
                string candidate = Path.Combine(directory, name + " (" + index + ")" + extension);
                if (!exists(candidate)) return candidate;
            }
        }
    }
}
