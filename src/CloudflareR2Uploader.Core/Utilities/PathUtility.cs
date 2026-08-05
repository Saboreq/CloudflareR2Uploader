using System;
using System.Collections.Generic;
using System.IO;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>
    /// Path helpers that cope with long paths, Unicode names and unreadable folders.
    /// Enumeration never throws for an inaccessible subtree; it reports it instead.
    /// </summary>
    public static class PathUtility
    {
        /// <summary>
        /// .NET Framework 4.8 with <c>longPathAware</c> handles most long paths, but the
        /// extended-length prefix is still the reliable way to open very long paths.
        /// </summary>
        public static string ToExtendedLengthPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            if (path.Length < 248) return path;

            if (path.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + path.Substring(2);

            // Only rooted paths can be prefixed.
            if (path.Length >= 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))
                return @"\\?\" + path;

            return path;
        }

        /// <summary>
        /// Returns the path of <paramref name="fullPath"/> relative to
        /// <paramref name="baseDirectory"/>, using '/' separators. Falls back to the file
        /// name when the two are unrelated.
        /// </summary>
        public static string GetRelativePath(string baseDirectory, string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;
            if (string.IsNullOrEmpty(baseDirectory)) return Path.GetFileName(fullPath);

            string root = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(root.Length).Replace('\\', '/');

            return Path.GetFileName(fullPath);
        }

        /// <summary>
        /// Recursively enumerates files below <paramref name="directory"/>. Folders that
        /// cannot be read are added to <paramref name="inaccessible"/> and skipped, so one
        /// protected subfolder never aborts the whole scan.
        /// </summary>
        public static List<string> EnumerateFilesSafely(string directory, List<string> inaccessible)
        {
            List<string> results = new List<string>();
            if (string.IsNullOrEmpty(directory)) return results;

            Stack<string> pending = new Stack<string>();
            pending.Push(directory);

            while (pending.Count > 0)
            {
                string current = pending.Pop();

                try
                {
                    foreach (string file in Directory.GetFiles(current))
                        results.Add(file);
                }
                catch (UnauthorizedAccessException) { if (inaccessible != null) inaccessible.Add(current); }
                catch (DirectoryNotFoundException) { if (inaccessible != null) inaccessible.Add(current); }
                catch (PathTooLongException) { if (inaccessible != null) inaccessible.Add(current); }
                catch (IOException) { if (inaccessible != null) inaccessible.Add(current); }

                try
                {
                    foreach (string sub in Directory.GetDirectories(current))
                    {
                        // Do not follow reparse points: they can form cycles.
                        try
                        {
                            FileAttributes attributes = File.GetAttributes(sub);
                            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) continue;
                        }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }

                        pending.Push(sub);
                    }
                }
                catch (UnauthorizedAccessException) { if (inaccessible != null) inaccessible.Add(current); }
                catch (DirectoryNotFoundException) { if (inaccessible != null) inaccessible.Add(current); }
                catch (PathTooLongException) { if (inaccessible != null) inaccessible.Add(current); }
                catch (IOException) { if (inaccessible != null) inaccessible.Add(current); }
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results;
        }

        /// <summary>Produces a file-system-safe name for a state file derived from a key.</summary>
        public static string SanitizeForFileName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "_";

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] buffer = value.ToCharArray();
            for (int i = 0; i < buffer.Length; i++)
            {
                if (Array.IndexOf(invalid, buffer[i]) >= 0) buffer[i] = '_';
            }

            string result = new string(buffer);
            return result.Length > 80 ? result.Substring(0, 80) : result;
        }

        /// <summary>True when the path points at an existing directory.</summary>
        public static bool IsDirectory(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && Directory.Exists(path);
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
