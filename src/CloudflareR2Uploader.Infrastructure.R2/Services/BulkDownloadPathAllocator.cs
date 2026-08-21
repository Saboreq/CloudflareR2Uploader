using System;
using System.Collections.Generic;
using System.IO;

namespace CloudflareR2Uploader.Services
{
    internal sealed class BulkDownloadPathAllocator
    {
        private const string PartialSuffix = ".r2partial";
        private const int MaxRenameAttempts = 999;

        private readonly object _gate = new object();
        private readonly Dictionary<string, int> _owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<int> _claimedOriginalOwners = new HashSet<int>();
        private readonly Func<string, bool> _exists;
        private int _nextSyntheticOwner = -1;

        public BulkDownloadPathAllocator(IEnumerable<string> plannedFinalPaths, Func<string, bool> exists)
        {
            ArgumentNullException.ThrowIfNull(plannedFinalPaths);
            ArgumentNullException.ThrowIfNull(exists);

            _exists = exists;
            int owner = 0;
            foreach (string path in plannedFinalPaths)
            {
                if (!string.IsNullOrEmpty(path) && IsReservationPairAvailable(path))
                    ReservePair(owner, path);
                owner++;
            }
        }

        public string AllocateRename(string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);

            lock (_gate)
            {
                return AllocateRenameLocked(_nextSyntheticOwner--, path);
            }
        }

        public bool TryClaimOriginal(int owner, string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);

            lock (_gate)
            {
                int finalOwner;
                int partialOwner;
                return _owners.TryGetValue(path, out finalOwner) && finalOwner == owner &&
                    _owners.TryGetValue(path + PartialSuffix, out partialOwner) && partialOwner == owner &&
                    _claimedOriginalOwners.Add(owner);
            }
        }

        public string AllocateRename(int owner, string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);

            lock (_gate)
            {
                return AllocateRenameLocked(owner, path);
            }
        }

        private string AllocateRenameLocked(int owner, string path)
        {
            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            for (int index = 1; index <= MaxRenameAttempts; index++)
            {
                string candidate = Path.Combine(directory, name + " (" + index + ")" + extension);
                if (IsUnavailable(candidate)) continue;
                ReservePair(owner, candidate);
                return candidate;
            }

            throw new InvalidOperationException(
                "No collision-free local download path was found after " + MaxRenameAttempts + " attempts.");
        }

        private bool IsReservationPairAvailable(string path)
        {
            return !_owners.ContainsKey(path) && !_owners.ContainsKey(path + PartialSuffix);
        }

        private bool IsUnavailable(string path)
        {
            string partial = path + PartialSuffix;
            return _owners.ContainsKey(path) || _owners.ContainsKey(partial) ||
                _exists(path) || _exists(partial);
        }

        private void ReservePair(int owner, string path)
        {
            _owners.Add(path, owner);
            _owners.Add(path + PartialSuffix, owner);
        }
    }
}
