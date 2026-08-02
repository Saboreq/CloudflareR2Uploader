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
    /// A rounded, purple progress bar with an optional indeterminate sweep.
    /// The bar animates towards its target value rather than jumping, which makes bursty
    /// multipart progress read smoothly — the value shown is always a real measurement, never
    /// a timer-driven guess.
    /// </summary>
    [ToolboxItem(true)]
    [DefaultProperty("Value")]
    public class ModernProgressBar : Control
    {
        private const int AnimationIntervalMs = 16;

        private readonly Timer _timer;

        private double _value;
        private double _displayedValue;
        private bool _indeterminate;
        private double _sweepPosition;
        private Color _barColor = Color.Empty;
        private int _cornerRadius = -1;

        public ModernProgressBar()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            Size = new Size(200, 8);
            TabStop = false;

            _timer = new Timer { Interval = AnimationIntervalMs };
            _timer.Tick += OnTick;
        }

        /// <summary>Progress in the range 0..1.</summary>
        [Category("Behavior")]
        [DefaultValue(0.0)]
        public double Value
        {
            get { return _value; }
            set
            {
                double clamped = value < 0 ? 0 : (value > 1 ? 1 : value);
                if (Math.Abs(_value - clamped) < 0.0001) return;

                _value = clamped;

                if (DesignMode)
                {
                    _displayedValue = clamped;
                    Invalidate();
                    return;
                }

                EnsureAnimating();
            }
        }

        [Category("Behavior")]
        [DefaultValue(false)]
        [Description("Shows a sweeping indicator for work whose size is not yet known.")]
        public bool Indeterminate
        {
            get { return _indeterminate; }
            set
            {
                if (_indeterminate == value) return;
                _indeterminate = value;

                if (value) EnsureAnimating();
                Invalidate();
            }
        }

        [Category("Appearance")]
        [Description("Overrides the violet accent, e.g. red for a failed item.")]
        public Color BarColor
        {
            get { return _barColor; }
            set
            {
                if (_barColor == value) return;
                _barColor = value;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [DefaultValue(-1)]
        [Description("Corner radius; -1 makes the bar fully rounded.")]
        public int CornerRadius
        {
            get { return _cornerRadius; }
            set
            {
                if (_cornerRadius == value) return;
                _cornerRadius = value;
                Invalidate();
            }
        }

        /// <summary>Jumps straight to a value with no animation, e.g. when reusing a row.</summary>
        public void SetValueImmediate(double value)
        {
            double clamped = value < 0 ? 0 : (value > 1 ? 1 : value);
            _value = clamped;
            _displayedValue = clamped;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.UseHighQuality();

            Rectangle bounds = new Rectangle(0, 0, Width, Height);
            if (bounds.Width <= 1 || bounds.Height <= 0) return;

            int radius = _cornerRadius >= 0 ? _cornerRadius : bounds.Height / 2;

            using (SolidBrush track = new SolidBrush(Theme.Blend(Theme.WindowBackground, Theme.Border, 0.55)))
            {
                g.FillRoundedRectangle(track, bounds, radius);
            }

            Color accent = _barColor.IsEmpty ? Theme.Accent : _barColor;

            if (_indeterminate)
            {
                DrawIndeterminate(g, bounds, radius, accent);
                return;
            }

            if (_displayedValue <= 0) return;

            int fillWidth = (int)Math.Round(bounds.Width * _displayedValue);
            // Keep a sliver visible so "just started" does not look like "not started".
            if (fillWidth < bounds.Height && _displayedValue > 0) fillWidth = Math.Min(bounds.Height, bounds.Width);

            Rectangle fill = new Rectangle(bounds.X, bounds.Y, fillWidth, bounds.Height);

            using (LinearGradientBrush brush = new LinearGradientBrush(
                new Rectangle(bounds.X, bounds.Y, Math.Max(bounds.Width, 2), Math.Max(bounds.Height, 2)),
                Theme.Blend(accent, Theme.AccentBright, 0.32),
                _barColor.IsEmpty ? Theme.Blend(accent, Theme.Cyan, 0.28) : accent,
                LinearGradientMode.Horizontal))
            {
                g.FillRoundedRectangle(brush, fill, radius);
            }
        }

        private void DrawIndeterminate(Graphics g, Rectangle bounds, int radius, Color accent)
        {
            int sweepWidth = Math.Max(bounds.Height * 4, bounds.Width / 4);
            int travel = bounds.Width + sweepWidth;
            int x = (int)(_sweepPosition * travel) - sweepWidth;

            Rectangle sweep = new Rectangle(x, bounds.Y, sweepWidth, bounds.Height);
            Rectangle clipped = Rectangle.Intersect(sweep, bounds);
            if (clipped.Width <= 0) return;

            using (GraphicsPath clip = GraphicsExtensions.CreateRoundedRectangle(bounds, radius))
            {
                Region previous = g.Clip;
                g.SetClip(clip);

                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(sweep.X, sweep.Y, Math.Max(sweep.Width, 2), Math.Max(sweep.Height, 2)),
                    Theme.WithAlpha(accent, 0),
                    accent,
                    LinearGradientMode.Horizontal))
                {
                    ColorBlend blend = new ColorBlend(3);
                    blend.Colors = new[] { Theme.WithAlpha(accent, 0), accent, Theme.WithAlpha(accent, 0) };
                    blend.Positions = new[] { 0f, 0.5f, 1f };
                    brush.InterpolationColors = blend;

                    g.FillRectangle(brush, clipped);
                }

                g.Clip = previous;
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            bool needsMore = false;

            if (_indeterminate)
            {
                _sweepPosition += 0.012;
                if (_sweepPosition > 1) _sweepPosition = 0;
                needsMore = true;
            }

            double difference = _value - _displayedValue;
            if (Math.Abs(difference) > 0.0005)
            {
                // Ease towards the real value; large jumps still land within a few frames.
                _displayedValue += difference * 0.25;
                if (Math.Abs(_value - _displayedValue) <= 0.0005) _displayedValue = _value;
                needsMore = true;
            }
            else if (_displayedValue != _value)
            {
                _displayedValue = _value;
                needsMore = true;
            }

            Invalidate();

            if (!needsMore) _timer.Stop();
        }

        private void EnsureAnimating()
        {
            if (DesignMode || !IsHandleCreated || !Visible)
            {
                _displayedValue = _value;
                Invalidate();
                return;
            }

            if (!_timer.Enabled) _timer.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            // Animation must not run for rows scrolled out of view.
            if (!Visible) _timer.Stop();
            else if (_indeterminate || Math.Abs(_value - _displayedValue) > 0.0005) EnsureAnimating();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Tick -= OnTick;
                _timer.Stop();
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
