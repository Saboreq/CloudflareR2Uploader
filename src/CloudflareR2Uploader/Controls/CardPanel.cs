using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Controls
{
    /// <summary>
    /// A rounded dark card with a hairline border, an optional soft shadow and an optional
    /// heading. Used as the layout container for each section of the main window, so the
    /// designer can still hold real child controls inside it.
    /// </summary>
    [ToolboxItem(true)]
    [Designer("System.Windows.Forms.Design.ParentControlDesigner, System.Design", typeof(System.ComponentModel.Design.IDesigner))]
    public class CardPanel : Panel
    {
        private int _cornerRadius = 16;
        private bool _showShadow;
        private int _shadowDepth = 4;
        private string _heading = string.Empty;
        private string _subheading = string.Empty;
        private AppIcon _headingIcon = AppIcon.None;
        private Color _cardColor = Color.Empty;
        private Color _borderColor = Color.Empty;

        public CardPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;
            Padding = new Padding(16);
        }

        [Category("Appearance")]
        [DefaultValue(16)]
        public int CornerRadius
        {
            get { return _cornerRadius; }
            set
            {
                int clamped = value < 0 ? 0 : (value > 40 ? 40 : value);
                if (_cornerRadius == clamped) return;
                _cornerRadius = clamped;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [DefaultValue(false)]
        public bool ShowShadow
        {
            get { return _showShadow; }
            set
            {
                if (_showShadow == value) return;
                _showShadow = value;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [DefaultValue(4)]
        public int ShadowDepth
        {
            get { return _shadowDepth; }
            set
            {
                int clamped = value < 0 ? 0 : (value > 12 ? 12 : value);
                if (_shadowDepth == clamped) return;
                _shadowDepth = clamped;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [DefaultValue("")]
        [Description("Optional heading drawn inside the top padding of the card.")]
        public string Heading
        {
            get { return _heading; }
            set
            {
                string text = value ?? string.Empty;
                if (_heading == text) return;
                _heading = text;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [DefaultValue("")]
        public string Subheading
        {
            get { return _subheading; }
            set
            {
                string text = value ?? string.Empty;
                if (_subheading == text) return;
                _subheading = text;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [DefaultValue(AppIcon.None)]
        public AppIcon HeadingIcon
        {
            get { return _headingIcon; }
            set
            {
                if (_headingIcon == value) return;
                _headingIcon = value;
                Invalidate();
            }
        }

        [Category("Appearance")]
        public Color CardColor
        {
            get { return _cardColor; }
            set
            {
                if (_cardColor == value) return;
                _cardColor = value;
                Invalidate();
            }
        }

        [Category("Appearance")]
        public Color BorderColor
        {
            get { return _borderColor; }
            set
            {
                if (_borderColor == value) return;
                _borderColor = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.UseHighQuality();

            int inset = _showShadow ? _shadowDepth : 0;
            Rectangle bounds = new Rectangle(inset, inset, Width - (inset * 2) - 1, Height - (inset * 2) - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            if (_showShadow && _shadowDepth > 0)
            {
                g.DrawSoftShadow(bounds, _cornerRadius, _shadowDepth, Color.FromArgb(150, 0, 0, 0));
            }

            Color fill = _cardColor.IsEmpty ? Theme.CardBackground : _cardColor;
            Color border = _borderColor.IsEmpty ? Theme.Border : _borderColor;

            using (GraphicsPath path = GraphicsExtensions.CreateRoundedRectangle(bounds, _cornerRadius))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(bounds.Height, 2)),
                    Theme.Blend(fill, Color.White, 0.018),
                    fill,
                    LinearGradientMode.Vertical))
                {
                    g.FillPath(brush, path);
                }

                using (Pen pen = new Pen(border, 1f)) g.DrawPath(pen, path);
            }

            DrawTopEdge(g, bounds);
            DrawHeading(g, bounds);

            base.OnPaint(e);
        }

        private void DrawHeading(Graphics g, Rectangle bounds)
        {
            if (string.IsNullOrEmpty(_heading)) return;

            int left = bounds.X + Padding.Left;
            int top = bounds.Y + Math.Max(7, Padding.Top - 12);

            if (_headingIcon != AppIcon.None)
            {
                Rectangle iconBounds = new Rectangle(left, top + 1, 13, 13);
                IconPainter.Draw(g, _headingIcon, iconBounds, Theme.AccentHover, 1.8f);
                left += 20;
            }

            string heading = _heading.ToUpperInvariant();
            Size headingSize = TextRenderer.MeasureText(
                g, heading, ThemeFonts.Eyebrow, new Size(int.MaxValue, int.MaxValue),
                GraphicsExtensions.SingleLineFlags);

            TextRenderer.DrawText(
                g, heading, ThemeFonts.Eyebrow,
                new Point(left, top), Theme.TextSecondary, GraphicsExtensions.SingleLineFlags);

            if (string.IsNullOrEmpty(_subheading)) return;

            int subLeft = left + headingSize.Width + 10;
            int available = bounds.Right - Padding.Right - subLeft;
            if (available <= 20) return;

            string subheading = GraphicsExtensions.EllipsizeToWidth(g, _subheading, ThemeFonts.Caption, available);

            TextRenderer.DrawText(
                g, subheading, ThemeFonts.Caption,
                new Point(subLeft, top + 3), Theme.TextMuted, GraphicsExtensions.SingleLineFlags);
        }

        private static void DrawTopEdge(Graphics g, Rectangle bounds)
        {
            int left = bounds.X + Math.Max(18, bounds.Width / 10);
            int width = bounds.Width - ((left - bounds.X) * 2);
            if (width <= 0) return;

            using (LinearGradientBrush edge = new LinearGradientBrush(
                new Rectangle(left, bounds.Y, width, 1),
                Color.Transparent,
                Theme.WithAlpha(Theme.AccentHover, 92),
                LinearGradientMode.Horizontal))
            {
                ColorBlend blend = new ColorBlend(3);
                blend.Colors = new[]
                {
                    Color.Transparent,
                    Theme.WithAlpha(Theme.AccentHover, 92),
                    Color.Transparent
                };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                edge.InterpolationColors = blend;
                g.FillRectangle(edge, left, bounds.Y, width, 1);
            }
        }
    }
}
