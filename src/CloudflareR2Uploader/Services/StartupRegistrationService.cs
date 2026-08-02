using System;
using Microsoft.Win32;

namespace CloudflareR2Uploader.Services
{
    public interface IStartupRegistrationStore
    {
        string Read(string name);
        void Write(string name, string value);
        void Delete(string name);
    }

    internal sealed class RegistryStartupRegistrationStore : IStartupRegistrationStore
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public string Read(string name) { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false)) return key == null ? null : key.GetValue(name) as string; }
        public void Write(string name, string value) { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey)) key.SetValue(name, value, RegistryValueKind.String); }
        public void Delete(string name) { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (key != null) key.DeleteValue(name, false); }
    }

    public interface IStartupRegistrationService
    {
        bool IsEnabled(string executablePath);
        bool SetEnabled(bool enabled, string executablePath, out string error);
        string RegisteredCommand { get; }
    }

    public sealed class StartupRegistrationService : IStartupRegistrationService
    {
        public const string ValueName = "CloudflareR2Uploader";
        private readonly IStartupRegistrationStore _store;
        private readonly ILoggingService _log;
        public StartupRegistrationService(ILoggingService log) : this(log, new RegistryStartupRegistrationStore()) { }
        internal StartupRegistrationService(ILoggingService log, IStartupRegistrationStore store) { _log = log; _store = store; }
        public string RegisteredCommand { get { try { return _store.Read(ValueName); } catch (Exception ex) { if (_log != null) _log.Error("Startup.Read", "The per-user startup registration could not be read.", ex); return null; } } }
        public bool IsEnabled(string executablePath) { return string.Equals(RegisteredCommand, BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase); }
        public bool SetEnabled(bool enabled, string executablePath, out string error)
        {
            error = null;
            try
            {
                if (enabled)
                {
                    if (string.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("The executable path is missing.", "executablePath");
                    string normalized = executablePath.Replace('/', '\\');
                    if (normalized.IndexOf("\\bin\\Debug\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        normalized.IndexOf("testhost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        normalized.IndexOf("vstest", StringComparison.OrdinalIgnoreCase) >= 0)
                        throw new InvalidOperationException("Debug and test executables cannot be registered to start with Windows.");
                    _store.Write(ValueName, BuildCommand(executablePath));
                }
                else _store.Delete(ValueName);
                return true;
            }
            catch (Exception ex)
            {
                error = LoggingService.Sanitize(ex.Message);
                if (_log != null) _log.Error("Startup.Write", "The per-user startup registration could not be updated.", ex);
                return false;
            }
        }
        public static string BuildCommand(string executablePath) { return "\"" + (executablePath ?? string.Empty).Replace("\"", string.Empty) + "\" --background"; }
    }
}
