namespace CloudflareR2Uploader.Forms
{
    partial class ErrorDialog
    {
        /// <summary>Required designer variable.</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>Clean up any resources being used.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.headlineLabel = new System.Windows.Forms.Label();
            this.messageLabel = new System.Windows.Forms.Label();
            this.detailsTextBox = new System.Windows.Forms.TextBox();
            this.buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.closeButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.copyButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.detailsButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.rootLayout.SuspendLayout();
            this.buttonPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // rootLayout
            //
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.headlineLabel, 0, 0);
            this.rootLayout.Controls.Add(this.messageLabel, 0, 1);
            this.rootLayout.Controls.Add(this.detailsTextBox, 0, 2);
            this.rootLayout.Controls.Add(this.buttonPanel, 0, 3);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Location = new System.Drawing.Point(20, 20);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 4;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rootLayout.Size = new System.Drawing.Size(524, 260);
            this.rootLayout.TabIndex = 0;
            //
            // headlineLabel
            //
            this.headlineLabel.AutoSize = true;
            this.headlineLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.headlineLabel.Location = new System.Drawing.Point(3, 0);
            this.headlineLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 8);
            this.headlineLabel.Name = "headlineLabel";
            this.headlineLabel.Size = new System.Drawing.Size(518, 15);
            this.headlineLabel.TabIndex = 0;
            this.headlineLabel.Text = "Something went wrong";
            //
            // messageLabel
            //
            this.messageLabel.AutoSize = true;
            this.messageLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.messageLabel.Location = new System.Drawing.Point(3, 23);
            this.messageLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 14);
            this.messageLabel.MaximumSize = new System.Drawing.Size(520, 0);
            this.messageLabel.Name = "messageLabel";
            this.messageLabel.Size = new System.Drawing.Size(518, 15);
            this.messageLabel.TabIndex = 1;
            this.messageLabel.Text = "Message";
            //
            // detailsTextBox
            //
            this.detailsTextBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.detailsTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.detailsTextBox.Location = new System.Drawing.Point(3, 55);
            this.detailsTextBox.Multiline = true;
            this.detailsTextBox.Name = "detailsTextBox";
            this.detailsTextBox.ReadOnly = true;
            this.detailsTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.detailsTextBox.Size = new System.Drawing.Size(518, 154);
            this.detailsTextBox.TabIndex = 2;
            this.detailsTextBox.Visible = false;
            this.detailsTextBox.WordWrap = false;
            //
            // buttonPanel
            //
            this.buttonPanel.AutoSize = true;
            this.buttonPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.buttonPanel.Controls.Add(this.closeButton);
            this.buttonPanel.Controls.Add(this.copyButton);
            this.buttonPanel.Controls.Add(this.detailsButton);
            this.buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.buttonPanel.Location = new System.Drawing.Point(3, 218);
            this.buttonPanel.Margin = new System.Windows.Forms.Padding(3, 6, 3, 0);
            this.buttonPanel.Name = "buttonPanel";
            this.buttonPanel.Size = new System.Drawing.Size(518, 42);
            this.buttonPanel.TabIndex = 3;
            this.buttonPanel.WrapContents = false;
            //
            // closeButton
            //
            this.closeButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Primary;
            this.closeButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.closeButton.Location = new System.Drawing.Point(415, 3);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(100, 36);
            this.closeButton.TabIndex = 0;
            this.closeButton.Text = "Close";
            //
            // copyButton
            //
            this.copyButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.copyButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Copy;
            this.copyButton.Location = new System.Drawing.Point(219, 3);
            this.copyButton.Name = "copyButton";
            this.copyButton.Size = new System.Drawing.Size(190, 36);
            this.copyButton.TabIndex = 1;
            this.copyButton.Text = "Copy technical details";
            //
            // detailsButton
            //
            this.detailsButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Ghost;
            this.detailsButton.Icon = CloudflareR2Uploader.Controls.AppIcon.ChevronDown;
            this.detailsButton.Location = new System.Drawing.Point(78, 3);
            this.detailsButton.Name = "detailsButton";
            this.detailsButton.Size = new System.Drawing.Size(135, 36);
            this.detailsButton.TabIndex = 2;
            this.detailsButton.Text = "Show details";
            //
            // ErrorDialog
            //
            this.AcceptButton = this.closeButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(564, 300);
            this.Controls.Add(this.rootLayout);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(480, 260);
            this.Name = "ErrorDialog";
            this.Padding = new System.Windows.Forms.Padding(20);
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Cloudflare R2 Uploader";
            this.rootLayout.ResumeLayout(false);
            this.rootLayout.PerformLayout();
            this.buttonPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.Label headlineLabel;
        private System.Windows.Forms.Label messageLabel;
        private System.Windows.Forms.TextBox detailsTextBox;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private CloudflareR2Uploader.Controls.ModernButton closeButton;
        private CloudflareR2Uploader.Controls.ModernButton copyButton;
        private CloudflareR2Uploader.Controls.ModernButton detailsButton;
    }
}
