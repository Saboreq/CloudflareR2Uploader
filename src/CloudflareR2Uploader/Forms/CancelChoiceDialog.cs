using System;
using System.Drawing;
using System.Windows.Forms;
using CloudflareR2Uploader.Theming;

namespace CloudflareR2Uploader.Forms
{
    /// <summary>What to do with the parts already uploaded when a transfer is cancelled.</summary>
    public enum CancelChoice
    {
        /// <summary>Do not cancel after all.</summary>
        ContinueUploading = 0,

        /// <summary>Stop, but keep the completed parts on R2 so the file can be resumed.</summary>
        KeepPartsForResume = 1,

        /// <summary>Stop and abort the multipart upload so no partial data is left in the bucket.</summary>
        AbortAndDiscard = 2
    }

    /// <summary>
    /// Lets the user decide between keeping uploaded parts for a later resume and discarding
    /// them. Only shown when there is genuinely something to keep.
    /// </summary>
    public partial class CancelChoiceDialog : Form
    {
        public CancelChoiceDialog(bool multipartInProgress)
        {
            InitializeComponent();

            Icon = Program.LoadApplicationIcon();
            ApplyTheme();

            Choice = CancelChoice.ContinueUploading;

            if (multipartInProgress)
            {
                headlineLabel.Text = "Stop the upload?";
                explanationLabel.Text =
                    "Parts that have already been uploaded can stay on R2 so this file can carry on from where it stopped." + Environment.NewLine + Environment.NewLine +
                    "Keeping them uses bucket storage until the upload is completed or discarded. Discarding them removes the incomplete upload from the bucket straight away.";
            }
            else
            {
                headlineLabel.Text = "Stop the upload?";
                explanationLabel.Text =
                    "Nothing has been stored in the bucket yet, so stopping now leaves no partial data behind." + Environment.NewLine + Environment.NewLine +
                    "Files that have already finished are unaffected.";

                keepPartsButton.Text = "Stop";
                abortButton.Visible = false;
            }

            keepPartsButton.Click += delegate { Complete(CancelChoice.KeepPartsForResume); };
            abortButton.Click += delegate { Complete(CancelChoice.AbortAndDiscard); };
            keepUploadingButton.Click += delegate { Complete(CancelChoice.ContinueUploading); };

            AcceptButton = keepPartsButton;
        }

        public CancelChoice Choice { get; private set; }

        private void ApplyTheme()
        {
            BackColor = Theme.WindowBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;

            rootLayout.BackColor = Color.Transparent;
            buttonPanel.BackColor = Color.Transparent;

            headlineLabel.Font = ThemeFonts.Title;
            headlineLabel.ForeColor = Theme.TextPrimary;

            explanationLabel.Font = ThemeFonts.Body;
            explanationLabel.ForeColor = Theme.TextSecondary;
        }

        private void Complete(CancelChoice choice)
        {
            Choice = choice;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DarkTitleBar.Apply(this);
            keepPartsButton.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Closing the dialog without choosing means "carry on uploading".
            if (DialogResult != DialogResult.OK) Choice = CancelChoice.ContinueUploading;
            base.OnFormClosing(e);
        }
    }
}
