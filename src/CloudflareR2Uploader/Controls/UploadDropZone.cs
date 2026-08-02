using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Controls
{
    public sealed class PathsDroppedEventArgs : EventArgs
    {
        public PathsDroppedEventArgs(string[] paths) { Paths = paths ?? new string[0]; }
        public string[] Paths { get; private set; }
    }

    /// <summary>
    /// The large drop target at the top of the window. Accepts dropped files and folders,
    /// and hosts the two browse buttons.
    /// </summary>
    [ToolboxItem(true)]
    [DefaultEvent("PathsDropped")]
    public class UploadDropZone : Control
    {
        private const int AnimationIntervalMs = 16;

        private readonly ModernButton _browseFilesButton;
        private readonly ModernButton _browseFolderButton;
        private readonly Timer _timer;

        private bool _dragActive;
        private double _dragAmount;
        private double _dashOffset;

        public UploadDropZone()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.ContainerControl |
                ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;
            Size = new Size(640, 190);
            AllowDrop = true;
            TabStop = false;

            _browseFilesButton = new ModernButton
            {
                Text = "Browse files",
                ButtonStyle = ModernButtonStyle.Primary,
                Icon = AppIcon.File,
                Size = new Size(140, 36),
                TabIndex = 0
            };
            _browseFilesButton.Click += delegate { RaiseBrowseFiles(); };

            _browseFolderButton = new ModernButton
            {
                Text = "Browse folder",
                ButtonStyle = ModernButtonStyle.Secondary,
                Icon = AppIcon.Folder,
                Size = new Size(148, 36),
                TabIndex = 1
            };
            _browseFolderButton.Click += delegate { RaiseBrowseFolder(); };

            Controls.Add(_browseFilesButton);
            Controls.Add(_browseFolderButton);

            _timer = new Timer { Interval = AnimationIntervalMs };
            _timer.Tick += OnTick;

            AccessibleRole = AccessibleRole.DropList;
            AccessibleName = "Upload drop area";
            AccessibleDescription = "Drop files or folders here to add them to the upload queue. Files larger than 300 MB are supported.";
        }

        [Category("Action")]
        public event EventHandler BrowseFilesRequested;

        [Category("Action")]
        public event EventHandler BrowseFolderRequested;

        [Category("Action")]
        public event EventHandler<PathsDroppedEventArgs> PathsDropped;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ModernButton BrowseFilesButton { get { return _browseFilesButton; } }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ModernButton BrowseFolderButton { get { return _browseFolderButton; } }

        // ------------------------------------------------------------------------- layout

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            // Setting Font/Size in the constructor can trigger layout before the hosted
            // buttons have been created.
            if (_browseFilesButton == null || _browseFolderButton == null) return;

            int gap = 12;
            int totalWidth = _browseFilesButton.Width + gap + _browseFolderButton.Width;
            int left = (Width - totalWidth) / 2;
            // Keep the actions below the supporting caption while retaining a comfortable
            // bottom inset at the compact designer height.
            int top = Height - _browseFilesButton.Height - 12;

            if (left < 8) left = 8;
            if (top < 8) top = 8;

            _browseFilesButton.Location = new Point(left, top);
            _browseFolderButton.Location = new Point(left + _browseFilesButton.Width + gap, top);
        }

        // ------------------------------------------------------------------------ painting

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.UseHighQuality();

            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            const int Radius = 16;

            Color fillTop = Theme.Blend(Theme.CardBackground, Theme.AccentSoft, 0.16 + (_dragAmount * 0.44));
            Color fillBottom = Theme.Blend(Theme.SurfaceBackground, Theme.CyanSoft, 0.08 + (_dragAmount * 0.24));

            using (GraphicsPath path = GraphicsExtensions.CreateRoundedRectangle(bounds, Radius))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(bounds.Height, 2)),
                    fillTop, fillBottom, LinearGradientMode.Vertical))
                {
                    g.FillPath(brush, path);
                }

                GraphicsState gridState = g.Save();
                g.SetClip(path);
                DrawGrid(g, bounds);
                g.Restore(gridState);

                Color activeAccent = Theme.Blend(Theme.Accent, Theme.Cyan, _dragAmount * 0.22);
                Color borderColor = Theme.Blend(Theme.BorderStrong, activeAccent, 0.30 + (_dragAmount * 0.70));
                using (Pen pen = new Pen(borderColor, 1.6f + (float)(_dragAmount * 0.8)))
                {
                    pen.DashStyle = DashStyle.Dash;
                    pen.DashPattern = new[] { 7f, 5f };
                    pen.DashOffset = (float)_dashOffset;
                    g.DrawPath(pen, path);
                }
            }

            DrawTopEdge(g, bounds);
            DrawContent(g, bounds);
        }

        private static void DrawGrid(Graphics g, Rectangle bounds)
        {
            using (Pen grid = new Pen(Theme.WithAlpha(Theme.TextPrimary, 7), 1f))
            {
                const int spacing = 52;
                for (int x = bounds.X + spacing; x < bounds.Right; x += spacing)
                    g.DrawLine(grid, x, bounds.Y, x, bounds.Bottom);
                for (int y = bounds.Y + spacing; y < bounds.Bottom; y += spacing)
                    g.DrawLine(grid, bounds.X, y, bounds.Right, y);
            }
        }

        private static void DrawTopEdge(Graphics g, Rectangle bounds)
        {
            int left = bounds.X + bounds.Width / 8;
            int width = bounds.Width - (bounds.Width / 4);
            if (width <= 0) return;

            using (LinearGradientBrush edge = new LinearGradientBrush(
                new Rectangle(left, bounds.Y, width, 1),
                Color.Transparent,
                Theme.WithAlpha(Theme.AccentHover, 135),
                LinearGradientMode.Horizontal))
            {
                ColorBlend blend = new ColorBlend(3);
                blend.Colors = new[]
                {
                    Color.Transparent,
                    Theme.WithAlpha(Theme.AccentHover, 135),
                    Color.Transparent
                };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                edge.InterpolationColors = blend;
                g.FillRectangle(edge, left, bounds.Y, width, 1);
            }
        }

        private void DrawContent(Graphics g, Rectangle bounds)
        {
            int iconSize = 44;
            int contentTop = bounds.Y + 20;

            Color iconColor = Theme.Blend(Theme.AccentHover, Theme.Cyan, _dragAmount * 0.30);

            // Halo behind the icon, brightening while a drag hovers.
            int haloSize = (int)(iconSize * (1.55 + (_dragAmount * 0.25)));
            using (SolidBrush halo = new SolidBrush(Theme.WithAlpha(Theme.Accent, (int)(28 + (_dragAmount * 42)))))
            {
                g.FillEllipse(halo,
                    (Width - haloSize) / 2f,
                    contentTop - ((haloSize - iconSize) / 2f),
                    haloSize, haloSize);
            }

            IconPainter.Draw(g, AppIcon.UploadCloud,
                new Rectangle((Width - iconSize) / 2, contentTop, iconSize, iconSize),
                iconColor, 2.1f);

            int y = contentTop + iconSize + 12;

            string headline = _dragActive ? "Release to add these items" : "Drop files or folders here";
            DrawCentredText(g, headline, ThemeFonts.SectionHeader, Theme.TextPrimary, y);
            y += ThemeFonts.SectionHeader.Height + 6;

            DrawCentredText(g, "MULTIPART READY  /  NO 300 MB LIMIT",
                ThemeFonts.Eyebrow, Theme.TextMuted, y);
        }

        private void DrawCentredText(Graphics g, string text, Font font, Color color, int top)
        {
            if (string.IsNullOrEmpty(text)) return;

            string display = GraphicsExtensions.EllipsizeToWidth(g, text, font, Width - 32);
            Size size = TextRenderer.MeasureText(g, display, font, new Size(int.MaxValue, int.MaxValue),
                GraphicsExtensions.SingleLineFlags);

            TextRenderer.DrawText(g, display, font, new Point((Width - size.Width) / 2, top), color,
                GraphicsExtensions.SingleLineFlags);
        }

        // ---------------------------------------------------------------------- drag & drop

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            UpdateDragEffect(drgevent);
        }

        protected override void OnDragOver(DragEventArgs drgevent)
        {
            base.OnDragOver(drgevent);
            UpdateDragEffect(drgevent);
        }

        private void UpdateDragEffect(DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                SetDragActive(true);
            }
            else
            {
                e.Effect = DragDropEffects.None;
                SetDragActive(false);
            }
        }

        protected override void OnDragLeave(EventArgs e)
        {
            base.OnDragLeave(e);
            SetDragActive(false);
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);
            SetDragActive(false);

            if (drgevent.Data == null || !drgevent.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[] paths = drgevent.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;

            EventHandler<PathsDroppedEventArgs> handler = PathsDropped;
            if (handler != null) handler(this, new PathsDroppedEventArgs(paths));
        }

        private void SetDragActive(bool active)
        {
            if (_dragActive == active) return;
            _dragActive = active;
            EnsureAnimating();
            Invalidate();
        }

        // ------------------------------------------------------------------------ animation

        private void OnTick(object sender, EventArgs e)
        {
            bool needsMore = false;

            double target = _dragActive ? 1.0 : 0.0;
            if (Math.Abs(_dragAmount - target) > 0.01)
            {
                _dragAmount += (_dragAmount < target ? 0.14 : -0.14);
                if (_dragAmount < 0) _dragAmount = 0;
                if (_dragAmount > 1) _dragAmount = 1;
                needsMore = true;
            }
            else if (_dragAmount != target)
            {
                _dragAmount = target;
                needsMore = true;
            }

            // The dashes crawl only while a drag is over the zone.
            if (_dragActive)
            {
                _dashOffset -= 0.35;
                if (_dashOffset < -1000) _dashOffset = 0;
                needsMore = true;
            }

            Invalidate();
            if (!needsMore) _timer.Stop();
        }

        private void EnsureAnimating()
        {
            if (DesignMode || !IsHandleCreated || !Visible) return;
            if (!_timer.Enabled) _timer.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) _timer.Stop();
        }

        private void RaiseBrowseFiles()
        {
            EventHandler handler = BrowseFilesRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void RaiseBrowseFolder()
        {
            EventHandler handler = BrowseFolderRequested;
            if (handler != null) handler(this, EventArgs.Empty);
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
