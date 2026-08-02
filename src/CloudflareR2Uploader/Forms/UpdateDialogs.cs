using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Forms
{
    internal sealed class UpdateAvailableDialog : Form
    {
        public UpdateAvailableDialog(string currentVersion, UpdateManifest manifest)
        {
            Text = "Update available";
            Size = new Size(640, 470);
            MinimumSize = new Size(560, 400);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.WindowBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;
            Icon = Program.LoadApplicationIcon();
            Label heading = new Label { Dock = DockStyle.Top, Height = 72, Padding = new Padding(22, 20, 22, 0), Text = "Cloudflare R2 Uploader " + manifest.Version + " is available", Font = ThemeFonts.Title, ForeColor = Theme.TextPrimary };
            Label versions = new Label { Dock = DockStyle.Top, Height = 42, Padding = new Padding(22, 4, 22, 0), Text = "Installed: " + currentVersion + "   ·   Download: " + FileSizeFormatter.Format(manifest.SizeBytes), ForeColor = Theme.TextSecondary };
            Label notesLabel = new Label { Dock = DockStyle.Top, Height = 28, Padding = new Padding(22, 4, 22, 0), Text = "WHAT'S NEW", ForeColor = Theme.AccentBright, Font = ThemeFonts.BodyBold };
            TextBox notes = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = string.IsNullOrWhiteSpace(manifest.Notes) ? "No release notes were provided." : manifest.Notes, BackColor = Theme.InputBackground, ForeColor = Theme.TextPrimary, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(22) };
            Label assurance = new Label { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(22, 8, 22, 0), Text = "The installer will be downloaded over HTTPS and its size and SHA-256 verified before it runs.", ForeColor = Theme.TextSecondary };
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12) };
            Button update = NewButton("Update now", true); update.DialogResult = DialogResult.Yes;
            Button later = NewButton("Not now", false); later.DialogResult = DialogResult.No;
            buttons.Controls.Add(update); buttons.Controls.Add(later);
            AcceptButton = update;
            CancelButton = later;
            Controls.Add(notes); Controls.Add(assurance); Controls.Add(buttons); Controls.Add(notesLabel); Controls.Add(versions); Controls.Add(heading);
        }

        private static Button NewButton(string text, bool accent)
        {
            Button button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(112, 36), FlatStyle = FlatStyle.Flat, BackColor = accent ? Theme.AccentPressed : Theme.ElevatedBackground, ForeColor = Theme.TextPrimary };
            button.FlatAppearance.BorderColor = accent ? Theme.Accent : Theme.BorderStrong;
            return button;
        }
    }

    internal sealed class UpdateDownloadDialog : Form
    {
        private readonly Label _detail;
        private readonly ProgressBar _progress;

        public UpdateDownloadDialog(CancellationTokenSource cancellation)
        {
            Text = "Downloading update";
            Size = new Size(560, 220);
            StartPosition = FormStartPosition.CenterParent;
            ControlBox = false;
            BackColor = Theme.WindowBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;
            Icon = Program.LoadApplicationIcon();
            Label heading = new Label { Dock = DockStyle.Top, Height = 62, Padding = new Padding(20, 20, 20, 0), Text = "Downloading verified installer...", Font = ThemeFonts.BodyBold };
            _detail = new Label { Dock = DockStyle.Top, Height = 42, Padding = new Padding(20, 6, 20, 0), ForeColor = Theme.TextSecondary, Text = "Connecting securely..." };
            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 18, Style = ProgressBarStyle.Marquee };
            Button cancel = new Button { Dock = DockStyle.Bottom, Height = 42, Text = "Cancel", FlatStyle = FlatStyle.Flat, BackColor = Theme.ElevatedBackground, ForeColor = Theme.TextPrimary };
            cancel.Click += (s, e) => { cancel.Enabled = false; cancel.Text = "Cancelling..."; cancellation.Cancel(); };
            Controls.Add(cancel); Controls.Add(_progress); Controls.Add(_detail); Controls.Add(heading);
        }

        public void Report(UpdateDownloadProgress value)
        {
            if (value == null) return;
            _detail.Text = FileSizeFormatter.Format(value.BytesReceived) + " / " + FileSizeFormatter.Format(value.TotalBytes);
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = Math.Max(0, Math.Min(100, value.Percentage));
        }
    }
}
