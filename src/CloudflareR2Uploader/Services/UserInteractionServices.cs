using System;
using System.Diagnostics;
using System.Windows.Forms;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    public interface IClipboardService
    {
        void SetText(string text);
    }

    public sealed class WindowsClipboardService : IClipboardService
    {
        public void SetText(string text) { Clipboard.SetText(text ?? string.Empty); }
    }

    public interface IProcessLauncher
    {
        void Open(string target);
    }

    public sealed class WindowsProcessLauncher : IProcessLauncher
    {
        public void Open(string target) { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
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
