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
    /// A dark, rounded single-line text field. A real <see cref="TextBox"/> is hosted inside so
    /// selection, IME, clipboard and accessibility all behave exactly as Windows users expect;
    /// only the frame is custom-drawn.
    /// <para>
    /// When <see cref="UseSystemPasswordChar"/> is set, an eye button reveals the value while
    /// it is held or toggled. The value itself is never logged or copied anywhere by this control.
    /// </para>
    /// </summary>
    [ToolboxItem(true)]
    [DefaultProperty("Text")]
    [DefaultEvent("TextChanged")]
    public class ModernTextBox : Control
    {
        private readonly TextBox _inner;

        private string _placeholder = string.Empty;
        private bool _showRevealButton;
        private bool _revealed;
        private bool _revealHovered;
        private int _cornerRadius = 10;
        private bool _focusedVisual;
        private bool _readOnlyVisual;

        public ModernTextBox()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.ContainerControl, true);

            BackColor = Theme.InputBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;
            Size = new Size(240, 34);
            Padding = new Padding(10, 0, 10, 0);

            _inner = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.InputBackground,
                ForeColor = Theme.TextPrimary,
                Font = ThemeFonts.Body,
                AutoSize = false
            };

            _inner.GotFocus += delegate { _focusedVisual = true; Invalidate(); };
            _inner.LostFocus += delegate { _focusedVisual = false; Invalidate(); };
            _inner.TextChanged += delegate { OnTextChanged(EventArgs.Empty); Invalidate(); };
            _inner.KeyDown += (s, e) => OnKeyDown(e);
            _inner.KeyPress += (s, e) => OnKeyPress(e);
            _inner.KeyUp += (s, e) => OnKeyUp(e);

            Controls.Add(_inner);
        }

        /// <summary>The hosted text box, for callers that need to set MaxLength, CharacterCasing, etc.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public TextBox InnerTextBox { get { return _inner; } }

        [Category("Appearance")]
        [DefaultValue("")]
        [Description("Grey hint shown while the field is empty.")]
        public string PlaceholderText
        {
            get { return _placeholder; }
            set
            {
                string text = value ?? string.Empty;
                if (_placeholder == text) return;
                _placeholder = text;
                Invalidate();
            }
        }

        [Category("Behavior")]
        [DefaultValue(false)]
        [Description("Masks the value and shows a temporary show/hide button.")]
        public bool UseSystemPasswordChar
        {
            get { return _inner.UseSystemPasswordChar; }
            set
            {
                _inner.UseSystemPasswordChar = value;
                ShowRevealButton = value;
                if (!value) _revealed = false;
                PerformLayout();
                Invalidate();
            }
        }

        [Category("Behavior")]
        [DefaultValue(false)]
        public bool ShowRevealButton
        {
            get { return _showRevealButton; }
            set
            {
                if (_showRevealButton == value) return;
                _showRevealButton = value;
                PerformLayout();
                Invalidate();
            }
        }

        [Category("Behavior")]
        [DefaultValue(false)]
        public bool ReadOnly
        {
            get { return _inner.ReadOnly; }
            set
            {
                _inner.ReadOnly = value;
                _readOnlyVisual = value;
                _inner.BackColor = value ? Theme.CardBackground : Theme.InputBackground;
                BackColor = _inner.BackColor;
                Invalidate();
            }
        }

        [Category("Behavior")]
        [DefaultValue(32767)]
        public int MaxLength
        {
            get { return _inner.MaxLength; }
            set { _inner.MaxLength = value; }
        }

        [Category("Appearance")]
        [DefaultValue(10)]
        public int CornerRadius
        {
            get { return _cornerRadius; }
            set
            {
                int clamped = value < 0 ? 0 : (value > 24 ? 24 : value);
                if (_cornerRadius == clamped) return;
                _cornerRadius = clamped;
                Invalidate();
            }
        }

        [Browsable(true)]
        [EditorBrowsable(EditorBrowsableState.Always)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public override string Text
        {
            get { return _inner.Text; }
            set
            {
                if (_inner.Text == value) return;
                _inner.Text = value ?? string.Empty;
                Invalidate();
            }
        }

        public void SelectAll() { _inner.SelectAll(); }

        public new void Focus() { _inner.Focus(); }

        // ------------------------------------------------------------------------- layout

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            // Font, padding and size assignments in the constructor can trigger layout before
            // the hosted TextBox has been initialized.
            if (_inner == null) return;

            int buttonWidth = _showRevealButton ? Height - 8 : 0;
            int textHeight = Math.Max(_inner.PreferredHeight, Font.Height + 2);

            int left = Padding.Left;
            int width = Width - Padding.Left - Padding.Right - buttonWidth;
            if (width < 10) width = 10;

            _inner.SetBounds(left, Math.Max(1, (Height - textHeight) / 2), width, textHeight);
        }

        private Rectangle GetRevealButtonBounds()
        {
            if (!_showRevealButton) return Rectangle.Empty;
            int size = Height - 8;
            return new Rectangle(Width - size - 4, 4, size, size);
        }

        // ------------------------------------------------------------------------ painting

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.UseHighQuality();

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            Color fill = _readOnlyVisual ? Theme.CardBackground : Theme.InputBackground;
            Color border = !Enabled
                ? Theme.Divider
                : (_focusedVisual ? Theme.Accent : Theme.Border);

            using (GraphicsPath path = GraphicsExtensions.CreateRoundedRectangle(bounds, _cornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);

                if (_focusedVisual)
                {
                    // A soft ring instead of a hard system focus rectangle.
                    using (Pen glow = new Pen(Theme.WithAlpha(Theme.Accent, 70), 2f))
                    {
                        g.DrawRoundedRectangle(glow, Rectangle.Inflate(bounds, -1, -1), Math.Max(0, _cornerRadius - 1));
                    }
                }

                using (Pen pen = new Pen(border, _focusedVisual ? 1.4f : 1f)) g.DrawPath(pen, path);
            }

            if (string.IsNullOrEmpty(_inner.Text) && !string.IsNullOrEmpty(_placeholder))
            {
                Rectangle textArea = new Rectangle(
                    _inner.Left, _inner.Top, Math.Max(10, _inner.Width), Math.Max(10, _inner.Height));

                TextRenderer.DrawText(
                    g, _placeholder, Font, textArea, Theme.TextMuted,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            if (_showRevealButton) DrawRevealButton(g);
        }

        private void DrawRevealButton(Graphics g)
        {
            Rectangle button = GetRevealButtonBounds();
            if (button.Width <= 0) return;

            if (_revealHovered)
            {
                using (SolidBrush brush = new SolidBrush(Theme.CardHover))
                {
                    g.FillRoundedRectangle(brush, button, 6);
                }
            }

            Color color = _revealHovered ? Theme.TextPrimary : Theme.TextMuted;
            Rectangle iconBounds = Rectangle.Inflate(button, -5, -5);
            IconPainter.Draw(g, _revealed ? AppIcon.EyeOff : AppIcon.Eye, iconBounds, color, 1.7f);
        }

        // --------------------------------------------------------------------------- input

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool hovered = _showRevealButton && GetRevealButtonBounds().Contains(e.Location);
            if (hovered == _revealHovered) return;

            _revealHovered = hovered;
            Cursor = hovered ? Cursors.Hand : Cursors.IBeam;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_revealHovered) return;
            _revealHovered = false;
            Cursor = Cursors.IBeam;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (_showRevealButton && GetRevealButtonBounds().Contains(e.Location))
            {
                ToggleReveal();
                return;
            }

            _inner.Focus();
        }

        /// <summary>Flips the mask. Callers use this for a keyboard-accessible reveal too.</summary>
        public void ToggleReveal()
        {
            _revealed = !_revealed;
            _inner.UseSystemPasswordChar = !_revealed;

            AccessibleDescription = _revealed
                ? "The secret is currently visible."
                : "The secret is currently hidden.";

            Invalidate();
        }

        /// <summary>Re-masks the value. Called when a dialog closes so a secret is never left visible.</summary>
        public void HideSecret()
        {
            if (!_showRevealButton || !_revealed) return;
            _revealed = false;
            _inner.UseSystemPasswordChar = true;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (_inner == null) return;
            _inner.Enabled = Enabled;
            _inner.ForeColor = Enabled ? Theme.TextPrimary : Theme.TextDisabled;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (_inner != null) _inner.Focus();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (_inner == null) return;
            _inner.Font = Font;
            PerformLayout();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            PerformLayout();
        }
    }
}
