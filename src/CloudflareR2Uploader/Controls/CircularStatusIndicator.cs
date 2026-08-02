using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Controls
{
    public enum ConnectionIndicatorState
    {
        Unknown = 0,
        Connecting = 1,
        Connected = 2,
        Warning = 3,
        Error = 4
    }

    /// <summary>
    /// A small status dot with a soft halo, plus a rotating arc while connecting.
    /// The state is always mirrored in adjacent text, so colour is never the only cue.
    /// </summary>
    [ToolboxItem(true)]
    [DefaultProperty("State")]
    public class CircularStatusIndicator : Control
    {
        private readonly Timer _timer;

        private ConnectionIndicatorState _state = ConnectionIndicatorState.Unknown;
        private float _angle;
        private double _pulse;
        private bool _pulseRising = true;

        public CircularStatusIndicator()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            Size = new Size(16, 16);
            TabStop = false;

            _timer = new Timer { Interval = 40 };
            _timer.Tick += OnTick;
        }

        [Category("Appearance")]
        [DefaultValue(ConnectionIndicatorState.Unknown)]
        public ConnectionIndicatorState State
        {
            get { return _state; }
            set
            {
                if (_state == value) return;
                _state = value;

                AccessibleDescription = GetStateText(value);
                UpdateAnimation();
                Invalidate();
            }
        }

        /// <summary>Plain-language wording for the current state, used for the accessible name.</summary>
        public static string GetStateText(ConnectionIndicatorState state)
        {
            switch (state)
            {
                case ConnectionIndicatorState.Connecting: return "Connecting";
                case ConnectionIndicatorState.Connected: return "Connected";
                case ConnectionIndicatorState.Warning: return "Attention needed";
                case ConnectionIndicatorState.Error: return "Not connected";
                default: return "Not configured";
            }
        }

        public Color GetStateColor()
        {
            switch (_state)
            {
                case ConnectionIndicatorState.Connected: return Theme.Success;
                case ConnectionIndicatorState.Connecting: return Theme.Accent;
                case ConnectionIndicatorState.Warning: return Theme.Warning;
                case ConnectionIndicatorState.Error: return Theme.Error;
                default: return Theme.TextMuted;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.UseHighQuality();

            int side = Math.Min(Width, Height);
            if (side < 6) return;

            Color color = GetStateColor();
            float centreX = Width / 2f;
            float centreY = Height / 2f;

            if (_state == ConnectionIndicatorState.Connecting)
            {
                float arcSize = side - 2f;
                using (Pen pen = new Pen(Theme.WithAlpha(color, 60), 1.8f))
                {
                    g.DrawEllipse(pen, centreX - arcSize / 2f, centreY - arcSize / 2f, arcSize, arcSize);
                }
                using (Pen pen = new Pen(color, 1.8f))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawArc(pen, centreX - arcSize / 2f, centreY - arcSize / 2f, arcSize, arcSize, _angle, 100f);
                }
                return;
            }

            // Halo, breathing gently when connected.
            float haloScale = 1.0f + (float)(_pulse * 0.22);
            float haloSize = side * 0.92f * haloScale;
            int haloAlpha = _state == ConnectionIndicatorState.Unknown ? 26 : (int)(52 - (_pulse * 20));

            using (SolidBrush halo = new SolidBrush(Theme.WithAlpha(color, Math.Max(0, haloAlpha))))
            {
                g.FillEllipse(halo, centreX - haloSize / 2f, centreY - haloSize / 2f, haloSize, haloSize);
            }

            float dotSize = side * 0.46f;
            using (SolidBrush dot = new SolidBrush(color))
            {
                g.FillEllipse(dot, centreX - dotSize / 2f, centreY - dotSize / 2f, dotSize, dotSize);
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_state == ConnectionIndicatorState.Connecting)
            {
                _angle = (_angle + 11f) % 360f;
            }
            else
            {
                _pulse += _pulseRising ? 0.035 : -0.035;
                if (_pulse >= 1) { _pulse = 1; _pulseRising = false; }
                else if (_pulse <= 0) { _pulse = 0; _pulseRising = true; }
            }

            Invalidate();
        }

        private void UpdateAnimation()
        {
            // No animation at design time, and none while the control is not on screen.
            bool shouldRun = !DesignMode
                             && Visible
                             && IsHandleCreated
                             && (_state == ConnectionIndicatorState.Connecting || _state == ConnectionIndicatorState.Connected);

            if (shouldRun)
            {
                if (!_timer.Enabled) _timer.Start();
            }
            else if (_timer.Enabled)
            {
                _timer.Stop();
                _pulse = 0;
                _angle = 0;
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateAnimation();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateAnimation();
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

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new IndicatorAccessibleObject(this);
        }

        private sealed class IndicatorAccessibleObject : ControlAccessibleObject
        {
            public IndicatorAccessibleObject(CircularStatusIndicator owner) : base(owner) { }

            public override AccessibleRole Role { get { return AccessibleRole.StatusBar; } }

            public override string Value
            {
                get
                {
                    CircularStatusIndicator indicator = Owner as CircularStatusIndicator;
                    return indicator == null ? string.Empty : GetStateText(indicator.State);
                }
            }
        }
    }
}
