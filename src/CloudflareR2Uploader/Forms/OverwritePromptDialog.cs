using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Forms
{
    /// <summary>
    /// Asked once per conflicting key when the overwrite behaviour is "Ask". The user can
    /// apply the same answer to the rest of the queue so a large folder is not death by prompt.
    /// </summary>
    public partial class OverwritePromptDialog : Form
    {
        public OverwritePromptDialog(UploadQueueItem item, ExistingObjectInfo existing)
        {
            InitializeComponent();

            Icon = Program.LoadApplicationIcon();
            ApplyTheme();

            headlineLabel.Text = "An object with this key already exists in the bucket";
            keyLabel.Text = existing != null ? existing.Key : (item != null ? item.ObjectKey : string.Empty);

            comparisonLabel.Text = BuildComparison(item, existing);

            overwriteButton.Click += delegate { Complete(OverwriteDecision.Overwrite); };
            renameButton.Click += delegate { Complete(OverwriteDecision.Rename); };
            skipButton.Click += delegate { Complete(OverwriteDecision.Skip); };
            cancelAllButton.Click += delegate { Complete(OverwriteDecision.CancelAll); };

            AcceptButton = overwriteButton;

            renameButton.AccessibleDescription = "Upload under a new key such as name (1).ext, keeping the existing object.";
            skipButton.AccessibleDescription = "Leave the existing object and mark this file as skipped.";
        }

        /// <summary>The user's answer.</summary>
        public OverwriteDecision Decision { get; private set; }

        /// <summary>True when the answer should be reused for the rest of the queue.</summary>
        public bool ApplyToAll { get { return applyToAllCheckBox.Checked; } }

        private static string BuildComparison(UploadQueueItem item, ExistingObjectInfo existing)
        {
            if (item == null || existing == null) return string.Empty;

            string localLine = string.Format(
                CultureInfo.CurrentCulture,
                "Local file:      {0}, modified {1}",
                FileSizeFormatter.Format(item.FileSize),
                item.LastWriteUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

            string remoteLine = string.Format(
                CultureInfo.CurrentCulture,
                "In the bucket:  {0}, modified {1}",
                FileSizeFormatter.Format(existing.Length),
                existing.LastModifiedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

            return localLine + Environment.NewLine + remoteLine;
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

            keyLabel.Font = ThemeFonts.Mono;
            keyLabel.ForeColor = Theme.Accent;

            comparisonLabel.Font = ThemeFonts.Mono;
            comparisonLabel.ForeColor = Theme.TextSecondary;

            applyToAllCheckBox.Font = ThemeFonts.Body;
            applyToAllCheckBox.ForeColor = Theme.TextSecondary;
            applyToAllCheckBox.BackColor = Color.Transparent;
            applyToAllCheckBox.FlatStyle = FlatStyle.Flat;
        }

        private void Complete(OverwriteDecision decision)
        {
            Decision = decision;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DarkTitleBar.Apply(this);
            overwriteButton.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Closing with the X or Escape is treated as "skip this one", which is the choice
            // that cannot destroy data.
            if (DialogResult != DialogResult.OK) Decision = OverwriteDecision.Skip;
            base.OnFormClosing(e);
        }
    }
}
