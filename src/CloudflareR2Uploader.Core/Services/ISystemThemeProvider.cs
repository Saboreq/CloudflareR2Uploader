#nullable enable

using System;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Reports the operating system's light/dark preference so the "System" theme option can
    /// follow it, including while the application is running.
    /// </summary>
    public interface ISystemThemeProvider : IDisposable
    {
        /// <summary>True when Windows is currently set to a dark app mode.</summary>
        bool IsDark { get; }

        /// <summary>Raised when the OS preference changes. May fire on any thread.</summary>
        event EventHandler? Changed;
    }
}
