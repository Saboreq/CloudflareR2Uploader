using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Controls
{
    public sealed class QueueItemActionEventArgs : EventArgs
    {
        public QueueItemActionEventArgs(UploadQueueItem item) { Item = item; }
        public UploadQueueItem Item { get; private set; }
    }

    /// <summary>
    /// One row of the upload queue. Fully owner-drawn — a row is repainted many times a second
    /// while uploading, and drawing is far cheaper than a nest of child controls per file.
    /// <para>
    /// The row is focusable and every action has a keyboard equivalent, so the queue is usable
    /// without a mouse.
    /// </para>
    /// </summary>
    [ToolboxItem(true)]
    public class UploadQueueItemControl : Control
    {
        private const int RowPadding = 12;
        private const int IconSize = 30;
        private const int ActionButtonSize = 28;
        private const int BarHeight = 6;

        private readonly ToolTip _toolTip;

        private UploadQueueItem _item;
        private bool _hovered;
        private int _hoveredAction = -1;
        private int _pressedAction = -1;
        private double _displayedFraction;
        private readonly Timer _barTimer;
        private string _lastTooltip = string.Empty;

        public UploadQueueItemControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw, true);

            BackColor = Theme.SurfaceBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;
            Height = 86;
            Width = 720;
            TabStop = true;
            Margin = new Padding(0, 0, 0, 8);

            _toolTip = new ToolTip { InitialDelay = 400, ReshowDelay = 120, ShowAlways = true };

            _barTimer = new Timer { Interval = 16 };
            _barTimer.Tick += OnBarTick;
        }

        [Category("Action")]
        public event EventHandler<QueueItemActionEventArgs> RemoveRequested;

        [Category("Action")]
        public event EventHandler<QueueItemActionEventArgs> RetryRequested;

        [Category("Action")]
        public event EventHandler<QueueItemActionEventArgs> CopyKeyRequested;

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public UploadQueueItem Item
        {
            get { return _item; }
            set
            {
                if (_item == value) return;

                if (_item != null) _item.Changed -= OnItemChanged;
                _item = value;

                if (_item != null)
                {
                    _item.Changed += OnItemChanged;
                    _displayedFraction = _item.Fraction;
                    AccessibleName = _item.FileName;
                }

                UpdateAccessibleValue();
                Invalidate();
            }
        }

        private void OnItemChanged(object sender, EventArgs e)
        {
            // Raised from the upload worker; hop to the UI thread before touching the control.
            UiThreadUtility.Post(this, () =>
            {
                UpdateAccessibleValue();
                EnsureBarAnimating();
                Invalidate();
            });
        }

        private void UpdateAccessibleValue()
        {
            if (_item == null) return;

            AccessibleDescription = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                "{0}. {1}. {2} percent. Destination {3}.",
                _item.FileName,
                _item.Status.ToDisplayText(),
                _item.Percent,
                _item.ObjectKey);
        }

        // ------------------------------------------------------------------------- painting

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.UseHighQuality();

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bounds.Width <= 4 || bounds.Height <= 4) return;

            using (SolidBrush background = new SolidBrush(Theme.SurfaceBackground))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            Color fill = _hovered ? Theme.CardHover : Theme.ElevatedBackground;
            Color border = Focused ? Theme.Accent : Theme.Border;

            using (GraphicsPath path = GraphicsExtensions.CreateRoundedRectangle(bounds, 10))
            {
                using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (Pen pen = new Pen(border, Focused ? 1.5f : 1f)) g.DrawPath(pen, path);
            }

            if (_item == null)
            {
                TextRenderer.DrawText(g, "(no file)", Font, new Point(RowPadding, RowPadding),
                    Theme.TextMuted, GraphicsExtensions.SingleLineFlags);
                return;
            }

            UploadItemStatus status = _item.Status;
            Color statusColor = GetStatusColor(status);

            // Status stripe down the left edge: a second, non-colour cue is the status text.
            using (GraphicsPath stripe = GraphicsExtensions.CreateRoundedRectangle(
                new Rectangle(bounds.X, bounds.Y + 8, 6, bounds.Height - 16), 3))
            {
                using (SolidBrush brush = new SolidBrush(statusColor)) g.FillPath(brush, stripe);
            }

            int left = RowPadding + 6;

            DrawTypeIcon(g, left, statusColor);
            left += IconSize + 12;

            int actionsWidth = (ActionButtonSize + 4) * 3 + 6;
            int right = Width - RowPadding - actionsWidth;
            int textWidth = Math.Max(60, right - left - 8);

            DrawPrimaryLine(g, left, textWidth, statusColor);
            DrawDestinationLine(g, left, textWidth);
            DrawProgress(g, left, textWidth, statusColor);

            DrawActions(g);
        }

        private void DrawTypeIcon(Graphics g, int left, Color statusColor)
        {
            int top = (Height - IconSize) / 2;
            Rectangle box = new Rectangle(left, top, IconSize, IconSize);

            using (SolidBrush brush = new SolidBrush(Theme.Blend(Theme.CardBackgroundAlt, statusColor, 0.14)))
            {
                g.FillRoundedRectangle(brush, box, 8);
            }
            using (Pen pen = new Pen(Theme.WithAlpha(statusColor, 90), 1f))
            {
                g.DrawRoundedRectangle(pen, box, 8);
            }

            AppIcon icon = GetStatusIcon(_item.Status);
            IconPainter.Draw(g, icon, Rectangle.Inflate(box, -7, -7), statusColor, 2f);
        }

        private void DrawPrimaryLine(Graphics g, int left, int width, Color statusColor)
        {
            int top = 12;

            string name = GraphicsExtensions.EllipsizeToWidth(g, _item.FileName, ThemeFonts.BodyBold, width - 120);
            Size nameSize = TextRenderer.MeasureText(g, name, ThemeFonts.BodyBold,
                new Size(int.MaxValue, int.MaxValue), GraphicsExtensions.SingleLineFlags);

            TextRenderer.DrawText(g, name, ThemeFonts.BodyBold, new Point(left, top),
                Theme.TextPrimary, GraphicsExtensions.SingleLineFlags);

            // Size, then the status pill, both anchored after the name.
            int x = left + nameSize.Width + 10;

            string size = FileSizeFormatter.Format(_item.FileSize);
            Size sizeSize = TextRenderer.MeasureText(g, size, ThemeFonts.Caption,
                new Size(int.MaxValue, int.MaxValue), GraphicsExtensions.SingleLineFlags);

            TextRenderer.DrawText(g, size, ThemeFonts.Caption, new Point(x, top + 2),
                Theme.TextMuted, GraphicsExtensions.SingleLineFlags);
            x += sizeSize.Width + 10;

            DrawStatusPill(g, x, top, statusColor);
        }

        private void DrawStatusPill(Graphics g, int x, int top, Color statusColor)
        {
            string text = _item.Status.ToDisplayText();
            if (_item.RetryCount > 0 && !_item.Status.IsTerminal())
                text += " · retry " + _item.RetryCount;
            else if (_item.RetryCount > 0)
                text += " · " + _item.RetryCount + " retries";

            if (_item.Status == UploadItemStatus.Uploading && _item.TotalParts > 0)
                text += " · part " + Math.Min(_item.CompletedParts + 1, _item.TotalParts) + "/" + _item.TotalParts;

            Size textSize = TextRenderer.MeasureText(g, text, ThemeFonts.CaptionBold,
                new Size(int.MaxValue, int.MaxValue), GraphicsExtensions.SingleLineFlags);

            Rectangle pill = new Rectangle(x, top, textSize.Width + 16, textSize.Height + 5);
            if (pill.Right > Width - RowPadding - ((ActionButtonSize + 4) * 3 + 12)) return;

            using (SolidBrush brush = new SolidBrush(Theme.WithAlpha(statusColor, 34)))
            {
                g.FillRoundedRectangle(brush, pill, pill.Height / 2);
            }

            TextRenderer.DrawText(g, text, ThemeFonts.CaptionBold,
                new Point(pill.X + 8, pill.Y + 2), statusColor, GraphicsExtensions.SingleLineFlags);
        }

        private void DrawDestinationLine(Graphics g, int left, int width)
        {
            int top = 34;

            IconPainter.Draw(g, AppIcon.UploadArrow, new Rectangle(left, top, 12, 12), Theme.TextMuted, 1.6f);

            string key = string.IsNullOrEmpty(_item.ObjectKey) ? "(no destination key)" : _item.ObjectKey;
            string display = GraphicsExtensions.EllipsizePathToWidth(g, key, ThemeFonts.Caption, width - 18);

            TextRenderer.DrawText(g, display, ThemeFonts.Caption, new Point(left + 16, top),
                Theme.TextSecondary, GraphicsExtensions.SingleLineFlags);
        }

        private void DrawProgress(Graphics g, int left, int width, Color statusColor)
        {
            int top = 56;

            Rectangle track = new Rectangle(left, top, width, BarHeight);
            using (SolidBrush brush = new SolidBrush(Theme.Blend(Theme.WindowBackground, Theme.Border, 0.5)))
            {
                g.FillRoundedRectangle(brush, track, BarHeight / 2);
            }

            double fraction = _displayedFraction;
            if (fraction > 0)
            {
                int fillWidth = (int)Math.Round(width * fraction);
                if (fillWidth < BarHeight) fillWidth = BarHeight;
                if (fillWidth > width) fillWidth = width;

                Rectangle fill = new Rectangle(left, top, fillWidth, BarHeight);
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(left, top, Math.Max(width, 2), BarHeight),
                    Theme.Blend(statusColor, Theme.AccentBright, 0.22),
                    _item.Status == UploadItemStatus.Uploading
                        ? Theme.Blend(statusColor, Theme.Cyan, 0.30)
                        : statusColor,
                    LinearGradientMode.Horizontal))
                {
                    g.FillRoundedRectangle(brush, fill, BarHeight / 2);
                }
            }

            TextRenderer.DrawText(g, BuildMetricsLine(), ThemeFonts.Caption,
                new Point(left, top + BarHeight + 4), Theme.TextMuted, GraphicsExtensions.SingleLineFlags);
        }

        private string BuildMetricsLine()
        {
            UploadItemStatus status = _item.Status;

            if (status == UploadItemStatus.Failed)
                return Truncate(_item.ErrorMessage ?? "Upload failed.", 160);

            if (status == UploadItemStatus.Completed)
            {
                string etag = string.IsNullOrEmpty(_item.ETag) ? string.Empty : "  ·  ETag " + Truncate(_item.ETag.Trim('"'), 24);
                return FileSizeFormatter.Format(_item.FileSize) + " uploaded and verified" + etag;
            }

            if (status == UploadItemStatus.Skipped) return _item.StatusDetail ?? "Skipped.";

            if (status == UploadItemStatus.Cancelled)
                return string.IsNullOrEmpty(_item.StatusDetail) ? "Cancelled." : _item.StatusDetail;

            if (status == UploadItemStatus.Queued)
            {
                return _item.HasResumableState
                    ? FileSizeFormatter.Format(_item.FileSize) + "  ·  will resume from " + _item.Percent + "%"
                    : FileSizeFormatter.Format(_item.FileSize) + "  ·  waiting";
            }

            if (status == UploadItemStatus.Paused)
            {
                return FileSizeFormatter.FormatProgress(_item.TransferredBytes, _item.FileSize)
                       + "  ·  paused, uploaded parts kept";
            }

            string progress = FileSizeFormatter.FormatProgress(_item.TransferredBytes, _item.FileSize);
            string speed = FileSizeFormatter.FormatSpeed(_item.BytesPerSecond);
            string eta = DurationFormatter.FormatEta(_item.FileSize - _item.TransferredBytes, _item.BytesPerSecond);

            return progress + "  ·  " + _item.Percent + "%  ·  " + speed + "  ·  " + eta + " left";
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength - 1) + "…";
        }

        // -------------------------------------------------------------------------- actions

        private Rectangle GetActionBounds(int index)
        {
            int top = (Height - ActionButtonSize) / 2;
            int right = Width - RowPadding;
            int x = right - ((ActionButtonSize + 4) * (3 - index)) + 4;
            return new Rectangle(x, top, ActionButtonSize, ActionButtonSize);
        }

        private AppIcon GetActionIcon(int index)
        {
            switch (index)
            {
                case 0: return AppIcon.Copy;
                case 1: return AppIcon.Refresh;
                default: return _item != null && _item.Status.IsTerminal() ? AppIcon.Trash : AppIcon.Cross;
            }
        }

        private string GetActionTooltip(int index)
        {
            switch (index)
            {
                case 0: return "Copy the destination object key (Ctrl+C)";
                case 1: return "Retry this file (R)";
                default:
                    return _item != null && _item.Status.IsTerminal()
                        ? "Remove from the queue (Delete)"
                        : "Cancel this upload (Delete)";
            }
        }

        private bool IsActionEnabled(int index)
        {
            if (_item == null) return false;

            switch (index)
            {
                case 0: return !string.IsNullOrEmpty(_item.ObjectKey);
                case 1:
                    return _item.Status == UploadItemStatus.Failed
                        || _item.Status == UploadItemStatus.Cancelled
                        || _item.Status == UploadItemStatus.Skipped;
                default: return true;
            }
        }

        private void DrawActions(Graphics g)
        {
            for (int i = 0; i < 3; i++)
            {
                Rectangle bounds = GetActionBounds(i);
                bool enabled = IsActionEnabled(i);
                bool hovered = enabled && _hoveredAction == i;

                if (hovered)
                {
                    Color hover = i == 2 && _item != null && !_item.Status.IsTerminal()
                        ? Theme.WithAlpha(Theme.Error, 40)
                        : Theme.CardHover;

                    using (SolidBrush brush = new SolidBrush(_pressedAction == i ? Theme.Blend(hover, Color.Black, 0.3) : hover))
                    {
                        g.FillRoundedRectangle(brush, bounds, 7);
                    }
                }

                Color color = !enabled
                    ? Theme.TextDisabled
                    : (hovered ? (i == 2 && !_item.Status.IsTerminal() ? Theme.Error : Theme.TextPrimary) : Theme.TextMuted);

                IconPainter.Draw(g, GetActionIcon(i), Rectangle.Inflate(bounds, -7, -7), color, 1.7f);
            }
        }

        // ---------------------------------------------------------------------------- input

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int hovered = -1;
            for (int i = 0; i < 3; i++)
            {
                if (GetActionBounds(i).Contains(e.Location)) { hovered = i; break; }
            }

            if (hovered != _hoveredAction)
            {
                _hoveredAction = hovered;
                Cursor = hovered >= 0 && IsActionEnabled(hovered) ? Cursors.Hand : Cursors.Default;

                string tip = hovered >= 0 ? GetActionTooltip(hovered) : BuildRowTooltip();
                if (tip != _lastTooltip)
                {
                    _lastTooltip = tip;
                    _toolTip.SetToolTip(this, tip);
                }

                Invalidate();
            }
        }

        private string BuildRowTooltip()
        {
            if (_item == null) return string.Empty;

            string tip = _item.LocalFilePath + Environment.NewLine +
                         "Destination: " + _item.ObjectKey + Environment.NewLine +
                         "Status: " + _item.Status.ToDisplayText();

            if (!string.IsNullOrEmpty(_item.ErrorMessage)) tip += Environment.NewLine + _item.ErrorMessage;
            else if (!string.IsNullOrEmpty(_item.PublicUrl)) tip += Environment.NewLine + _item.PublicUrl;

            return tip;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            _lastTooltip = BuildRowTooltip();
            _toolTip.SetToolTip(this, _lastTooltip);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _hoveredAction = -1;
            _pressedAction = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            if (e.Button != MouseButtons.Left) return;

            for (int i = 0; i < 3; i++)
            {
                if (GetActionBounds(i).Contains(e.Location) && IsActionEnabled(i))
                {
                    _pressedAction = i;
                    Invalidate();
                    return;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            int pressed = _pressedAction;
            _pressedAction = -1;
            Invalidate();

            if (pressed < 0 || e.Button != MouseButtons.Left) return;
            if (!GetActionBounds(pressed).Contains(e.Location)) return;

            InvokeAction(pressed);
        }

        private void InvokeAction(int index)
        {
            if (_item == null || !IsActionEnabled(index)) return;

            switch (index)
            {
                case 0: Raise(CopyKeyRequested); break;
                case 1: Raise(RetryRequested); break;
                default: Raise(RemoveRequested); break;
            }
        }

        private void Raise(EventHandler<QueueItemActionEventArgs> handler)
        {
            if (handler != null) handler(this, new QueueItemActionEventArgs(_item));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Delete || keyData == Keys.R || keyData == (Keys.Control | Keys.C)) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Delete) { InvokeAction(2); e.Handled = true; }
            else if (e.KeyCode == Keys.R && !e.Control) { InvokeAction(1); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.C) { InvokeAction(0); e.Handled = true; }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ------------------------------------------------------------------- bar animation

        private void OnBarTick(object sender, EventArgs e)
        {
            if (_item == null) { _barTimer.Stop(); return; }

            double target = _item.Fraction;
            double difference = target - _displayedFraction;

            if (Math.Abs(difference) <= 0.0008)
            {
                _displayedFraction = target;
                Invalidate();
                _barTimer.Stop();
                return;
            }

            _displayedFraction += difference * 0.3;
            Invalidate();
        }

        private void EnsureBarAnimating()
        {
            if (DesignMode || !IsHandleCreated || !Visible) return;
            if (_item == null) return;

            if (Math.Abs(_item.Fraction - _displayedFraction) <= 0.0008) return;
            if (!_barTimer.Enabled) _barTimer.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            // Rows scrolled out of the viewport must not keep a timer alive.
            if (!Visible) _barTimer.Stop();
            else EnsureBarAnimating();
        }

        private static Color GetStatusColor(UploadItemStatus status)
        {
            switch (status)
            {
                case UploadItemStatus.Completed: return Theme.Success;
                case UploadItemStatus.Failed: return Theme.Error;
                case UploadItemStatus.Cancelled: return Theme.Cancelled;
                case UploadItemStatus.Skipped: return Theme.Warning;
                case UploadItemStatus.Paused: return Theme.Paused;
                case UploadItemStatus.Uploading: return Theme.Accent;
                case UploadItemStatus.Verifying: return Theme.AccentHover;
                case UploadItemStatus.Preparing: return Theme.AccentHover;
                default: return Theme.Queued;
            }
        }

        private static AppIcon GetStatusIcon(UploadItemStatus status)
        {
            switch (status)
            {
                case UploadItemStatus.Completed: return AppIcon.Check;
                case UploadItemStatus.Failed: return AppIcon.Warning;
                case UploadItemStatus.Cancelled: return AppIcon.Cross;
                case UploadItemStatus.Skipped: return AppIcon.Info;
                case UploadItemStatus.Paused: return AppIcon.Pause;
                case UploadItemStatus.Uploading: return AppIcon.UploadArrow;
                case UploadItemStatus.Verifying: return AppIcon.Refresh;
                default: return AppIcon.File;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_item != null) _item.Changed -= OnItemChanged;
                _barTimer.Tick -= OnBarTick;
                _barTimer.Stop();
                _barTimer.Dispose();
                _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new QueueRowAccessibleObject(this);
        }

        private sealed class QueueRowAccessibleObject : ControlAccessibleObject
        {
            public QueueRowAccessibleObject(UploadQueueItemControl owner) : base(owner) { }

            public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }

            public override string Value
            {
                get
                {
                    UploadQueueItemControl row = Owner as UploadQueueItemControl;
                    if (row == null || row.Item == null) return string.Empty;
                    return row.Item.Percent + " percent, " + row.Item.Status.ToDisplayText();
                }
            }
        }
    }
}
