using System;
using System.Drawing;
using System.Windows.Forms;
using CloudflareR2Uploader.Controls;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Forms
{
    internal sealed class R2NameDialog : Form
    {
        private readonly ModernTextBox _nameTextBox;
        private readonly Label _validationLabel;

        public R2NameDialog(
            string title,
            string headline,
            string detail,
            string initialName,
            string actionText)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 240);
            MinimumSize = new Size(500, 270);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BackColor = Theme.WindowBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;
            Icon = Program.LoadApplicationIcon();

            Label heading = new Label
            {
                AutoSize = false,
                Location = new Point(24, 22),
                Size = new Size(512, 28),
                Text = headline,
                Font = ThemeFonts.Title,
                ForeColor = Theme.TextPrimary
            };
            Label description = new Label
            {
                AutoSize = false,
                Location = new Point(24, 58),
                Size = new Size(512, 38),
                Text = detail,
                Font = ThemeFonts.BodySmall,
                ForeColor = Theme.TextSecondary
            };
            _nameTextBox = new ModernTextBox
            {
                Location = new Point(24, 106),
                Size = new Size(512, 38),
                Text = initialName ?? string.Empty,
                AccessibleName = "File or folder name"
            };
            _validationLabel = new Label
            {
                AutoSize = false,
                Location = new Point(24, 148),
                Size = new Size(512, 24),
                ForeColor = Theme.Error,
                Font = ThemeFonts.Caption
            };
            ModernButton cancel = new ModernButton
            {
                Location = new Point(322, 184),
                Size = new Size(100, 36),
                Text = "Cancel",
                ButtonStyle = ModernButtonStyle.Secondary,
                DialogResult = DialogResult.Cancel
            };
            ModernButton action = new ModernButton
            {
                Location = new Point(430, 184),
                Size = new Size(106, 36),
                Text = actionText,
                ButtonStyle = ModernButtonStyle.Primary
            };
            action.Click += ValidateAndClose;

            Controls.Add(heading);
            Controls.Add(description);
            Controls.Add(_nameTextBox);
            Controls.Add(_validationLabel);
            Controls.Add(cancel);
            Controls.Add(action);
            AcceptButton = action;
            CancelButton = cancel;
        }

        public string EntryName { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DarkTitleBar.Apply(this);
            _nameTextBox.Focus();
            _nameTextBox.SelectAll();
        }

        private void ValidateAndClose(object sender, EventArgs e)
        {
            string value = _nameTextBox.Text ?? string.Empty;
            string reason;
            if (!R2ObjectOperationPathUtility.TryValidateLeafName(value, out reason))
            {
                _validationLabel.Text = reason;
                return;
            }

            EntryName = value;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class R2DestinationDialog : Form
    {
        private readonly bool _isFolder;
        private readonly ModernTextBox _destinationTextBox;
        private readonly Label _validationLabel;

        public R2DestinationDialog(bool isFolder, string currentDestination)
        {
            _isFolder = isFolder;
            Text = isFolder ? "Move virtual folder" : "Move object";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 240);
            MinimumSize = new Size(500, 270);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BackColor = Theme.WindowBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;
            Icon = Program.LoadApplicationIcon();

            Label heading = new Label
            {
                AutoSize = false,
                Location = new Point(24, 22),
                Size = new Size(512, 28),
                Text = isFolder ? "Move this virtual folder" : "Move this object",
                Font = ThemeFonts.Title,
                ForeColor = Theme.TextPrimary
            };
            Label detail = new Label
            {
                AutoSize = false,
                Location = new Point(24, 58),
                Size = new Size(512, 38),
                Text = isFolder
                    ? "Enter the full destination prefix. Moving a folder copies all matching objects, then removes the originals."
                    : "Enter the full destination object key. Use / between virtual folders.",
                Font = ThemeFonts.BodySmall,
                ForeColor = Theme.TextSecondary
            };
            _destinationTextBox = new ModernTextBox
            {
                Location = new Point(24, 106),
                Size = new Size(512, 38),
                Text = currentDestination ?? string.Empty,
                AccessibleName = "Destination object key"
            };
            _validationLabel = new Label
            {
                AutoSize = false,
                Location = new Point(24, 148),
                Size = new Size(512, 24),
                ForeColor = Theme.Error,
                Font = ThemeFonts.Caption
            };
            ModernButton cancel = new ModernButton
            {
                Location = new Point(322, 184),
                Size = new Size(100, 36),
                Text = "Cancel",
                ButtonStyle = ModernButtonStyle.Secondary,
                DialogResult = DialogResult.Cancel
            };
            ModernButton move = new ModernButton
            {
                Location = new Point(430, 184),
                Size = new Size(106, 36),
                Text = "Move",
                ButtonStyle = ModernButtonStyle.Primary
            };
            move.Click += ValidateAndClose;

            Controls.Add(heading);
            Controls.Add(detail);
            Controls.Add(_destinationTextBox);
            Controls.Add(_validationLabel);
            Controls.Add(cancel);
            Controls.Add(move);
            CancelButton = cancel;
        }

        public string Destination { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DarkTitleBar.Apply(this);
            _destinationTextBox.Focus();
            _destinationTextBox.SelectAll();
        }

        private void ValidateAndClose(object sender, EventArgs e)
        {
            string normalized = _isFolder
                ? R2BrowserPathUtility.NormalizePrefix(_destinationTextBox.Text)
                : (_destinationTextBox.Text ?? string.Empty);

            string body = _isFolder ? normalized.TrimEnd('/') : normalized;
            string reason = null;
            if (body.Length == 0 || !ObjectKeyUtility.TryValidate(body, out reason))
            {
                _validationLabel.Text = body.Length == 0 ? "Enter a destination." : reason;
                return;
            }

            Destination = normalized;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class R2ConfirmationDialog : Form
    {
        private R2ConfirmationDialog(string title, string headline, string detail, string actionText)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 230);
            MinimumSize = new Size(500, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BackColor = Theme.WindowBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;
            Icon = Program.LoadApplicationIcon();

            Label heading = new Label
            {
                AutoSize = false,
                Location = new Point(24, 24),
                Size = new Size(512, 30),
                Text = headline,
                Font = ThemeFonts.Title,
                ForeColor = Theme.TextPrimary
            };
            Label message = new Label
            {
                AutoSize = false,
                Location = new Point(24, 64),
                Size = new Size(512, 90),
                Text = detail,
                Font = ThemeFonts.Body,
                ForeColor = Theme.TextSecondary
            };
            ModernButton cancel = new ModernButton
            {
                Location = new Point(316, 174),
                Size = new Size(106, 36),
                Text = "Cancel",
                ButtonStyle = ModernButtonStyle.Secondary,
                DialogResult = DialogResult.Cancel
            };
            ModernButton action = new ModernButton
            {
                Location = new Point(430, 174),
                Size = new Size(106, 36),
                Text = actionText,
                ButtonStyle = ModernButtonStyle.Danger,
                DialogResult = DialogResult.OK
            };

            Controls.Add(heading);
            Controls.Add(message);
            Controls.Add(cancel);
            Controls.Add(action);
            CancelButton = cancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DarkTitleBar.Apply(this);
        }

        public static bool Confirm(
            IWin32Window owner,
            string title,
            string headline,
            string detail,
            string actionText)
        {
            using (R2ConfirmationDialog dialog = new R2ConfirmationDialog(
                title, headline, detail, actionText))
            {
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }
}
