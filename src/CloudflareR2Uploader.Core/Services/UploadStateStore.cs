using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Persists resumable multipart state under
    /// <c>%LocalAppData%\CloudflareR2Uploader\UploadState\</c>.
    /// <para>
    /// A state file holds only what is needed to resume: the local file's identity, the
    /// bucket and key, the multipart upload ID, the part size, and the confirmed part
    /// numbers and ETags. It never contains credentials.
    /// </para>
    /// </summary>
    public sealed class UploadStateStore
    {
        private readonly ILoggingService _log;
        private readonly string _directory;
        private readonly object _sync = new object();

        public UploadStateStore(ILoggingService log, string directory = null)
        {
            _log = log;
            _directory = string.IsNullOrEmpty(directory) ? AppPaths.UploadStateDirectory : directory;
        }

        public string Directory { get { return _directory; } }

        /// <summary>
        /// Stable identity for a (local file, bucket, key) triple. Hashed so the file name is
        /// short and valid regardless of how long or exotic the paths are.
        /// </summary>
        public static string BuildStateId(string localFilePath, string bucketName, string objectKey)
        {
            string composite = (localFilePath ?? string.Empty).ToUpperInvariant()
                               + "|" + (bucketName ?? string.Empty)
                               + "|" + (objectKey ?? string.Empty);

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(composite));
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString().Substring(0, 32);
            }
        }

        private string GetPath(string stateId)
        {
            return Path.Combine(_directory, PathUtility.SanitizeForFileName(stateId) + ".json");
        }

        public MultipartUploadState Load(string stateId)
        {
            if (string.IsNullOrEmpty(stateId)) return null;

            lock (_sync)
            {
                string path = GetPath(stateId);
                try
                {
                    if (!File.Exists(path)) return null;

                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(MultipartUploadState));
                        MultipartUploadState state = serializer.ReadObject(stream) as MultipartUploadState;

                        if (state == null || !state.IsStructurallyValid())
                        {
                            if (_log != null) _log.Warning("UploadState.Load", "Discarding an incomplete state file: " + Path.GetFileName(path));
                            TryDeleteFile(path);
                            return null;
                        }

                        return state;
                    }
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    if (_log != null) _log.Error("UploadState.Load", "Could not read the resume state file " + Path.GetFileName(path) + ".", ex);
                    TryDeleteFile(path);
                    return null;
                }
            }
        }

        public bool Save(MultipartUploadState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (string.IsNullOrEmpty(state.StateId))
                state.StateId = BuildStateId(state.LocalFilePath, state.BucketName, state.ObjectKey);

            lock (_sync)
            {
                string path = GetPath(state.StateId);
                try
                {
                    AppPaths.EnsureDirectory(_directory);

                    string temp = path + ".tmp";
                    using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (System.Xml.XmlDictionaryWriter writer =
                           JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                    {
                        new DataContractJsonSerializer(typeof(MultipartUploadState)).WriteObject(writer, state);
                        writer.Flush();
                    }

                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temp, path);
                    return true;
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    // Losing resume state is not fatal: the upload continues, it just cannot
                    // be resumed later. Never let this take the upload down.
                    if (_log != null) _log.Error("UploadState.Save", "Could not write the resume state file.", ex);
                    return false;
                }
            }
        }

        public void Delete(string stateId)
        {
            if (string.IsNullOrEmpty(stateId)) return;
            lock (_sync)
            {
                TryDeleteFile(GetPath(stateId));
                TryDeleteFile(GetPath(stateId) + ".tmp");
            }
        }

        /// <summary>All state files currently on disk, newest first. Unreadable files are skipped.</summary>
        public List<MultipartUploadState> LoadAll()
        {
            List<MultipartUploadState> results = new List<MultipartUploadState>();

            lock (_sync)
            {
                try
                {
                    if (!System.IO.Directory.Exists(_directory)) return results;

                    foreach (string path in System.IO.Directory.GetFiles(_directory, "*.json"))
                    {
                        string id = Path.GetFileNameWithoutExtension(path);
                        MultipartUploadState state = LoadUnlocked(path, id);
                        if (state != null) results.Add(state);
                    }
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    if (_log != null) _log.Error("UploadState.LoadAll", "Could not enumerate resume state files.", ex);
                }
            }

            results.Sort(delegate (MultipartUploadState a, MultipartUploadState b)
            {
                return b.CreatedUtc.CompareTo(a.CreatedUtc);
            });
            return results;
        }

        private MultipartUploadState LoadUnlocked(string path, string stateId)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(MultipartUploadState));
                    MultipartUploadState state = serializer.ReadObject(stream) as MultipartUploadState;
                    if (state != null && state.IsStructurallyValid())
                    {
                        if (string.IsNullOrEmpty(state.StateId)) state.StateId = stateId;
                        return state;
                    }
                }
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                if (_log != null) _log.Warning("UploadState.LoadAll", "Skipping unreadable state file " + Path.GetFileName(path) + " (" + ex.GetType().Name + ").");
            }
            return null;
        }

        /// <summary>
        /// Removes state files older than <paramref name="maxAgeDays"/>. R2 keeps incomplete
        /// multipart uploads until they are aborted or the bucket lifecycle removes them, so
        /// very old local state is almost always stale.
        /// </summary>
        public int CleanupStale(int maxAgeDays = 30)
        {
            int removed = 0;
            lock (_sync)
            {
                try
                {
                    if (!System.IO.Directory.Exists(_directory)) return 0;

                    DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Abs(maxAgeDays));
                    foreach (string path in System.IO.Directory.GetFiles(_directory, "*.json"))
                    {
                        try
                        {
                            if (File.GetLastWriteTimeUtc(path) < cutoff)
                            {
                                File.Delete(path);
                                removed++;
                            }
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    if (_log != null) _log.Error("UploadState.Cleanup", "Could not clean up old resume state.", ex);
                }
            }

            if (removed > 0 && _log != null)
                _log.Info("UploadState.Cleanup", "Removed " + removed + " stale resume state file(s).");

            return removed;
        }

        /// <summary>
        /// Decides whether a stored state can still be used for <paramref name="item"/>.
        /// The local file must exist and its size and last-write time must match exactly;
        /// anything else means the bytes on disk no longer correspond to the uploaded parts.
        /// </summary>
        public static bool CanResume(MultipartUploadState state, UploadQueueItem item, out string reason)
        {
            if (state == null)
            {
                reason = "No saved upload state was found.";
                return false;
            }

            if (!state.IsStructurallyValid())
            {
                reason = "The saved upload state is incomplete.";
                return false;
            }

            if (item == null)
            {
                reason = "No queue item was supplied.";
                return false;
            }

            if (!string.Equals(state.LocalFilePath, item.LocalFilePath, StringComparison.OrdinalIgnoreCase))
            {
                reason = "The saved state refers to a different file.";
                return false;
            }

            if (!string.Equals(state.ObjectKey, item.ObjectKey, StringComparison.Ordinal))
            {
                reason = "The destination object key changed since the upload was interrupted.";
                return false;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(PathUtility.ToExtendedLengthPath(item.LocalFilePath));
                if (!info.Exists)
                {
                    reason = "The local file no longer exists.";
                    return false;
                }
            }
            catch (IOException ex)
            {
                reason = "The local file could not be read: " + ex.Message;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                reason = "Access to the local file was denied.";
                return false;
            }

            if (info.Length != state.FileSize)
            {
                reason = "The local file's size changed from "
                         + FileSizeFormatter.Format(state.FileSize) + " to "
                         + FileSizeFormatter.Format(info.Length) + ".";
                return false;
            }

            if (info.LastWriteTimeUtc != state.LastWriteUtc)
            {
                reason = "The local file was modified after the upload was interrupted.";
                return false;
            }

            if (state.PartSize < MultipartCalculator.MinPartSize)
            {
                reason = "The saved part size is below the 5 MiB minimum.";
                return false;
            }

            reason = null;
            return true;
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool IsRecoverable(Exception ex)
        {
            return ex is IOException
                || ex is UnauthorizedAccessException
                || ex is System.Runtime.Serialization.SerializationException
                || ex is System.Xml.XmlException
                || ex is FormatException
                || ex is ArgumentException;
        }
    }
}
