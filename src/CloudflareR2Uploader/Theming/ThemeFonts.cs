using System;
using System.Drawing;
using System.Drawing.Text;

namespace CloudflareR2Uploader.Theming
{
    /// <summary>
    /// Resolves fonts from the set installed with Windows only. No font files ship with the
    /// application, so every lookup falls back through a chain that ends at the guaranteed
    /// <see cref="SystemFonts.MessageBoxFont"/>.
    /// </summary>
    public static class ThemeFonts
    {
        private static readonly string FamilyName = ResolveFamily();
        private static readonly string DisplayFamilyName = ResolveDisplayFamily();

        private static readonly object Sync = new object();
        private static Font _body;
        private static Font _bodySmall;
        private static Font _bodyBold;
        private static Font _caption;
        private static Font _captionBold;
        private static Font _title;
        private static Font _sectionHeader;
        private static Font _brandTitle;
        private static Font _eyebrow;
        private static Font _mono;
        private static Font _metric;

        /// <summary>Base point size, taken from the user's message-box font so it honours
        /// "make text bigger" accessibility settings.</summary>
        private static float BaseSize
        {
            get
            {
                Font f = SystemFonts.MessageBoxFont;
                return f == null ? 9f : f.SizeInPoints;
            }
        }

        public static Font Body { get { return Get(ref _body, FamilyName, BaseSize, FontStyle.Regular); } }
        public static Font BodySmall { get { return Get(ref _bodySmall, FamilyName, BaseSize - 0.5f, FontStyle.Regular); } }
        public static Font BodyBold { get { return Get(ref _bodyBold, FamilyName, BaseSize, FontStyle.Bold); } }
        public static Font Caption { get { return Get(ref _caption, FamilyName, BaseSize - 1.0f, FontStyle.Regular); } }
        public static Font CaptionBold { get { return Get(ref _captionBold, FamilyName, BaseSize - 1.0f, FontStyle.Bold); } }
        public static Font SectionHeader { get { return Get(ref _sectionHeader, FamilyName, BaseSize + 0.5f, FontStyle.Bold); } }
        public static Font Title { get { return Get(ref _title, DisplayFamilyName, BaseSize + 5.0f, FontStyle.Bold); } }
        public static Font BrandTitle { get { return Get(ref _brandTitle, DisplayFamilyName, BaseSize + 2.0f, FontStyle.Bold); } }
        public static Font Eyebrow { get { return Get(ref _eyebrow, ResolveMonoFamily(), BaseSize - 1.5f, FontStyle.Bold); } }
        public static Font Metric { get { return Get(ref _metric, DisplayFamilyName, BaseSize + 3.0f, FontStyle.Bold); } }
        public static Font Mono { get { return Get(ref _mono, ResolveMonoFamily(), BaseSize - 0.5f, FontStyle.Regular); } }

        private static Font Get(ref Font slot, string family, float size, FontStyle style)
        {
            lock (Sync)
            {
                if (slot == null)
                {
                    if (size < 6f) size = 6f;
                    slot = Create(family, size, style);
                }
                return slot;
            }
        }

        private static Font Create(string family, float size, FontStyle style)
        {
            try
            {
                Font font = new Font(family, size, style, GraphicsUnit.Point);
                // Font silently substitutes when a family is missing; verify we got what we asked for.
                if (string.Equals(font.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase))
                    return font;
                font.Dispose();
            }
            catch (ArgumentException)
            {
                // Family not installed or does not support the style; fall through.
            }

            try
            {
                return new Font("Segoe UI", size, style, GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
                Font fallback = SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
                return new Font(fallback.FontFamily, size, style, GraphicsUnit.Point);
            }
        }

        private static string ResolveFamily()
        {
            // "Segoe UI Variable Text" ships with Windows 11 and is the modern UI face.
            return FirstInstalled(new[] { "Segoe UI Variable Text", "Segoe UI", "Tahoma" });
        }

        private static string ResolveDisplayFamily()
        {
            // Bahnschrift has the geometric, compact character of Space Grotesk while
            // remaining a standard Windows font. Older systems fall back safely.
            return FirstInstalled(new[] { "Bahnschrift", "Segoe UI Variable Display", "Segoe UI", "Tahoma" });
        }

        private static string ResolveMonoFamily()
        {
            return FirstInstalled(new[] { "Cascadia Mono", "Consolas", "Courier New" });
        }

        private static string FirstInstalled(string[] candidates)
        {
            try
            {
                using (InstalledFontCollection installed = new InstalledFontCollection())
                {
                    FontFamily[] families = installed.Families;
                    foreach (string candidate in candidates)
                    {
                        foreach (FontFamily family in families)
                        {
                            if (string.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase))
                                return candidate;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Enumerating fonts can fail on locked-down systems; use the safe default.
            }
            return "Segoe UI";
        }
    }
}
