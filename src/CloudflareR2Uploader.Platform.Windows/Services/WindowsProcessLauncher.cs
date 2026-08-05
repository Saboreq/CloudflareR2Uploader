using System;
using System.Diagnostics;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Opens a target through the Windows shell. <c>UseShellExecute</c> is required so that
    /// https URLs open in the default browser and folders open in Explorer.
    /// </summary>
    public sealed class WindowsProcessLauncher : IProcessLauncher
    {
        public void Open(string target) { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }

        public void Start(string executable, string arguments)
        {
            Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true });
        }
    }
}
