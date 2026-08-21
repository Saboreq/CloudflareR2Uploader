using System;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Copies text to the system clipboard. The platform boundary keeps WPF-specific APIs
    /// out of Core and ensures tests never touch the real clipboard.
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
        private readonly Func<string, string, string> _urlBuilder = ObjectKeyUtility.BuildPublicUrl;

        public string Build(string publicBaseUrl, R2BrowserItem item)
        {
            if (item == null || item.IsFolder || string.IsNullOrWhiteSpace(publicBaseUrl)) return null;
            return _urlBuilder(publicBaseUrl, item.Key);
        }
    }
}
