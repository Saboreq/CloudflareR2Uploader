using System.Drawing;
using System.Windows.Forms;
using CloudflareR2Uploader.Theming;

namespace CloudflareR2Uploader.Controls
{
    /// <summary>Dark renderer shared by object-browser context menus.</summary>
    public sealed class DarkContextMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkContextMenuRenderer() : base(new DarkColorTable())
        {
            RoundedEdges = true;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item.Enabled && e.Item.ForeColor == SystemColors.ControlText)
                e.TextColor = Theme.TextPrimary;
            else if (!e.Item.Enabled)
                e.TextColor = Theme.TextDisabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Divider))
            {
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
            }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.ElevatedBackground; } }
            public override Color MenuBorder { get { return Theme.BorderStrong; } }
            public override Color MenuItemBorder { get { return Theme.Accent; } }
            public override Color MenuItemSelected { get { return Theme.AccentSoft; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.AccentSoft; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.AccentSoft; } }
            public override Color ImageMarginGradientBegin { get { return Theme.ElevatedBackground; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.ElevatedBackground; } }
            public override Color ImageMarginGradientEnd { get { return Theme.ElevatedBackground; } }
            public override Color SeparatorDark { get { return Theme.Divider; } }
            public override Color SeparatorLight { get { return Theme.Divider; } }
        }
    }
}
