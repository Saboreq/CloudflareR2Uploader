using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace CloudflareR2Uploader.Tests
{
    internal sealed class TemporaryDirectory : IDisposable
    {
        private static readonly string TestRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CloudflareR2Uploader.Tests"));

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(TestRoot);
            Path = System.IO.Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; private set; }

        public string File(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            string resolved = System.IO.Path.GetFullPath(Path);
            string rootWithSeparator = TestRoot.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;

            if (!resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a directory outside the test root.");

            if (Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }

    internal sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture;
        private readonly CultureInfo _uiCulture;

        public CultureScope(CultureInfo culture)
        {
            _culture = Thread.CurrentThread.CurrentCulture;
            _uiCulture = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }

        public void Dispose()
        {
            Thread.CurrentThread.CurrentCulture = _culture;
            Thread.CurrentThread.CurrentUICulture = _uiCulture;
        }
    }
}
