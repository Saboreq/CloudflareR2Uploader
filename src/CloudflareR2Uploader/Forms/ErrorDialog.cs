using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CloudflareR2Uploader.Theming;

namespace CloudflareR2Uploader.Forms
{
    /// <summary>
    /// Shows a friendly error message. The stack trace is never in the visible message; the
    /// sanitised technical block is available behind "Show details" and "Copy technical details".
    /// </summary>
    public partial class ErrorDialog : Form
    {
        private readonly string _technicalDetails;
        private bool _detailsVisible;
        private int _collapsedHeight;

        public ErrorDialog(string headline, string message, string technicalDetails)
        {
            InitializeComponent();

            _technicalDetails = technicalDetails ?? string.Empty;

            headlineLabel.Text = headline ?? "Error";
            messageLabel.Text = message ?? string.Empty;
            detailsTextBox.Text = _technicalDetails.Replace("\n", Environment.NewLine);

            Text = "Cloudflare R2 Uploader";
            Icon = Program.LoadApplicationIcon();

            ApplyTheme();
            DarkScrollBars.Apply(detailsTextBox);

            copyButton.Visible = _technicalDetails.Length > 0;
            detailsButton.Visible = _technicalDetails.Length > 0;

            detailsButton.Click += OnToggleDetails;
            copyButton.Click += OnCopyDetails;
        }

        private void ApplyTheme()
        {
            BackColor = Theme.WindowBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;

            rootLayout.BackColor = Color.Transparent;
            buttonPanel.BackColor = Color.Transparent;

            headlineLabel.Font = ThemeFonts.Title;
            headlineLabel.ForeColor = Theme.TextPrimary;

            messageLabel.Font = ThemeFonts.Body;
            messageLabel.ForeColor = Theme.TextSecondary;

            detailsTextBox.BackColor = Theme.InputBackground;
            detailsTextBox.ForeColor = Theme.TextSecondary;
            detailsTextBox.Font = ThemeFonts.Mono;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DarkTitleBar.Apply(this);
            _collapsedHeight = Height;
            closeButton.Focus();
        }

        private void OnToggleDetails(object sender, EventArgs e)
        {
            _detailsVisible = !_detailsVisible;

            detailsTextBox.Visible = _detailsVisible;
            detailsButton.Text = _detailsVisible ? "Hide details" : "Show details";

            // Grow the dialog rather than squeezing the message when details appear.
            if (_detailsVisible)
            {
                if (_collapsedHeight <= 0) _collapsedHeight = Height;
                Height = Math.Min(_collapsedHeight + 230, Screen.FromControl(this).WorkingArea.Height - 40);
            }
            else if (_collapsedHeight > 0)
            {
                Height = _collapsedHeight;
            }
        }

        private void OnCopyDetails(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_technicalDetails)) return;

            try
            {
                Clipboard.SetText(_technicalDetails);
                copyButton.Text = "Copied";
                copyButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Check;
            }
            catch (ExternalException)
            {
                // Another process owns the clipboard; nothing useful to do but say so.
                copyButton.Text = "Clipboard busy";
            }
        }

        /// <summary>Convenience helper used from anywhere an error has to be shown.</summary>
        public static void Show(IWin32Window owner, string headline, string message, string technicalDetails)
        {
            using (ErrorDialog dialog = new ErrorDialog(headline, message, technicalDetails))
            {
                if (owner != null) dialog.ShowDialog(owner);
                else dialog.ShowDialog();
            }
        }
    }
}
