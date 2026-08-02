using System.Drawing;

namespace CloudflareR2Uploader.Theming
{
    /// <summary>
    /// Central palette for the dark black-and-purple interface. Every colour used by the
    /// application comes from here so the look stays consistent and is trivial to adjust.
    /// </summary>
    public static class Theme
    {
        // Saboreq / SabHaven brand surfaces, darkest first.
        public static readonly Color WindowBackground = Color.FromArgb(0x05, 0x05, 0x07);
        // Use the RGB overload. The single-int overload treats 0x101017 as ARGB
        // (alpha 0), which makes this color transparent and causes plain WinForms
        // controls such as upload queue rows to throw when it is assigned.
        public static readonly Color SurfaceBackground = Color.FromArgb(0x08, 0x08, 0x0B);
        public static readonly Color ElevatedBackground = Color.FromArgb(0x0B, 0x0B, 0x10);
        public static readonly Color CardBackground = Color.FromArgb(0x0E, 0x0E, 0x14);
        public static readonly Color CardBackgroundAlt = Color.FromArgb(0x12, 0x12, 0x1A);
        public static readonly Color CardHover = Color.FromArgb(0x16, 0x16, 0x20);
        public static readonly Color InputBackground = Color.FromArgb(0x0B, 0x0B, 0x10);

        // Opaque equivalents of the low-alpha borders used by the web properties.
        public static readonly Color Border = Color.FromArgb(0x22, 0x22, 0x28);
        public static readonly Color BorderStrong = Color.FromArgb(0x31, 0x31, 0x39);
        public static readonly Color Divider = Color.FromArgb(0x1B, 0x1B, 0x21);

        // Text.
        public static readonly Color TextPrimary = Color.FromArgb(0xF7, 0xF7, 0xF8);
        public static readonly Color TextSecondary = Color.FromArgb(0xB0, 0xB0, 0xB4);
        public static readonly Color TextMuted = Color.FromArgb(0x78, 0x78, 0x80);
        public static readonly Color TextOnAccent = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color TextDisabled = Color.FromArgb(0x4A, 0x4A, 0x51);

        // Accent (violet).
        public static readonly Color Accent = Color.FromArgb(0x8B, 0x5C, 0xF6);
        public static readonly Color AccentHover = Color.FromArgb(0xA7, 0x8B, 0xFA);
        public static readonly Color AccentBright = Color.FromArgb(0xC4, 0xB5, 0xFD);
        public static readonly Color AccentPressed = Color.FromArgb(0x6D, 0x38, 0xE0);
        public static readonly Color AccentDeep = Color.FromArgb(0x3B, 0x07, 0x64);
        public static readonly Color AccentSoft = Color.FromArgb(0x20, 0x16, 0x35);
        public static readonly Color AccentGlow = Color.FromArgb(0x40, 0x8B, 0x5C, 0xF6);
        public static readonly Color Cyan = Color.FromArgb(0x22, 0xD3, 0xEE);
        public static readonly Color CyanSoft = Color.FromArgb(0x0D, 0x25, 0x2B);

        // Status colours. Every status is also communicated with text, never colour alone.
        public static readonly Color Success = Color.FromArgb(0x69, 0xD6, 0xA3);
        public static readonly Color Warning = Color.FromArgb(0xF0, 0xB9, 0x5A);
        public static readonly Color Error = Color.FromArgb(0xEF, 0x71, 0x85);
        public static readonly Color Paused = Color.FromArgb(0x22, 0xD3, 0xEE);
        public static readonly Color Cancelled = Color.FromArgb(0x87, 0x87, 0x90);
        public static readonly Color Queued = Color.FromArgb(0x70, 0x70, 0x7A);

        /// <summary>Blends <paramref name="from"/> towards <paramref name="to"/>.</summary>
        public static Color Blend(Color from, Color to, double amount)
        {
            if (amount <= 0) return from;
            if (amount >= 1) return to;
            return Color.FromArgb(
                from.A + (int)((to.A - from.A) * amount),
                from.R + (int)((to.R - from.R) * amount),
                from.G + (int)((to.G - from.G) * amount),
                from.B + (int)((to.B - from.B) * amount));
        }

        public static Color WithAlpha(Color color, int alpha)
        {
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }
}
