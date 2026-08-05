#nullable enable

using System;
using System.Threading;
using Microsoft.Win32;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Reads the per-user Windows app-mode preference and watches it for changes.
    /// <para>
    /// The value lives at
    /// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme</c>.
    /// There is no public managed API for it and no registry change notification that the
    /// BCL exposes, so the key is polled. Polling every two seconds costs one cheap registry
    /// read and is imperceptible next to how rarely a person changes their theme; a
    /// <c>RegNotifyChangeKeyValue</c> P/Invoke would add an interop surface and a dedicated
    /// wait thread for the same result.
    /// </para>
    /// </summary>
    public sealed class WindowsThemeProvider : ISystemThemeProvider
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightThemeValue = "AppsUseLightTheme";

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        private readonly Timer _timer;
        private int _isDark;
        private bool _disposed;

        public WindowsThemeProvider()
        {
            _isDark = ReadIsDark() ? 1 : 0;
            _timer = new Timer(OnTick, null, PollInterval, PollInterval);
        }

        public bool IsDark { get { return Volatile.Read(ref _isDark) != 0; } }

        public event EventHandler? Changed;

        private void OnTick(object? state)
        {
            if (_disposed) return;

            int current = ReadIsDark() ? 1 : 0;
            if (Interlocked.Exchange(ref _isDark, current) == current) return;

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Defaults to dark when the value is missing or unreadable, because dark is this
        /// application's own default and a failed read must not flip the user to light.
        /// </summary>
        private static bool ReadIsDark()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false))
                {
                    if (key?.GetValue(AppsUseLightThemeValue) is int useLight) return useLight == 0;
                }
            }
            catch (Exception ex) when (
                ex is System.Security.SecurityException ||
                ex is UnauthorizedAccessException ||
                ex is System.IO.IOException)
            {
                // Fall through to the default.
            }

            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
        }
    }
}
