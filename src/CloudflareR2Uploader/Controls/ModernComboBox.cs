using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Controls
{
    /// <summary>
    /// A drop-down list styled for the dark theme. It subclasses <see cref="ComboBox"/> rather
    /// than reimplementing one, so keyboard navigation, type-ahead and screen-reader support
    /// come from the platform.
    /// </summary>
    [ToolboxItem(true)]
    public class ModernComboBox : ComboBox
    {
        private bool _hovered;

        public ModernComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.InputBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;
            ItemHeight = 22;
            DropDown += delegate { DarkScrollBars.ApplyComboDropDown(this); };
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                base.OnDrawItem(e);
                return;
            }

            Graphics g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

            Color background = selected ? Theme.AccentSoft : Theme.InputBackground;
            Color foreground = Enabled ? Theme.TextPrimary : Theme.TextDisabled;

            using (SolidBrush brush = new SolidBrush(background)) g.FillRectangle(brush, e.Bounds);

            if (selected)
            {
                // A left accent bar makes the selection legible without relying on colour alone.
                using (SolidBrush brush = new SolidBrush(Theme.Accent))
                {
                    g.FillRectangle(brush, new Rectangle(e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height));
                }
            }

            string text = GetItemText(Items[e.Index]);
            Rectangle textBounds = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);

            TextRenderer.DrawText(
                g, text, Font, textBounds, foreground,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.UseHighQuality();

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            Color border = !Enabled
                ? Theme.Divider
                : (Focused ? Theme.Accent : (_hovered ? Theme.BorderStrong : Theme.Border));

            using (SolidBrush brush = new SolidBrush(Enabled ? Theme.InputBackground : Theme.CardBackground))
            {
                g.FillRoundedRectangle(brush, bounds, 10);
            }

            using (Pen pen = new Pen(border, Focused ? 1.4f : 1f))
            {
                g.DrawRoundedRectangle(pen, bounds, 10);
            }

            string text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : Text;
            Rectangle textBounds = new Rectangle(9, 0, Width - 34, Height);

            if (!string.IsNullOrEmpty(text))
            {
                string display = GraphicsExtensions.EllipsizeToWidth(g, text, Font, textBounds.Width);
                TextRenderer.DrawText(
                    g, display, Font, textBounds,
                    Enabled ? Theme.TextPrimary : Theme.TextDisabled,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            Rectangle chevron = new Rectangle(Width - 26, (Height - 16) / 2, 16, 16);
            IconPainter.Draw(g, AppIcon.ChevronDown, chevron,
                Enabled ? Theme.TextSecondary : Theme.TextDisabled, 1.8f);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Repaint the whole control, not just the invalidated slice, so the rounded frame
            // is never left half-drawn after the list closes.
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            DarkScrollBars.Apply(this);
        }
    }
}
