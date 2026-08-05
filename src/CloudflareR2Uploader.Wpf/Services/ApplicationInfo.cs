using System;
using System.Reflection;

namespace CloudflareR2Uploader.Wpf
{
    /// <summary>
    /// Identity of the running build. Read once from the entry assembly, because the version
    /// is stamped there by the release script and must agree with the update manifest.
    /// </summary>
    public static class ApplicationInfo
    {
        public const string ProductName = "Cloudflare R2 Uploader";

        static ApplicationInfo()
        {
            DisplayVersion = ReadVersion();
        }

        /// <summary>SemVer informational version, e.g. "1.2.3" or "1.2.3-beta.1".</summary>
        public static string DisplayVersion { get; }

        private static string ReadVersion()
        {
            try
            {
                Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(ApplicationInfo).Assembly;

                AssemblyInformationalVersionAttribute? informational =
                    assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

                if (informational is not null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
                {
                    string value = informational.InformationalVersion;

                    // The SDK appends "+<commit>" build metadata; it is noise in a title bar.
                    int metadata = value.IndexOf('+');
                    return metadata > 0 ? value[..metadata] : value;
                }

                Version? version = assembly.GetName().Version;
                return version is null ? "1.0" : version.ToString(3);
            }
            catch (Exception)
            {
                return "1.0";
            }
        }
    }
}
