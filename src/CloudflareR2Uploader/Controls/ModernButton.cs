using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Controls
{
    public enum ModernButtonStyle
    {
        /// <summary>Filled violet. For the single primary action on a surface.</summary>
        Primary = 0,

        /// <summary>Dark fill with a border. For everything else.</summary>
        Secondary = 1,

        /// <summary>No fill until hovered. For low-emphasis and icon-only actions.</summary>
        Ghost = 2,

        /// <summary>Red accent. For destructive actions such as cancelling or clearing.</summary>
        Danger = 3
    }

    /// <summary>
    /// An owner-drawn button with rounded corners and a short hover/press fade.
    /// <para>
    /// Designer-friendly: parameterless constructor, browsable properties with sensible
    /// defaults, and the animation timer never runs at design time or while hidden.
    /// </para>
    /// </summary>
    [ToolboxItem(true)]
    [DefaultEvent("Click")]
    public class ModernButton : Control, IButtonControl
    {
        private const int AnimationIntervalMs = 15;
        private const double AnimationStep = 0.18;

        private readonly Timer _animationTimer;

        private ModernButtonStyle _buttonStyle = ModernButtonStyle.Secondary;
        private AppIcon _icon = AppIcon.None;
        private int _cornerRadius = 10;
        private bool _hovered;
        private bool _pressed;
        private bool _keyboardPressed;
        private double _hoverAmount;
        private double _pressAmount;
        private DialogResult _dialogResult = DialogResult.None;
        private bool _isDefault;
        private Color _accentOverride = Color.Empty;

        public ModernButton()
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
            Size = new Size(120, 36);
            Cursor = Cursors.Hand;
            TabStop = true;

            _animationTimer = new Timer { Interval = AnimationIntervalMs };
            _animationTimer.Tick += OnAnimationTick;
        }

        // ------------------------------------------------------------------- designer state

        [Category("Appearance")]
        [DefaultValue(ModernButtonStyle.Secondary)]
        [Description("Visual weight of the button.")]
        public ModernButtonStyle ButtonStyle
        {
            get { return _buttonStyle; }
            set
            {
                if (_buttonStyle == value) return;
                _buttonStyle = value;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [DefaultValue(AppIcon.None)]
        [Description("Vector icon drawn to the left of the text, or on its own when there is no text.")]
        public AppIcon Icon
        {
            get { return _icon; }
            set
            {
                if (_icon == value) return;
                _icon = value;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [DefaultValue(10)]
        public int CornerRadius
        {
            get { return _cornerRadius; }
            set
            {
                int clamped = value < 0 ? 0 : (value > 32 ? 32 : value);
                if (_cornerRadius == clamped) return;
                _cornerRadius = clamped;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [Description("Overrides the accent colour used by the Primary style.")]
        public Color AccentColor
        {
            get { return _accentOverride; }
            set
            {
                if (_accentOverride == value) return;
                _accentOverride = value;
                Invalidate();
            }
        }

        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public override Color BackColor
        {
            get { return base.BackColor; }
            set { base.BackColor = value; }
        }

        // -------------------------------------------------------------------- IButtonControl

        [Category("Behavior")]
        [DefaultValue(DialogResult.None)]
        public DialogResult DialogResult
        {
            get { return _dialogResult; }
            set { _dialogResult = value; }
        }

        public void NotifyDefault(bool value)
        {
            if (_isDefault == value) return;
            _isDefault = value;
            Invalidate();
        }

        public void PerformClick()
        {
            if (!Enabled) return;
            OnClick(EventArgs.Empty);
        }

        // ------------------------------------------------------------------------- painting

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.UseHighQuality();

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            Color fill, border, text;
            ResolveColors(out fill, out border, out text);

            using (GraphicsPath path = GraphicsExtensions.CreateRoundedRectangle(bounds, _cornerRadius))
            {
                if (fill.A > 0)
                {
                    if (_buttonStyle == ModernButtonStyle.Primary && Enabled)
                    {
                        // Match the SabHaven violet CTA: softly dimensional, never glossy.
                        using (LinearGradientBrush brush = new LinearGradientBrush(
                            new Rectangle(0, 0, Width, Height + 1),
                            Theme.Blend(fill, Theme.AccentBright, 0.18),
                            Theme.Blend(fill, Theme.AccentPressed, 0.18),
                            LinearGradientMode.ForwardDiagonal))
                        {
                            g.FillPath(brush, path);
                        }
                    }
                    else
                    {
                        using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                    }
                }

                if (border.A > 0)
                {
                    using (Pen pen = new Pen(border, 1f)) g.DrawPath(pen, path);
                }

                if (_buttonStyle == ModernButtonStyle.Primary && Enabled)
                {
                    Rectangle highlight = Rectangle.Inflate(bounds, -1, -1);
                    using (Pen pen = new Pen(Theme.WithAlpha(Color.White, 38), 1f))
                    {
                        g.DrawArc(pen, highlight.X + 3, highlight.Y, Math.Max(1, highlight.Width - 6), 7, 188, 164);
                    }
                }
            }

            DrawContent(g, text);

            if (Focused && TabStop && ShowFocusCues) DrawFocusRing(g, bounds);
        }

        private void ResolveColors(out Color fill, out Color border, out Color text)
        {
            double hover = _hoverAmount;
            double press = _pressAmount;

            if (!Enabled)
            {
                switch (_buttonStyle)
                {
                    case ModernButtonStyle.Primary:
                        fill = Theme.Blend(Theme.CardBackgroundAlt, Theme.AccentSoft, 0.35);
                        break;
                    case ModernButtonStyle.Ghost:
                        fill = Color.Transparent;
                        break;
                    default:
                        fill = Theme.CardBackground;
                        break;
                }
                border = _buttonStyle == ModernButtonStyle.Ghost ? Color.Transparent : Theme.Divider;
                text = Theme.TextDisabled;
                return;
            }

            Color accent = _accentOverride.IsEmpty ? Theme.Accent : _accentOverride;

            switch (_buttonStyle)
            {
                case ModernButtonStyle.Primary:
                    fill = Theme.Blend(accent, Theme.AccentHover, hover);
                    fill = Theme.Blend(fill, Theme.AccentPressed, press);
                    border = Theme.Blend(accent, Theme.AccentBright, 0.34 + (hover * 0.42));
                    text = Theme.TextOnAccent;
                    break;

                case ModernButtonStyle.Danger:
                    fill = Theme.Blend(Theme.ElevatedBackground, Theme.WithAlpha(Theme.Error, 60), hover);
                    fill = Theme.Blend(fill, Theme.WithAlpha(Theme.Error, 90), press);
                    border = Theme.Blend(Theme.Border, Theme.Error, 0.35 + (hover * 0.45));
                    text = Theme.Error;
                    break;

                case ModernButtonStyle.Ghost:
                    fill = Theme.Blend(Color.FromArgb(0, Theme.CardHover), Theme.CardHover, hover);
                    fill = Theme.Blend(fill, Theme.CardBackgroundAlt, press);
                    border = Color.Transparent;
                    text = Theme.Blend(Theme.TextSecondary, Theme.TextPrimary, hover);
                    break;

                default:
                    fill = Theme.Blend(Theme.ElevatedBackground, Theme.CardHover, hover);
                    fill = Theme.Blend(fill, Theme.CardBackground, press);
                    border = Theme.Blend(Theme.Border, Theme.BorderStrong, hover);
                    if (_isDefault) border = Theme.Blend(border, Theme.Accent, 0.5);
                    text = Theme.TextPrimary;
                    break;
            }
        }

        private void DrawContent(Graphics g, Color textColor)
        {
            bool hasText = !string.IsNullOrEmpty(Text);
            bool hasIcon = _icon != AppIcon.None;

            // Pressing nudges the content down by a pixel: cheap, convincing feedback.
            int pressOffset = _pressAmount > 0.5 ? 1 : 0;

            int iconSize = Math.Min(18, Math.Max(12, Height - 14));

            if (hasIcon && !hasText)
            {
                Rectangle iconBounds = new Rectangle(
                    (Width - iconSize) / 2,
                    ((Height - iconSize) / 2) + pressOffset,
                    iconSize, iconSize);
                IconPainter.Draw(g, _icon, iconBounds, textColor, 1.9f);
                return;
            }

            if (!hasText) return;

            Size textSize = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, int.MaxValue),
                GraphicsExtensions.SingleLineFlags);

            int gap = hasIcon ? 8 : 0;
            int contentWidth = textSize.Width + (hasIcon ? iconSize + gap : 0);
            int left = Math.Max(6, (Width - contentWidth) / 2);

            if (hasIcon)
            {
                Rectangle iconBounds = new Rectangle(
                    left, ((Height - iconSize) / 2) + pressOffset, iconSize, iconSize);
                IconPainter.Draw(g, _icon, iconBounds, textColor, 1.9f);
                left += iconSize + gap;
            }

            Rectangle textBounds = new Rectangle(
                left,
                ((Height - textSize.Height) / 2) + pressOffset,
                Math.Min(textSize.Width, Width - left - 4),
                textSize.Height);

            TextRenderer.DrawText(g, Text, Font, textBounds, textColor, GraphicsExtensions.SingleLineFlags);
        }

        private void DrawFocusRing(Graphics g, Rectangle bounds)
        {
            Rectangle ring = Rectangle.Inflate(bounds, -2, -2);
            if (ring.Width <= 0 || ring.Height <= 0) return;

            using (Pen pen = new Pen(Theme.Blend(Theme.Accent, Color.White, 0.35), 1.4f))
            {
                pen.DashStyle = DashStyle.Dot;
                g.DrawRoundedRectangle(pen, ring, Math.Max(0, _cornerRadius - 2));
            }
        }

        // ------------------------------------------------------------------------ animation

        private void OnAnimationTick(object sender, EventArgs e)
        {
            double hoverTarget = _hovered && Enabled ? 1.0 : 0.0;
            double pressTarget = (_pressed || _keyboardPressed) && Enabled ? 1.0 : 0.0;

            bool changed = Advance(ref _hoverAmount, hoverTarget) | Advance(ref _pressAmount, pressTarget);

            if (changed) Invalidate();
            else _animationTimer.Stop();     // never burn CPU once the button has settled
        }

        private static bool Advance(ref double current, double target)
        {
            if (Math.Abs(current - target) < 0.01)
            {
                if (current == target) return false;
                current = target;
                return true;
            }

            current += (current < target ? AnimationStep : -AnimationStep);
            if (current < 0) current = 0;
            if (current > 1) current = 1;
            return true;
        }

        private void StartAnimation()
        {
            // DesignMode keeps the designer surface completely static.
            if (DesignMode || !IsHandleCreated || !Visible) return;
            if (!_animationTimer.Enabled) _animationTimer.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible)
            {
                _animationTimer.Stop();
                _hoverAmount = 0;
                _pressAmount = 0;
                _hovered = false;
                _pressed = false;
            }
        }

        // --------------------------------------------------------------------------- input

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            StartAnimation();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            StartAnimation();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            _pressed = true;
            if (TabStop) Focus();
            StartAnimation();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            StartAnimation();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            if (!Enabled)
            {
                _hovered = false;
                _pressed = false;
                _hoverAmount = 0;
                _pressAmount = 0;
            }
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _keyboardPressed = false;
            StartAnimation();
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                _keyboardPressed = true;
                StartAnimation();
                e.Handled = true;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (!_keyboardPressed) return;
            if (e.KeyCode != Keys.Space && e.KeyCode != Keys.Enter) return;

            _keyboardPressed = false;
            StartAnimation();
            e.Handled = true;
            PerformClick();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);

            // Close the owning dialog when a DialogResult is set, matching Button's behaviour.
            if (_dialogResult == DialogResult.None) return;

            Form form = FindForm();
            if (form != null)
            {
                form.DialogResult = _dialogResult;
            }
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animationTimer.Tick -= OnAnimationTick;
                _animationTimer.Stop();
                _animationTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ModernButtonAccessibleObject(this);
        }

        private sealed class ModernButtonAccessibleObject : ControlAccessibleObject
        {
            public ModernButtonAccessibleObject(ModernButton owner) : base(owner) { }

            public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }

            public override string DefaultAction { get { return "Press"; } }

            public override void DoDefaultAction()
            {
                ModernButton button = Owner as ModernButton;
                if (button != null) button.PerformClick();
            }
        }
    }
}
