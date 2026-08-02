using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CloudflareR2Uploader.Theming
{
    /// <summary>
    /// Asks the desktop window manager to paint a window's native caption dark so the
    /// standard, fully reliable window frame still matches the dark interface.
    /// Silently does nothing on Windows versions without the attribute.
    /// </summary>
    public static class DarkTitleBar
    {
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Apply(Form form)
        {
            if (form == null || !form.IsHandleCreated) return;

            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
                }
            }
            catch (DllNotFoundException)
            {
                // dwmapi.dll is always present on supported systems; ignore if it is not.
            }
            catch (EntryPointNotFoundException)
            {
            }
        }
    }
}
