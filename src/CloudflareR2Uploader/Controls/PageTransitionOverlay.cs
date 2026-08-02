using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CloudflareR2Uploader.Controls
{
    /// <summary>Cross-fades two page snapshots without recreating either live page.</summary>
    public sealed class PageTransitionOverlay : Control
    {
        private const int DurationMs = 190;
        private readonly Timer _timer;
        private Bitmap _outgoing;
        private Bitmap _incoming;
        private DateTime _startedUtc;
        private bool _towardsFiles;

        public PageTransitionOverlay()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Visible = false;
            _timer = new Timer { Interval = 15 };
            _timer.Tick += OnTick;
        }

        public bool IsAnimating { get { return _timer.Enabled; } }

        public void Start(Bitmap outgoing, Bitmap incoming, bool towardsFiles)
        {
            Stop();
            if (outgoing == null || incoming == null)
            {
                if (outgoing != null) outgoing.Dispose();
                if (incoming != null) incoming.Dispose();
                return;
            }

            _outgoing = outgoing;
            _incoming = incoming;
            _towardsFiles = towardsFiles;
            _startedUtc = DateTime.UtcNow;
            Visible = true;
            BringToFront();
            _timer.Start();
            Invalidate();
        }

        public void Stop()
        {
            _timer.Stop();
            Visible = false;
            if (_outgoing != null) _outgoing.Dispose();
            if (_incoming != null) _incoming.Dispose();
            _outgoing = null;
            _incoming = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_outgoing == null || _incoming == null) return;

            double raw = (DateTime.UtcNow - _startedUtc).TotalMilliseconds / DurationMs;
            if (raw < 0) raw = 0;
            if (raw > 1) raw = 1;
            float progress = (float)(1.0 - Math.Pow(1.0 - raw, 3.0));
            int direction = _towardsFiles ? -1 : 1;
            int travel = Math.Max(8, Width / 90);

            DrawImage(e.Graphics, _incoming,
                new Rectangle(0, 0, Width, Height), 1f);
            DrawImage(e.Graphics, _outgoing,
                new Rectangle(direction * (int)(travel * progress), 0, Width, Height), 1f - progress);
        }

        private static void DrawImage(Graphics graphics, Image image, Rectangle destination, float opacity)
        {
            if (opacity <= 0) return;
            ColorMatrix matrix = new ColorMatrix { Matrix33 = opacity };
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                graphics.DrawImage(
                    image,
                    destination,
                    0,
                    0,
                    image.Width,
                    image.Height,
                    GraphicsUnit.Pixel,
                    attributes);
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _startedUtc).TotalMilliseconds >= DurationMs)
            {
                Stop();
                return;
            }
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
                _timer.Tick -= OnTick;
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
