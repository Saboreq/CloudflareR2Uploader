using System;
using System.Collections.Generic;
using System.IO;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    public sealed class PreviewCacheService : IDisposable
    {
        public const long MaximumCacheBytes = 100L * 1024L * 1024L;
        private readonly string _root;
        private readonly string _session;

        public PreviewCacheService(string root = null)
        {
            _root = Path.GetFullPath(string.IsNullOrEmpty(root) ? AppPaths.PreviewCacheDirectory : root);
            Directory.CreateDirectory(_root);
            CleanupStaleSessions(_root, TimeSpan.FromDays(2));
            _session = Path.Combine(_root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_session);
        }

        public string SessionDirectory { get { return _session; } }

        public string CreatePath(string extension)
        {
            string safeExtension = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension.Trim();
            if (safeExtension.Length > 12 || safeExtension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) safeExtension = string.Empty;
            if (safeExtension.Length > 0 && safeExtension[0] != '.') safeExtension = "." + safeExtension;
            string path = Path.GetFullPath(Path.Combine(_session, Guid.NewGuid().ToString("N") + safeExtension));
            if (!path.StartsWith(_session + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid preview cache path.");
            return path;
        }

        public void EnforceBound()
        {
            FileInfo[] files = new DirectoryInfo(_root).GetFiles("*", SearchOption.AllDirectories);
            Array.Sort(files, (left, right) => left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc));
            long total = 0;
            foreach (FileInfo file in files) total += file.Length;
            foreach (FileInfo file in files)
            {
                if (total <= MaximumCacheBytes) break;
                long length = file.Length;
                TryDelete(file.FullName);
                total -= length;
            }
        }

        public void ClearSession()
        {
            if (!Directory.Exists(_session)) return;
            foreach (string file in Directory.GetFiles(_session)) TryDelete(file);
        }

        public static void CleanupStaleSessions(string root, TimeSpan age)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string directory in Directory.GetDirectories(fullRoot))
            {
                string full = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) continue;
                try { if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.Subtract(age)) Directory.Delete(directory, true); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_session)) Directory.Delete(_session, true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
