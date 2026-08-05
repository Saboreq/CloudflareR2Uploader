using System;
using System.Windows.Forms;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// WinForms clipboard implementation. The WPF front end supplies its own; the shared
    /// <see cref="IClipboardService"/> abstraction lives in CloudflareR2Uploader.Core.
    /// </summary>
    public sealed class WindowsClipboardService : IClipboardService
    {
        public void SetText(string text) { Clipboard.SetText(text ?? string.Empty); }
    }
}
