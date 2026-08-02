using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CloudflareR2Uploader.Controls
{
    /// <summary>The icons the interface draws. No image files ship with the application.</summary>
    public enum AppIcon
    {
        None = 0,
        UploadCloud,
        UploadArrow,
        Folder,
        File,
        Gear,
        Check,
        Cross,
        Pause,
        Play,
        Trash,
        Copy,
        Refresh,
        Warning,
        Link,
        Plus,
        Eye,
        EyeOff,
        ChevronDown,
        Info,
        ArrowLeft,
        ArrowUp
    }

    /// <summary>
    /// Draws every icon as vector geometry scaled to the requested box. Drawing them rather
    /// than shipping bitmaps keeps the single-EXE requirement intact and keeps the icons crisp
    /// at 100%, 125% and 150% scaling.
    /// </summary>
    public static class IconPainter
    {
        /// <summary>
        /// Draws <paramref name="icon"/> centred in <paramref name="bounds"/>.
        /// <paramref name="strokeWidth"/> is expressed for a 24x24 box and scaled with it.
        /// </summary>
        public static void Draw(Graphics graphics, AppIcon icon, Rectangle bounds, Color color, float strokeWidth = 2f)
        {
            if (graphics == null || icon == AppIcon.None) return;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            SmoothingMode previousSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            GraphicsState state = graphics.Save();
            try
            {
                // Everything below is authored on a 24x24 grid, then scaled into place.
                int side = Math.Min(bounds.Width, bounds.Height);
                float scale = side / 24f;

                graphics.TranslateTransform(
                    bounds.X + (bounds.Width - side) / 2f,
                    bounds.Y + (bounds.Height - side) / 2f);
                graphics.ScaleTransform(scale, scale);

                using (Pen pen = new Pen(color, strokeWidth))
                using (SolidBrush brush = new SolidBrush(color))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;

                    DrawGeometry(graphics, icon, pen, brush);
                }
            }
            finally
            {
                graphics.Restore(state);
                graphics.SmoothingMode = previousSmoothing;
            }
        }

        private static void DrawGeometry(Graphics g, AppIcon icon, Pen pen, SolidBrush brush)
        {
            switch (icon)
            {
                case AppIcon.UploadCloud:
                    DrawUploadCloud(g, pen);
                    break;

                case AppIcon.UploadArrow:
                    g.DrawLine(pen, 12f, 20f, 12f, 5f);
                    g.DrawLines(pen, new[] { new PointF(5.5f, 11.5f), new PointF(12f, 5f), new PointF(18.5f, 11.5f) });
                    break;

                case AppIcon.Folder:
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddLines(new[]
                        {
                            new PointF(3f, 19f), new PointF(3f, 6f), new PointF(9.5f, 6f),
                            new PointF(11.5f, 8.5f), new PointF(21f, 8.5f), new PointF(21f, 19f)
                        });
                        path.CloseFigure();
                        g.DrawPath(pen, path);
                    }
                    break;

                case AppIcon.File:
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddLines(new[]
                        {
                            new PointF(6f, 3f), new PointF(14f, 3f), new PointF(19f, 8f),
                            new PointF(19f, 21f), new PointF(6f, 21f)
                        });
                        path.CloseFigure();
                        g.DrawPath(pen, path);
                    }
                    g.DrawLines(pen, new[] { new PointF(14f, 3f), new PointF(14f, 8f), new PointF(19f, 8f) });
                    break;

                case AppIcon.Gear:
                    DrawGear(g, pen, brush);
                    break;

                case AppIcon.Check:
                    g.DrawLines(pen, new[] { new PointF(5f, 12.5f), new PointF(10f, 17.5f), new PointF(19f, 6.5f) });
                    break;

                case AppIcon.Cross:
                    g.DrawLine(pen, 6.5f, 6.5f, 17.5f, 17.5f);
                    g.DrawLine(pen, 17.5f, 6.5f, 6.5f, 17.5f);
                    break;

                case AppIcon.Pause:
                    g.FillRectangle(brush, 7f, 5.5f, 3.5f, 13f);
                    g.FillRectangle(brush, 13.5f, 5.5f, 3.5f, 13f);
                    break;

                case AppIcon.Play:
                    g.FillPolygon(brush, new[] { new PointF(7.5f, 5f), new PointF(19f, 12f), new PointF(7.5f, 19f) });
                    break;

                case AppIcon.Trash:
                    g.DrawLine(pen, 4.5f, 6.5f, 19.5f, 6.5f);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(6.5f, 6.5f), new PointF(7.5f, 20f), new PointF(16.5f, 20f), new PointF(17.5f, 6.5f)
                    });
                    g.DrawLines(pen, new[] { new PointF(9.5f, 6.5f), new PointF(9.5f, 4f), new PointF(14.5f, 4f), new PointF(14.5f, 6.5f) });
                    break;

                case AppIcon.Copy:
                    g.DrawRectangle(pen, 8f, 8f, 12f, 13f);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(16f, 4f), new PointF(4f, 4f), new PointF(4f, 17f)
                    });
                    break;

                case AppIcon.Refresh:
                    g.DrawArc(pen, 4.5f, 4.5f, 15f, 15f, 60, 250);
                    g.FillPolygon(brush, new[] { new PointF(19.5f, 3.5f), new PointF(20.5f, 11f), new PointF(13.5f, 8f) });
                    break;

                case AppIcon.Warning:
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddPolygon(new[] { new PointF(12f, 3.5f), new PointF(22f, 20.5f), new PointF(2f, 20.5f) });
                        g.DrawPath(pen, path);
                    }
                    g.DrawLine(pen, 12f, 10f, 12f, 15f);
                    g.FillEllipse(brush, 10.9f, 17f, 2.2f, 2.2f);
                    break;

                case AppIcon.Link:
                    g.DrawLines(pen, new[]
                    {
                        new PointF(13f, 4f), new PointF(20f, 4f), new PointF(20f, 11f)
                    });
                    g.DrawLine(pen, 20f, 4f, 11f, 13f);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(17f, 14f), new PointF(17f, 20f), new PointF(4f, 20f), new PointF(4f, 7f), new PointF(10f, 7f)
                    });
                    break;

                case AppIcon.Plus:
                    g.DrawLine(pen, 12f, 5f, 12f, 19f);
                    g.DrawLine(pen, 5f, 12f, 19f, 12f);
                    break;

                case AppIcon.Eye:
                    DrawEye(g, pen, brush, false);
                    break;

                case AppIcon.EyeOff:
                    DrawEye(g, pen, brush, true);
                    break;

                case AppIcon.ChevronDown:
                    g.DrawLines(pen, new[] { new PointF(6f, 9.5f), new PointF(12f, 15.5f), new PointF(18f, 9.5f) });
                    break;

                case AppIcon.Info:
                    g.DrawEllipse(pen, 3.5f, 3.5f, 17f, 17f);
                    g.FillEllipse(brush, 10.9f, 7f, 2.2f, 2.2f);
                    g.DrawLine(pen, 12f, 11.5f, 12f, 17f);
                    break;

                case AppIcon.ArrowLeft:
                    g.DrawLine(pen, 20f, 12f, 5f, 12f);
                    g.DrawLines(pen, new[] { new PointF(11f, 6f), new PointF(5f, 12f), new PointF(11f, 18f) });
                    break;

                case AppIcon.ArrowUp:
                    g.DrawLine(pen, 12f, 20f, 12f, 5f);
                    g.DrawLines(pen, new[] { new PointF(6f, 11f), new PointF(12f, 5f), new PointF(18f, 11f) });
                    break;
            }
        }

        private static void DrawUploadCloud(Graphics g, Pen pen)
        {
            using (GraphicsPath cloud = new GraphicsPath())
            {
                cloud.AddArc(3.5f, 9f, 8f, 8f, 90, 180);      // left lobe
                cloud.AddArc(6.5f, 4.5f, 10f, 10f, 190, 160);  // top lobe
                cloud.AddArc(14f, 8f, 7f, 9f, 300, 150);       // right lobe
                g.DrawPath(pen, cloud);
            }

            g.DrawLine(pen, 12f, 20.5f, 12f, 10.5f);
            g.DrawLines(pen, new[] { new PointF(8.5f, 14f), new PointF(12f, 10.5f), new PointF(15.5f, 14f) });
        }

        private static void DrawGear(Graphics g, Pen pen, SolidBrush brush)
        {
            const float centre = 12f;
            const float outer = 9.5f;
            const float inner = 7.0f;

            using (GraphicsPath path = new GraphicsPath())
            {
                const int Teeth = 8;
                const double Step = Math.PI / Teeth;

                for (int i = 0; i < Teeth * 2; i++)
                {
                    double angle = i * Step;
                    float radius = (i % 2 == 0) ? outer : inner;
                    PointF a = new PointF(
                        centre + (float)(Math.Cos(angle) * radius),
                        centre + (float)(Math.Sin(angle) * radius));
                    PointF b = new PointF(
                        centre + (float)(Math.Cos(angle + Step) * radius),
                        centre + (float)(Math.Sin(angle + Step) * radius));
                    path.AddLine(a, b);
                }
                path.CloseFigure();
                g.DrawPath(pen, path);
            }

            g.DrawEllipse(pen, centre - 3.2f, centre - 3.2f, 6.4f, 6.4f);
        }

        private static void DrawEye(Graphics g, Pen pen, SolidBrush brush, bool crossedOut)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddBezier(2.5f, 12f, 7f, 5.5f, 17f, 5.5f, 21.5f, 12f);
                path.AddBezier(21.5f, 12f, 17f, 18.5f, 7f, 18.5f, 2.5f, 12f);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }

            g.DrawEllipse(pen, 9.4f, 9.4f, 5.2f, 5.2f);

            if (crossedOut) g.DrawLine(pen, 4f, 20f, 20f, 4f);
        }
    }
}
