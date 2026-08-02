using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Controls
{
    /// <summary>
    /// The shared Saboreq S/slash mark used by BIO and SabHaven. It stays vector-drawn so
    /// the main window remains sharp at every Windows DPI without shipping a loose image.
    /// </summary>
    [ToolboxItem(true)]
    public sealed class BrandMarkControl : Control
    {
        public BrandMarkControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            Size = new Size(44, 44);
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = "Saboreq";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            if (bounds.Width <= 4 || bounds.Height <= 4) return;

            Graphics g = e.Graphics;
            g.UseHighQuality();

            using (GraphicsPath tile = GraphicsExtensions.CreateRoundedRectangle(bounds, 11))
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(
                    bounds,
                    Theme.Blend(Theme.ElevatedBackground, Theme.Accent, 0.34),
                    Theme.Blend(Theme.ElevatedBackground, Theme.CyanSoft, 0.34),
                    LinearGradientMode.ForwardDiagonal))
                {
                    g.FillPath(fill, tile);
                }

                using (Pen border = new Pen(Theme.Blend(Theme.BorderStrong, Theme.AccentHover, 0.58), 1f))
                {
                    g.DrawPath(border, tile);
                }
            }

            using (LinearGradientBrush edge = new LinearGradientBrush(
                new Rectangle(bounds.X + 6, bounds.Y, bounds.Width - 12, 1),
                Color.Transparent,
                Theme.WithAlpha(Theme.AccentBright, 190),
                LinearGradientMode.Horizontal))
            {
                ColorBlend blend = new ColorBlend(3);
                blend.Colors = new[]
                {
                    Color.Transparent,
                    Theme.WithAlpha(Theme.AccentBright, 190),
                    Color.Transparent
                };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                edge.InterpolationColors = blend;
                g.FillRectangle(edge, bounds.X + 6, bounds.Y, bounds.Width - 12, 1);
            }

            BrandMarkRenderer.DrawGlyph(g, Rectangle.Inflate(bounds, -7, -7));
        }
    }

    internal static class BrandMarkRenderer
    {
        public static void DrawGlyph(Graphics graphics, Rectangle bounds)
        {
            if (graphics == null || bounds.Width <= 0 || bounds.Height <= 0) return;

            GraphicsState state = graphics.Save();
            try
            {
                float side = System.Math.Min(bounds.Width, bounds.Height);
                float scale = side / 32f;
                graphics.TranslateTransform(
                    bounds.X + (bounds.Width - side) / 2f,
                    bounds.Y + (bounds.Height - side) / 2f);
                graphics.ScaleTransform(scale, scale);

                using (GraphicsPath s = CreateSPath())
                using (SolidBrush white = new SolidBrush(Theme.TextPrimary))
                using (Pen slash = new Pen(Theme.AccentHover, 1.8f))
                {
                    graphics.FillPath(white, s);
                    slash.StartCap = LineCap.Round;
                    slash.EndCap = LineCap.Round;
                    graphics.DrawLine(slash, 21f, 8f, 17f, 24f);
                }
            }
            finally
            {
                graphics.Restore(state);
            }
        }

        private static GraphicsPath CreateSPath()
        {
            GraphicsPath path = new GraphicsPath();
            path.StartFigure();
            path.AddBezier(12f, 10.5f, 12f, 9.3f, 13f, 8.5f, 14.6f, 8.5f);
            path.AddBezier(14.6f, 8.5f, 16.3f, 8.5f, 17.3f, 9.4f, 17.5f, 10.7f);
            path.AddLine(17.5f, 10.7f, 19.8f, 10.7f);
            path.AddBezier(19.8f, 10.7f, 19.6f, 8.3f, 17.8f, 6.7f, 14.6f, 6.7f);
            path.AddBezier(14.6f, 6.7f, 11.4f, 6.7f, 9.6f, 8.4f, 9.6f, 10.8f);
            path.AddBezier(9.6f, 10.8f, 9.6f, 15.4f, 17.2f, 13.8f, 17.2f, 16.8f);
            path.AddBezier(17.2f, 16.8f, 17.2f, 18.1f, 16.1f, 18.9f, 14.3f, 18.9f);
            path.AddBezier(14.3f, 18.9f, 12.4f, 18.9f, 11.3f, 17.9f, 11.2f, 16.5f);
            path.AddLine(11.2f, 16.5f, 8.8f, 16.5f);
            path.AddBezier(8.8f, 16.5f, 8.9f, 19.1f, 11f, 20.7f, 14.3f, 20.7f);
            path.AddBezier(14.3f, 20.7f, 17.7f, 20.7f, 19.7f, 19f, 19.7f, 16.5f);
            path.AddBezier(19.7f, 16.5f, 19.7f, 11.8f, 12f, 13.4f, 12f, 10.5f);
            path.CloseFigure();
            return path;
        }
    }
}
