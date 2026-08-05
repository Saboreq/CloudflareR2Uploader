using System;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Copies text to the system clipboard. Abstracted because WinForms and WPF have
    /// separate clipboard APIs and because tests must never touch the real clipboard.
    /// </summary>
    public interface IClipboardService
    {
        void SetText(string text);
    }

    /// <summary>Opens a file, folder or URL with the shell's default handler.</summary>
    public interface IProcessLauncher
    {
        void Open(string target);

        /// <summary>Starts a known executable with explicit command-line arguments.</summary>
        void Start(string executable, string arguments);
    }

    public sealed class PublicUrlService
    {
        public string Build(string publicBaseUrl, R2BrowserItem item)
        {
            if (item == null || item.IsFolder || string.IsNullOrWhiteSpace(publicBaseUrl)) return null;
            return ObjectKeyUtility.BuildPublicUrl(publicBaseUrl, item.Key);
        }
    }
}
