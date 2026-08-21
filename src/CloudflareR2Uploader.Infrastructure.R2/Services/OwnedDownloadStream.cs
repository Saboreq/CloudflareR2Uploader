using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    internal static class OwnedDownloadStream
    {
        private const int BufferSize = 81920;
        private const int LinuxOpenReadWrite = 0x0002;
        private const int LinuxOpenCloseOnExec = 0x80000;
        private const int LinuxOpenTemporaryFile = 0x410000;
        private const uint OwnerReadWriteMode = 0x180;

        public static FileStream Create(string partialPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(partialPath);
            string extendedPath = PathUtility.ToExtendedLengthPath(partialPath);
            if (File.Exists(extendedPath))
                throw new IOException("The partial download path already exists.");

            if (!OperatingSystem.IsLinux())
            {
                return new FileStream(
                    extendedPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete,
                    BufferSize, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            }

            return CreateAnonymousLinux(
                Path.GetDirectoryName(extendedPath) ?? string.Empty,
                OwnerReadWriteMode);
        }

        internal static FileStream CreateAnonymousLinux(string directory, uint createMode)
        {
            byte[] directoryBytes = Encoding.UTF8.GetBytes(directory + '\0');
            int descriptor = Open(
                directoryBytes,
                LinuxOpenReadWrite | LinuxOpenCloseOnExec | LinuxOpenTemporaryFile,
                createMode);
            if (descriptor < 0)
                throw new IOException(
                    "An anonymous local download file could not be created. Native error " +
                    Marshal.GetLastPInvokeError() + ".");

            SafeFileHandle handle = new SafeFileHandle((IntPtr)descriptor, true);
            try
            {
                return new FileStream(handle, FileAccess.ReadWrite, BufferSize, false);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        [DllImport("libc", EntryPoint = "open", ExactSpelling = true, SetLastError = true)]
        private static extern int Open([In] byte[] path, int flags, uint mode);
    }
}
