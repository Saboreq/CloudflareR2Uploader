using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>
    /// A read-only view over a byte range of a file, backed by its own <see cref="FileStream"/>.
    /// Each concurrently uploaded part gets one of these, so several parts of a multi-gigabyte
    /// file can be sent at once without ever holding the file in memory and without two
    /// threads sharing one stream position.
    /// </summary>
    public sealed class BoundedFileStream : Stream
    {
        private readonly FileStream _inner;
        private readonly long _offset;
        private readonly long _length;
        private long _position;
        private bool _disposed;

        /// <summary>
        /// Opens a window of <paramref name="length"/> bytes starting at
        /// <paramref name="offset"/>. The file is opened with <see cref="FileShare.Read"/> so
        /// other readers (including the app's other parts) are not blocked.
        /// </summary>
        public BoundedFileStream(string path, long offset, long length, int bufferSize = 81920)
        {
            if (path == null) throw new ArgumentNullException("path");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset");
            if (length < 0) throw new ArgumentOutOfRangeException("length");

            _inner = new FileStream(
                PathUtility.ToExtendedLengthPath(path),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (offset + length > _inner.Length)
            {
                _inner.Dispose();
                throw new IOException(
                    "The file is smaller than expected; it appears to have changed since the upload was prepared.");
            }

            _offset = offset;
            _length = length;
            _inner.Position = offset;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _length; } }

        public override long Position
        {
            get { return _position; }
            set
            {
                if (value < 0 || value > _length) throw new ArgumentOutOfRangeException("value");
                _position = value;
                _inner.Position = _offset + value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int allowed = ClampCount(count);
            if (allowed <= 0) return 0;

            int read = _inner.Read(buffer, offset, allowed);
            _position += read;
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int allowed = ClampCount(count);
            if (allowed <= 0) return 0;

            int read = await _inner.ReadAsync(buffer, offset, allowed, cancellationToken).ConfigureAwait(false);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target;
            switch (origin)
            {
                case SeekOrigin.Begin: target = offset; break;
                case SeekOrigin.Current: target = _position + offset; break;
                case SeekOrigin.End: target = _length + offset; break;
                default: throw new ArgumentOutOfRangeException("origin");
            }

            if (target < 0 || target > _length)
                throw new IOException("Attempted to seek outside the bounds of the part.");

            Position = target;
            return _position;
        }

        public override void Flush() { }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("BoundedFileStream is read-only.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("BoundedFileStream is read-only.");
        }

        private int ClampCount(int count)
        {
            long remaining = _length - _position;
            if (remaining <= 0) return 0;
            return count > remaining ? (int)remaining : count;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing) _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
