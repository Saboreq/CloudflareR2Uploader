using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    internal interface IDownloadCommit : IDisposable
    {
        FileStream Stream { get; }
        Exception CleanupFailure { get; }
        bool TryPublish(string target, bool overwrite);
    }

    internal interface IDownloadCommitNativeOperations
    {
        bool TryPublish(SafeFileHandle handle, string target, bool overwrite, out int nativeError);
        bool TryAbandon(SafeFileHandle handle, out int nativeError);
    }

    internal sealed class OwnedDownloadCommit : IDownloadCommit
    {
        private const int BufferSize = 81920;
        private const int MaxCreateAttempts = 999;
        private const int LinuxAtCurrentWorkingDirectory = -100;
        private const int LinuxAtEmptyPath = 0x1000;
        private const int LinuxFileExists = 17;
        private const uint LinuxDefaultCreateMode = 0x1B6;
        private const uint WindowsDeleteAccess = 0x00010000;
        private const uint WindowsGenericRead = 0x80000000;
        private const uint WindowsGenericWrite = 0x40000000;
        private const uint WindowsShareDelete = 0x00000004;
        private const uint WindowsCreateNew = 1;
        private const uint WindowsFileAttributeNormal = 0x00000080;
        private const uint WindowsFileFlagOverlapped = 0x40000000;
        private const int WindowsFileExists = 80;
        private const int WindowsAlreadyExists = 183;
        private const int WindowsFileRenameInfo = 3;
        private const int WindowsFileDispositionInfo = 4;

        private readonly FileStream _stream;
        private readonly IDownloadCommitNativeOperations _nativeOperations;
        private readonly Action<FileStream> _streamDisposer;
        private bool _published;
        private int _disposeStarted;

        internal OwnedDownloadCommit(FileStream stream, IDownloadCommitNativeOperations nativeOperations)
            : this(stream, nativeOperations, DisposeStream)
        {
        }

        internal OwnedDownloadCommit(
            FileStream stream,
            IDownloadCommitNativeOperations nativeOperations,
            Action<FileStream> streamDisposer)
        {
            _stream = stream;
            _nativeOperations = nativeOperations;
            _streamDisposer = streamDisposer ?? DisposeStream;
        }

        public FileStream Stream => _stream;
        public Exception CleanupFailure { get; private set; }

        internal static uint WindowsCommitCreateFlags =>
            WindowsFileAttributeNormal | WindowsFileFlagOverlapped;

        internal static string CreateWindowsCommitFileName()
        {
            string token = Guid.NewGuid().ToString("N");
            return string.Concat(
                ".r2c-".AsSpan(), token.AsSpan(0, 16), ".tmp".AsSpan());
        }

        internal static string CreateWindowsCommitPath(string directory)
        {
            return PathUtility.ToExtendedLengthPath(
                Path.Combine(directory, CreateWindowsCommitFileName()));
        }

        internal static string ToWindowsNativePublishPath(string target)
        {
            if (string.IsNullOrEmpty(target) || target.Contains('\0'))
                throw new ArgumentException("A Windows publication target must be an absolute DOS or UNC path.", nameof(target));

            if (target.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                if (IsExtendedDosPath(target) || IsExtendedUncPath(target)) return target;
                throw new ArgumentException("The extended Windows publication target is invalid.", nameof(target));
            }

            string normalized = target.Replace('/', '\\');
            if (normalized.StartsWith(@"\\.\", StringComparison.Ordinal))
                throw new ArgumentException("Windows device paths are not publication targets.", nameof(target));
            if (IsDosPath(normalized, 0)) return @"\\?\" + normalized;
            if (IsUncPath(normalized, 2))
                return string.Concat(@"\\?\UNC\", normalized.AsSpan(2));

            throw new ArgumentException("A Windows publication target must be an absolute DOS or UNC path.", nameof(target));
        }

        internal static byte[] CreateWindowsRenameInformationBuffer(
            string nativeTarget,
            bool overwrite)
        {
            byte[] targetBytes = Encoding.Unicode.GetBytes(nativeTarget);
            int fileNameOffset = Marshal.OffsetOf<FileRenameInformation>(
                nameof(FileRenameInformation.FileName)).ToInt32();
            int bufferSize = checked(
                Marshal.SizeOf<FileRenameInformation>() + targetBytes.Length);
            byte[] buffer = new byte[bufferSize];
            buffer[Marshal.OffsetOf<FileRenameInformation>(
                nameof(FileRenameInformation.ReplaceIfExists)).ToInt32()] =
                overwrite ? (byte)1 : (byte)0;
            BitConverter.GetBytes(targetBytes.Length).CopyTo(
                buffer,
                Marshal.OffsetOf<FileRenameInformation>(
                    nameof(FileRenameInformation.FileNameLength)).ToInt32());
            targetBytes.CopyTo(buffer, fileNameOffset);
            return buffer;
        }

        public static OwnedDownloadCommit Create(string target)
        {
            return Create(target, null);
        }

        internal static OwnedDownloadCommit Create(
            string target,
            Action<string> linuxOverwriteLinkBoundary)
        {
            return Create(target, linuxOverwriteLinkBoundary, null);
        }

        internal static OwnedDownloadCommit Create(
            string target,
            Action<string> linuxOverwriteLinkBoundary,
            Action<FileStream> streamDisposer)
        {
            _ = linuxOverwriteLinkBoundary;
            string extendedTarget = PathUtility.ToExtendedLengthPath(target);
            string directory = Path.GetDirectoryName(extendedTarget) ?? string.Empty;
            if (OperatingSystem.IsLinux())
                return new OwnedDownloadCommit(
                    OwnedDownloadStream.CreateAnonymousLinux(directory, LinuxDefaultCreateMode),
                    null,
                    streamDisposer);
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Atomic download commits are supported on Windows and Linux.");

            for (int attempt = 0; attempt < MaxCreateAttempts; attempt++)
            {
                string commitPath = CreateWindowsCommitPath(directory);
                SafeFileHandle handle = CreateFile(
                    commitPath,
                    WindowsGenericRead | WindowsGenericWrite | WindowsDeleteAccess,
                    WindowsShareDelete,
                    IntPtr.Zero,
                    WindowsCreateNew,
                    WindowsCommitCreateFlags,
                    IntPtr.Zero);
                if (!handle.IsInvalid)
                {
                    try
                    {
                        return new OwnedDownloadCommit(
                            new FileStream(handle, FileAccess.ReadWrite, BufferSize, true),
                            WindowsNativeOperations.Instance,
                            streamDisposer);
                    }
                    catch
                    {
                        handle.Dispose();
                        throw;
                    }
                }

                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                if (error != WindowsFileExists && error != WindowsAlreadyExists)
                    throw new IOException("A local download commit could not be created.", new Win32Exception(error));
            }

            throw new IOException("No unique local download commit path was available.");
        }

        public bool TryPublish(string target, bool overwrite)
        {
            if (_published) throw new InvalidOperationException("The local download commit was already published.");
            string extendedTarget = PathUtility.ToExtendedLengthPath(target);
            if (OperatingSystem.IsLinux()) return TryPublishLinux(extendedTarget, overwrite);
            return TryPublishWindows(extendedTarget, overwrite);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;

            try
            {
                if (!_published && _nativeOperations != null)
                {
                    if (!_nativeOperations.TryAbandon(_stream.SafeFileHandle, out int nativeError))
                    {
                        RetainCleanupFailure(new IOException(
                            "The abandoned download commit could not be removed. Native error " +
                            nativeError + "."));
                    }
                }
            }
            catch (Exception ex)
            {
                RetainCleanupFailure(ex);
            }

            try
            {
                _streamDisposer(_stream);
            }
            catch (Exception ex)
            {
                RetainCleanupFailure(ex);
            }
        }

        private void RetainCleanupFailure(Exception failure)
        {
            CleanupFailure = CleanupFailure == null
                ? failure
                : new AggregateException(CleanupFailure, failure);
        }

        private static void DisposeStream(FileStream stream)
        {
            stream.Dispose();
        }

        private bool TryPublishLinux(string target, bool overwrite)
        {
            if (!overwrite)
            {
                int result = LinkAt(
                    _stream.SafeFileHandle.DangerousGetHandle().ToInt32(),
                    new byte[] { 0 },
                    LinuxAtCurrentWorkingDirectory,
                    Utf8Path(target),
                    LinuxAtEmptyPath);
                if (result == 0)
                {
                    _published = true;
                    return true;
                }

                int error = Marshal.GetLastPInvokeError();
                if (error == LinuxFileExists) return false;
                throw new IOException("The completed download could not be published. Native error " + error + ".");
            }

            throw new IOException(
                "Atomic overwrite is unavailable on this platform; the existing local file was preserved.");
        }

        private bool TryPublishWindows(string target, bool overwrite)
        {
            if (_nativeOperations.TryPublish(
                _stream.SafeFileHandle, target, overwrite, out int nativeError))
            {
                _published = true;
                return true;
            }
            if (!overwrite && (nativeError == WindowsFileExists || nativeError == WindowsAlreadyExists))
                return false;
            throw new IOException(
                "The completed download could not be published. Native error " + nativeError + ".");
        }

        private sealed class WindowsNativeOperations : IDownloadCommitNativeOperations
        {
            public static readonly WindowsNativeOperations Instance = new WindowsNativeOperations();

            private WindowsNativeOperations() { }

            public bool TryPublish(
                SafeFileHandle handle,
                string target,
                bool overwrite,
                out int nativeError)
            {
                string nativeTarget = ToWindowsNativePublishPath(target);
                byte[] information = CreateWindowsRenameInformationBuffer(nativeTarget, overwrite);
                IntPtr buffer = Marshal.AllocHGlobal(information.Length);
                try
                {
                    Marshal.Copy(information, 0, buffer, information.Length);

                    if (SetFileInformationByHandle(
                        handle,
                        WindowsFileRenameInfo,
                        buffer,
                        (uint)information.Length))
                    {
                        nativeError = 0;
                        return true;
                    }

                    int error = Marshal.GetLastPInvokeError();
                    nativeError = error;
                    return false;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            public bool TryAbandon(SafeFileHandle handle, out int nativeError)
            {
                FileDispositionInformation disposition = new FileDispositionInformation { DeleteFile = true };
                bool removed = SetFileInformationByHandle(
                    handle,
                    WindowsFileDispositionInfo,
                    ref disposition,
                    (uint)Marshal.SizeOf<FileDispositionInformation>());
                nativeError = removed ? 0 : Marshal.GetLastPInvokeError();
                return removed;
            }
        }

        private static bool IsExtendedDosPath(string path)
        {
            return IsDosPath(path, 4);
        }

        private static bool IsExtendedUncPath(string path)
        {
            return path.Length > 8 &&
                path.AsSpan(4, 4).Equals("UNC\\".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
                IsUncPath(path, 8);
        }

        private static bool IsDosPath(string path, int offset)
        {
            return path.Length > offset + 3 &&
                char.IsAsciiLetter(path[offset]) &&
                path[offset + 1] == ':' &&
                path[offset + 2] == '\\' &&
                path[offset + 3] != '\\';
        }

        private static bool IsUncPath(string path, int offset)
        {
            int serverEnd = path.IndexOf('\\', offset);
            if (serverEnd <= offset) return false;
            int shareEnd = path.IndexOf('\\', serverEnd + 1);
            return shareEnd > serverEnd + 1 &&
                shareEnd + 1 < path.Length &&
                path[shareEnd + 1] != '\\';
        }

        private static byte[] Utf8Path(string path)
        {
            return Encoding.UTF8.GetBytes(path + '\0');
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileRenameInformation
        {
            public byte ReplaceIfExists;
            public IntPtr RootDirectory;
            public uint FileNameLength;
            public char FileName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileDispositionInformation
        {
            [MarshalAs(UnmanagedType.U1)]
            public bool DeleteFile;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle file,
            int fileInformationClass,
            IntPtr fileInformation,
            uint bufferSize);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle file,
            int fileInformationClass,
            ref FileDispositionInformation fileInformation,
            uint bufferSize);

        [DllImport("libc", EntryPoint = "linkat", ExactSpelling = true, SetLastError = true)]
        private static extern int LinkAt(int oldDirectory, [In] byte[] oldPath, int newDirectory, [In] byte[] newPath, int flags);

    }
}
