using System;
using System.IO;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>
    /// Every file the application writes lives under %LocalAppData%\CloudflareR2Uploader.
    /// Nothing is ever written next to the executable, so the EXE can run from a read-only
    /// or shared location.
    /// </summary>
    public static class AppPaths
    {
        public const string AppFolderName = "CloudflareR2Uploader";

        private static string _rootOverride;

        /// <summary>
        /// Redirects all application data to a different root. Used by the automated tests so
        /// they never touch the real user profile.
        /// </summary>
        internal static void OverrideRootForTesting(string root)
        {
            _rootOverride = root;
        }

        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(_rootOverride)) return _rootOverride;
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(localAppData, AppFolderName);
            }
        }

        public static string SettingsFilePath { get { return Path.Combine(Root, "settings.json"); } }

        /// <summary>DPAPI-protected credential blob. Never JSON, never logged.</summary>
        public static string CredentialFilePath { get { return Path.Combine(Root, "credentials.bin"); } }

        public static string UploadStateDirectory { get { return Path.Combine(Root, "UploadState"); } }

        public static string LogDirectory { get { return Path.Combine(Root, "Logs"); } }

        public static string PreviewCacheDirectory { get { return Path.Combine(Root, "PreviewCache"); } }

        public static string WebView2DataDirectory { get { return Path.Combine(Root, "WebView2"); } }

        public static string UpdateDirectory { get { return Path.Combine(Root, "Updates"); } }

        public static void EnsureDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        public static void EnsureAllDirectories()
        {
            EnsureDirectory(Root);
            EnsureDirectory(UploadStateDirectory);
            EnsureDirectory(LogDirectory);
            EnsureDirectory(PreviewCacheDirectory);
            EnsureDirectory(WebView2DataDirectory);
            EnsureDirectory(UpdateDirectory);
        }
    }
}
