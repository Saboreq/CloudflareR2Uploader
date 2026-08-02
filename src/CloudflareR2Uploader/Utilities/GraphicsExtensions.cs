using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CloudflareR2Uploader.Utilities
{
    /// <summary>Small drawing helpers shared by the owner-drawn controls.</summary>
    public static class GraphicsExtensions
    {
        /// <summary>Builds a rounded-rectangle path, degrading to a plain rectangle at radius 0.</summary>
        public static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int maxRadius = Math.Min(bounds.Width, bounds.Height) / 2;
            if (radius > maxRadius) radius = maxRadius;
            if (radius <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            int diameter = radius * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (GraphicsPath path = CreateRoundedRectangle(bounds, radius))
            {
                graphics.FillPath(brush, path);
            }
        }

        public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, Rectangle bounds, int radius)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (GraphicsPath path = CreateRoundedRectangle(bounds, radius))
            {
                graphics.DrawPath(pen, path);
            }
        }

        /// <summary>
        /// Approximates a drop shadow by stroking progressively fainter rounded rectangles.
        /// WinForms has no real shadow primitive; this is cheap and looks right on dark cards.
        /// </summary>
        public static void DrawSoftShadow(this Graphics graphics, Rectangle bounds, int radius, int depth, Color color)
        {
            if (depth <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;

            for (int i = depth; i >= 1; i--)
            {
                int alpha = (int)(color.A * (1.0 - (double)i / (depth + 1)) * 0.55);
                if (alpha <= 0) continue;

                Rectangle ring = Rectangle.Inflate(bounds, i, i);
                ring.Offset(0, Math.Max(1, i / 2));

                using (Pen pen = new Pen(Color.FromArgb(alpha, color.R, color.G, color.B)))
                {
                    graphics.DrawRoundedRectangle(pen, ring, radius + i);
                }
            }
        }

        /// <summary>Applies the high-quality settings every custom control in this app uses.</summary>
        public static void UseHighQuality(this Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        /// <summary>
        /// Shortens text with an ellipsis so it always fits <paramref name="maxWidth"/>.
        /// Used for long object keys and file names.
        /// </summary>
        public static string EllipsizeToWidth(Graphics graphics, string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0) return string.Empty;

            if (TextWidth(graphics, text, font) <= maxWidth) return text;

            const string Ellipsis = "…";
            int low = 0;
            int high = text.Length;

            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (TextWidth(graphics, text.Substring(0, mid) + Ellipsis, font) <= maxWidth) low = mid;
                else high = mid - 1;
            }

            return low <= 0 ? Ellipsis : text.Substring(0, low) + Ellipsis;
        }

        /// <summary>
        /// Shortens a path-like string from the middle, keeping the start and the file name:
        /// <c>photos/2024/summer/…/beach.jpg</c>.
        /// </summary>
        public static string EllipsizePathToWidth(Graphics graphics, string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0) return string.Empty;
            if (TextWidth(graphics, text, font) <= maxWidth) return text;

            int lastSlash = text.LastIndexOf('/');
            if (lastSlash <= 0) return EllipsizeToWidth(graphics, text, font, maxWidth);

            string tail = text.Substring(lastSlash);
            string head = text.Substring(0, lastSlash);

            int tailWidth = TextWidth(graphics, "…" + tail, font);
            if (tailWidth >= maxWidth) return EllipsizeToWidth(graphics, text, font, maxWidth);

            string trimmedHead = EllipsizeToWidth(graphics, head, font, maxWidth - tailWidth);
            if (trimmedHead.EndsWith("…", StringComparison.Ordinal))
                trimmedHead = trimmedHead.Substring(0, trimmedHead.Length - 1);

            return trimmedHead + "…" + tail;
        }

        /// <summary>Flags used everywhere text is measured or drawn with GDI text rendering.</summary>
        public const TextFormatFlags SingleLineFlags =
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        private static int TextWidth(Graphics graphics, string text, Font font)
        {
            return TextRenderer.MeasureText(
                graphics, text, font, new Size(int.MaxValue, int.MaxValue), SingleLineFlags).Width;
        }
    }
}
