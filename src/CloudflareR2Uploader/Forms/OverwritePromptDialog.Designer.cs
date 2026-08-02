namespace CloudflareR2Uploader.Forms
{
    partial class OverwritePromptDialog
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
            this.keyLabel = new System.Windows.Forms.Label();
            this.comparisonLabel = new System.Windows.Forms.Label();
            this.applyToAllCheckBox = new System.Windows.Forms.CheckBox();
            this.buttonPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.cancelAllButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.skipButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.renameButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.overwriteButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.rootLayout.SuspendLayout();
            this.buttonPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // rootLayout
            //
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.headlineLabel, 0, 0);
            this.rootLayout.Controls.Add(this.keyLabel, 0, 1);
            this.rootLayout.Controls.Add(this.comparisonLabel, 0, 2);
            this.rootLayout.Controls.Add(this.applyToAllCheckBox, 0, 3);
            this.rootLayout.Controls.Add(this.buttonPanel, 0, 4);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Location = new System.Drawing.Point(20, 20);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 5;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rootLayout.Size = new System.Drawing.Size(560, 220);
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
            this.headlineLabel.Text = "This object already exists";
            //
            // keyLabel
            //
            this.keyLabel.AutoSize = true;
            this.keyLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.keyLabel.Location = new System.Drawing.Point(3, 25);
            this.keyLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.keyLabel.Name = "keyLabel";
            this.keyLabel.Size = new System.Drawing.Size(554, 15);
            this.keyLabel.TabIndex = 1;
            this.keyLabel.Text = "key";
            //
            // comparisonLabel
            //
            this.comparisonLabel.AutoSize = true;
            this.comparisonLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.comparisonLabel.Location = new System.Drawing.Point(3, 50);
            this.comparisonLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 12);
            this.comparisonLabel.Name = "comparisonLabel";
            this.comparisonLabel.Size = new System.Drawing.Size(554, 15);
            this.comparisonLabel.TabIndex = 2;
            this.comparisonLabel.Text = "comparison";
            //
            // applyToAllCheckBox
            //
            this.applyToAllCheckBox.AutoSize = true;
            this.applyToAllCheckBox.Location = new System.Drawing.Point(3, 77);
            this.applyToAllCheckBox.Margin = new System.Windows.Forms.Padding(3, 0, 3, 12);
            this.applyToAllCheckBox.Name = "applyToAllCheckBox";
            this.applyToAllCheckBox.Size = new System.Drawing.Size(230, 19);
            this.applyToAllCheckBox.TabIndex = 3;
            this.applyToAllCheckBox.Text = "Do the same for the rest of this queue";
            this.applyToAllCheckBox.UseVisualStyleBackColor = true;
            //
            // buttonPanel
            //
            this.buttonPanel.AutoSize = true;
            this.buttonPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.buttonPanel.Controls.Add(this.overwriteButton);
            this.buttonPanel.Controls.Add(this.renameButton);
            this.buttonPanel.Controls.Add(this.skipButton);
            this.buttonPanel.Controls.Add(this.cancelAllButton);
            this.buttonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.buttonPanel.Location = new System.Drawing.Point(3, 111);
            this.buttonPanel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 0);
            this.buttonPanel.Name = "buttonPanel";
            this.buttonPanel.Size = new System.Drawing.Size(554, 42);
            this.buttonPanel.TabIndex = 4;
            this.buttonPanel.WrapContents = false;
            //
            // overwriteButton
            //
            this.overwriteButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Primary;
            this.overwriteButton.Location = new System.Drawing.Point(431, 3);
            this.overwriteButton.Name = "overwriteButton";
            this.overwriteButton.Size = new System.Drawing.Size(120, 36);
            this.overwriteButton.TabIndex = 0;
            this.overwriteButton.Text = "Overwrite";
            //
            // renameButton
            //
            this.renameButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.renameButton.Location = new System.Drawing.Point(305, 3);
            this.renameButton.Name = "renameButton";
            this.renameButton.Size = new System.Drawing.Size(120, 36);
            this.renameButton.TabIndex = 1;
            this.renameButton.Text = "Keep both";
            //
            // skipButton
            //
            this.skipButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.skipButton.Location = new System.Drawing.Point(199, 3);
            this.skipButton.Name = "skipButton";
            this.skipButton.Size = new System.Drawing.Size(100, 36);
            this.skipButton.TabIndex = 2;
            this.skipButton.Text = "Skip";
            //
            // cancelAllButton
            //
            this.cancelAllButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Ghost;
            this.cancelAllButton.Location = new System.Drawing.Point(73, 3);
            this.cancelAllButton.Name = "cancelAllButton";
            this.cancelAllButton.Size = new System.Drawing.Size(120, 36);
            this.cancelAllButton.TabIndex = 3;
            this.cancelAllButton.Text = "Cancel upload";
            //
            // OverwritePromptDialog
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(600, 260);
            this.Controls.Add(this.rootLayout);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OverwritePromptDialog";
            this.Padding = new System.Windows.Forms.Padding(20);
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "An object with this key already exists";
            this.rootLayout.ResumeLayout(false);
            this.rootLayout.PerformLayout();
            this.buttonPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.Label headlineLabel;
        private System.Windows.Forms.Label keyLabel;
        private System.Windows.Forms.Label comparisonLabel;
        private System.Windows.Forms.CheckBox applyToAllCheckBox;
        private System.Windows.Forms.FlowLayoutPanel buttonPanel;
        private CloudflareR2Uploader.Controls.ModernButton overwriteButton;
        private CloudflareR2Uploader.Controls.ModernButton renameButton;
        private CloudflareR2Uploader.Controls.ModernButton skipButton;
        private CloudflareR2Uploader.Controls.ModernButton cancelAllButton;
    }
}
