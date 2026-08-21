using System;
using System.IO;

namespace CloudflareR2Uploader.Services
{
    internal interface IAtomicFileWriter
    {
        void Write(string destinationPath, Action<FileStream> write);
    }

    internal interface IAtomicFileCommitter
    {
        void Commit(string temporaryPath, string destinationPath);
    }

    internal interface IAtomicFileCleanup
    {
        void Delete(string temporaryPath);
    }

    internal sealed class AtomicFileWriter : IAtomicFileWriter
    {
        private readonly IAtomicFileCommitter _committer;
        private readonly IAtomicFileCleanup _cleanup;

        internal static AtomicFileWriter Shared { get; } = new AtomicFileWriter(
            new FileSystemCommitter(), new FileSystemAtomicFileCleanup());

        internal AtomicFileWriter(IAtomicFileCommitter committer)
            : this(committer, new FileSystemAtomicFileCleanup())
        {
        }

        internal AtomicFileWriter(IAtomicFileCommitter committer, IAtomicFileCleanup cleanup)
        {
            ArgumentNullException.ThrowIfNull(committer);
            ArgumentNullException.ThrowIfNull(cleanup);
            _committer = committer;
            _cleanup = cleanup;
        }

        public void Write(string destinationPath, Action<FileStream> write)
        {
            ArgumentException.ThrowIfNullOrEmpty(destinationPath);
            ArgumentNullException.ThrowIfNull(write);

            string directory = Path.GetDirectoryName(destinationPath)!;
            string temporary = Path.Combine(
                directory,
                "." + Path.GetFileName(destinationPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            Exception primaryFailure = null;
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    write(stream);
                    stream.Flush(flushToDisk: true);
                }

                _committer.Commit(temporary, destinationPath);
            }
            catch (Exception ex)
            {
                primaryFailure = ex;
                throw;
            }
            finally
            {
                try
                {
                    _cleanup.Delete(temporary);
                }
                catch when (primaryFailure is not null)
                {
                    // The write/flush/commit failure is the actionable cause. Cleanup is best effort.
                }
            }
        }
    }

    internal sealed class FileSystemCommitter : IAtomicFileCommitter
    {
        public void Commit(string temporaryPath, string destinationPath)
        {
            if (File.Exists(destinationPath))
            {
                File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                return;
            }

            File.Move(temporaryPath, destinationPath);
        }
    }

    internal sealed class FileSystemAtomicFileCleanup : IAtomicFileCleanup
    {
        public void Delete(string temporaryPath) => File.Delete(temporaryPath);
    }
}
