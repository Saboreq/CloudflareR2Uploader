namespace CloudflareR2Uploader.Forms
{
    partial class CancelChoiceDialog
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.headlineLabel = new System.Windows.Forms.Label();
            this.explanationLabel = new System.Windows.Forms.Label();
            this.buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.keepPartsButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.abortButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.keepUploadingButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.rootLayout.SuspendLayout();
            this.buttonPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // rootLayout
            //
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.headlineLabel, 0, 0);
            this.rootLayout.Controls.Add(this.explanationLabel, 0, 1);
            this.rootLayout.Controls.Add(this.buttonPanel, 0, 2);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Location = new System.Drawing.Point(20, 20);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 3;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rootLayout.Size = new System.Drawing.Size(560, 160);
            this.rootLayout.TabIndex = 0;
            //
            // headlineLabel
            //
            this.headlineLabel.AutoSize = true;
            this.headlineLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.headlineLabel.Location = new System.Drawing.Point(3, 0);
            this.headlineLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.headlineLabel.Name = "headlineLabel";
            this.headlineLabel.Size = new System.Drawing.Size(554, 15);
            this.headlineLabel.TabIndex = 0;
            this.headlineLabel.Text = "Cancel the upload?";
            //
            // explanationLabel
            //
            this.explanationLabel.AutoSize = true;
            this.explanationLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.explanationLabel.Location = new System.Drawing.Point(3, 25);
            this.explanationLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 14);
            this.explanationLabel.Name = "explanationLabel";
            this.explanationLabel.Size = new System.Drawing.Size(554, 60);
            this.explanationLabel.TabIndex = 1;
            this.explanationLabel.Text = "explanation";
            //
            // buttonPanel
            //
            this.buttonPanel.AutoSize = true;
            this.buttonPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.buttonPanel.Controls.Add(this.keepPartsButton);
            this.buttonPanel.Controls.Add(this.abortButton);
            this.buttonPanel.Controls.Add(this.keepUploadingButton);
            this.buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.buttonPanel.Location = new System.Drawing.Point(3, 99);
            this.buttonPanel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.buttonPanel.Name = "buttonPanel";
            this.buttonPanel.Size = new System.Drawing.Size(554, 42);
            this.buttonPanel.TabIndex = 2;
            this.buttonPanel.WrapContents = false;
            //
            // keepPartsButton
            //
            this.keepPartsButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Primary;
            this.keepPartsButton.Location = new System.Drawing.Point(351, 3);
            this.keepPartsButton.Name = "keepPartsButton";
            this.keepPartsButton.Size = new System.Drawing.Size(200, 36);
            this.keepPartsButton.TabIndex = 0;
            this.keepPartsButton.Text = "Stop and keep parts";
            //
            // abortButton
            //
            this.abortButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Danger;
            this.abortButton.Location = new System.Drawing.Point(145, 3);
            this.abortButton.Name = "abortButton";
            this.abortButton.Size = new System.Drawing.Size(200, 36);
            this.abortButton.TabIndex = 1;
            this.abortButton.Text = "Stop and discard parts";
            //
            // keepUploadingButton
            //
            this.keepUploadingButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Ghost;
            this.keepUploadingButton.Location = new System.Drawing.Point(19, 3);
            this.keepUploadingButton.Name = "keepUploadingButton";
            this.keepUploadingButton.Size = new System.Drawing.Size(120, 36);
            this.keepUploadingButton.TabIndex = 2;
            this.keepUploadingButton.Text = "Keep uploading";
            //
            // CancelChoiceDialog
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(600, 200);
            this.Controls.Add(this.rootLayout);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CancelChoiceDialog";
            this.Padding = new System.Windows.Forms.Padding(20);
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Cancel upload";
            this.rootLayout.ResumeLayout(false);
            this.rootLayout.PerformLayout();
            this.buttonPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.Label headlineLabel;
        private System.Windows.Forms.Label explanationLabel;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private CloudflareR2Uploader.Controls.ModernButton keepPartsButton;
        private CloudflareR2Uploader.Controls.ModernButton abortButton;
        private CloudflareR2Uploader.Controls.ModernButton keepUploadingButton;
    }
}
