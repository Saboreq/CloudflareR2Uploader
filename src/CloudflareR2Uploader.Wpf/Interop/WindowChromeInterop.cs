using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CloudflareR2Uploader.Wpf.Interop
{
    /// <summary>
    /// Makes the Windows caption follow the application's palette.
    /// <para>
    /// The alternative would be a custom <c>WindowChrome</c> with hand-written hit testing,
    /// which is how the mock's title bar could also be reproduced. That is not worth it here:
    /// a real caption keeps minimize, maximize, restore, the system menu, double-click to
    /// maximize, drag, Aero Snap, snap layouts and the accessibility affordances working for
    /// free, and DWM already paints it dark. Only the colour needs changing.
    /// </para>
    /// </summary>
    internal static class WindowChromeInterop
    {
        /// <summary>Documented on Windows 10 20H1 and later.</summary>
        private const int DwmwaUseImmersiveDarkMode = 20;

        /// <summary>The attribute id used by Windows 10 builds 18985 to 19041.</summary>
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>
        /// Best effort: a failure here is purely cosmetic, so it is swallowed rather than
        /// surfaced. Older Windows 10 builds simply keep the light caption.
        /// </summary>
        public static void ApplyDarkTitleBar(Window window, bool isDark)
        {
            if (window is null) return;

            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            int value = isDark ? 1 : 0;

            try
            {
                if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                {
                    _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
                }
            }
            catch (DllNotFoundException)
            {
                // dwmapi.dll is present on every supported Windows version; this is defensive.
            }
            catch (EntryPointNotFoundException)
            {
            }
        }
    }
}
