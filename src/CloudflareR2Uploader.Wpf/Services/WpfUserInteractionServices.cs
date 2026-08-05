using System;
using System.Windows;
using CloudflareR2Uploader.Services;

namespace CloudflareR2Uploader.Wpf.Services
{
    /// <summary>
    /// WPF clipboard implementation of the shared <see cref="IClipboardService"/>.
    /// <para>
    /// <c>Clipboard.SetText</c> fails intermittently when another process holds the clipboard
    /// open, which is common with remote-desktop and password-manager clients. A failure is
    /// swallowed rather than surfaced as a crash: the user can always try the copy action
    /// again. Retrying synchronously would freeze the dispatcher and is intentionally avoided.
    /// </para>
    /// </summary>
    public sealed class WpfClipboardService : IClipboardService
    {
        public void SetText(string text)
        {
            try
            {
                Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text ?? string.Empty),
                    copy: true);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Another process kept the clipboard locked for the whole retry window.
            }
        }
    }
}
