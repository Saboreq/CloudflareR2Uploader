namespace CloudflareR2Uploader.Forms
{
    partial class SettingsForm
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
            this.components = new System.ComponentModel.Container();
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.headerPanel = new System.Windows.Forms.Panel();
            this.titleLabel = new System.Windows.Forms.Label();
            this.subtitleLabel = new System.Windows.Forms.Label();
            this.scrollPanel = new System.Windows.Forms.Panel();
            this.formLayout = new System.Windows.Forms.TableLayoutPanel();
            this.connectionHeaderLabel = new System.Windows.Forms.Label();
            this.credentialHintLabel = new System.Windows.Forms.Label();
            this.accountIdLabel = new System.Windows.Forms.Label();
            this.accountIdTextBox = new CloudflareR2Uploader.Controls.ModernTextBox();
            this.bucketLabel = new System.Windows.Forms.Label();
            this.bucketTextBox = new CloudflareR2Uploader.Controls.ModernTextBox();
            this.accessKeyIdLabel = new System.Windows.Forms.Label();
            this.accessKeyIdTextBox = new CloudflareR2Uploader.Controls.ModernTextBox();
            this.secretKeyLabel = new System.Windows.Forms.Label();
            this.secretKeyTextBox = new CloudflareR2Uploader.Controls.ModernTextBox();
            this.endpointLabel = new System.Windows.Forms.Label();
            this.endpointTextBox = new CloudflareR2Uploader.Controls.ModernTextBox();
            this.resolvedEndpointLabel = new System.Windows.Forms.Label();
            this.publicUrlLabel = new System.Windows.Forms.Label();
            this.publicUrlTextBox = new CloudflareR2Uploader.Controls.ModernTextBox();
            this.transferHeaderLabel = new System.Windows.Forms.Label();
            this.thresholdLabel = new System.Windows.Forms.Label();
            this.thresholdNumeric = new System.Windows.Forms.NumericUpDown();
            this.partSizeLabel = new System.Windows.Forms.Label();
            this.partSizeNumeric = new System.Windows.Forms.NumericUpDown();
            this.parallelLabel = new System.Windows.Forms.Label();
            this.parallelNumeric = new System.Windows.Forms.NumericUpDown();
            this.retryLabel = new System.Windows.Forms.Label();
            this.retryNumeric = new System.Windows.Forms.NumericUpDown();
            this.securityHeaderLabel = new System.Windows.Forms.Label();
            this.rememberCheckBox = new System.Windows.Forms.CheckBox();
            this.securityHintLabel = new System.Windows.Forms.Label();
            this.statusPanel = new System.Windows.Forms.Panel();
            this.statusIndicator = new CloudflareR2Uploader.Controls.CircularStatusIndicator();
            this.statusHeadlineLabel = new System.Windows.Forms.Label();
            this.statusDetailLabel = new System.Windows.Forms.Label();
            this.buttonLayout = new System.Windows.Forms.TableLayoutPanel();
            this.leftButtonPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.testConnectionButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.openLogsButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.clearCredentialsButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.rightButtonPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.saveButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.cancelButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.rootLayout.SuspendLayout();
            this.headerPanel.SuspendLayout();
            this.scrollPanel.SuspendLayout();
            this.formLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.thresholdNumeric)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.partSizeNumeric)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.parallelNumeric)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.retryNumeric)).BeginInit();
            this.statusPanel.SuspendLayout();
            this.buttonLayout.SuspendLayout();
            this.leftButtonPanel.SuspendLayout();
            this.rightButtonPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // rootLayout
            //
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.headerPanel, 0, 0);
            this.rootLayout.Controls.Add(this.scrollPanel, 0, 1);
            this.rootLayout.Controls.Add(this.statusPanel, 0, 2);
            this.rootLayout.Controls.Add(this.buttonLayout, 0, 3);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Location = new System.Drawing.Point(18, 18);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 4;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 62F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 58F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 52F));
            this.rootLayout.Size = new System.Drawing.Size(684, 664);
            this.rootLayout.TabIndex = 0;
            //
            // headerPanel
            //
            this.headerPanel.Controls.Add(this.titleLabel);
            this.headerPanel.Controls.Add(this.subtitleLabel);
            this.headerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.headerPanel.Location = new System.Drawing.Point(3, 3);
            this.headerPanel.Name = "headerPanel";
            this.headerPanel.Size = new System.Drawing.Size(678, 56);
            this.headerPanel.TabIndex = 0;
            //
            // titleLabel
            //
            this.titleLabel.AutoSize = true;
            this.titleLabel.Location = new System.Drawing.Point(0, 0);
            this.titleLabel.Name = "titleLabel";
            this.titleLabel.Size = new System.Drawing.Size(120, 25);
            this.titleLabel.TabIndex = 0;
            this.titleLabel.Text = "Connection settings";
            //
            // subtitleLabel
            //
            this.subtitleLabel.AutoSize = true;
            this.subtitleLabel.Location = new System.Drawing.Point(1, 30);
            this.subtitleLabel.Name = "subtitleLabel";
            this.subtitleLabel.Size = new System.Drawing.Size(400, 15);
            this.subtitleLabel.TabIndex = 1;
            this.subtitleLabel.Text = "These details are used with the Cloudflare R2 S3-compatible API.";
            //
            // scrollPanel
            //
            this.scrollPanel.AutoScroll = true;
            this.scrollPanel.Controls.Add(this.formLayout);
            this.scrollPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.scrollPanel.Location = new System.Drawing.Point(3, 65);
            this.scrollPanel.Name = "scrollPanel";
            this.scrollPanel.Size = new System.Drawing.Size(678, 484);
            this.scrollPanel.TabIndex = 1;
            //
            // formLayout
            //
            this.formLayout.AutoSize = true;
            this.formLayout.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.formLayout.ColumnCount = 2;
            this.formLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 178F));
            this.formLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.formLayout.Controls.Add(this.connectionHeaderLabel, 0, 0);
            this.formLayout.Controls.Add(this.credentialHintLabel, 0, 1);
            this.formLayout.Controls.Add(this.accountIdLabel, 0, 2);
            this.formLayout.Controls.Add(this.accountIdTextBox, 1, 2);
            this.formLayout.Controls.Add(this.bucketLabel, 0, 3);
            this.formLayout.Controls.Add(this.bucketTextBox, 1, 3);
            this.formLayout.Controls.Add(this.accessKeyIdLabel, 0, 4);
            this.formLayout.Controls.Add(this.accessKeyIdTextBox, 1, 4);
            this.formLayout.Controls.Add(this.secretKeyLabel, 0, 5);
            this.formLayout.Controls.Add(this.secretKeyTextBox, 1, 5);
            this.formLayout.Controls.Add(this.endpointLabel, 0, 6);
            this.formLayout.Controls.Add(this.endpointTextBox, 1, 6);
            this.formLayout.Controls.Add(this.resolvedEndpointLabel, 1, 7);
            this.formLayout.Controls.Add(this.publicUrlLabel, 0, 8);
            this.formLayout.Controls.Add(this.publicUrlTextBox, 1, 8);
            this.formLayout.Controls.Add(this.transferHeaderLabel, 0, 9);
            this.formLayout.Controls.Add(this.thresholdLabel, 0, 10);
            this.formLayout.Controls.Add(this.thresholdNumeric, 1, 10);
            this.formLayout.Controls.Add(this.partSizeLabel, 0, 11);
            this.formLayout.Controls.Add(this.partSizeNumeric, 1, 11);
            this.formLayout.Controls.Add(this.parallelLabel, 0, 12);
            this.formLayout.Controls.Add(this.parallelNumeric, 1, 12);
            this.formLayout.Controls.Add(this.retryLabel, 0, 13);
            this.formLayout.Controls.Add(this.retryNumeric, 1, 13);
            this.formLayout.Controls.Add(this.securityHeaderLabel, 0, 14);
            this.formLayout.Controls.Add(this.rememberCheckBox, 1, 15);
            this.formLayout.Controls.Add(this.securityHintLabel, 1, 16);
            this.formLayout.Dock = System.Windows.Forms.DockStyle.Top;
            this.formLayout.Location = new System.Drawing.Point(0, 0);
            this.formLayout.Name = "formLayout";
            this.formLayout.RowCount = 17;
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.formLayout.Size = new System.Drawing.Size(660, 640);
            this.formLayout.TabIndex = 0;
            //
            // connectionHeaderLabel
            //
            this.connectionHeaderLabel.AutoSize = true;
            this.formLayout.SetColumnSpan(this.connectionHeaderLabel, 2);
            this.connectionHeaderLabel.Location = new System.Drawing.Point(3, 0);
            this.connectionHeaderLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 6);
            this.connectionHeaderLabel.Name = "connectionHeaderLabel";
            this.connectionHeaderLabel.Size = new System.Drawing.Size(120, 17);
            this.connectionHeaderLabel.TabIndex = 0;
            this.connectionHeaderLabel.Text = "R2 CONNECTION";
            //
            // credentialHintLabel
            //
            this.credentialHintLabel.AutoSize = true;
            this.formLayout.SetColumnSpan(this.credentialHintLabel, 2);
            this.credentialHintLabel.Location = new System.Drawing.Point(3, 23);
            this.credentialHintLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 14);
            this.credentialHintLabel.MaximumSize = new System.Drawing.Size(640, 0);
            this.credentialHintLabel.Name = "credentialHintLabel";
            this.credentialHintLabel.Size = new System.Drawing.Size(600, 45);
            this.credentialHintLabel.TabIndex = 1;
            this.credentialHintLabel.Text = "hint";
            //
            // accountIdLabel
            //
            this.accountIdLabel.AutoSize = true;
            this.accountIdLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.accountIdLabel.Location = new System.Drawing.Point(3, 82);
            this.accountIdLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.accountIdLabel.Name = "accountIdLabel";
            this.accountIdLabel.Size = new System.Drawing.Size(172, 34);
            this.accountIdLabel.TabIndex = 2;
            this.accountIdLabel.Text = "Cloudflare Account ID";
            this.accountIdLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // accountIdTextBox
            //
            this.accountIdTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.accountIdTextBox.Location = new System.Drawing.Point(181, 82);
            this.accountIdTextBox.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.accountIdTextBox.Name = "accountIdTextBox";
            this.accountIdTextBox.Size = new System.Drawing.Size(476, 34);
            this.accountIdTextBox.TabIndex = 3;
            //
            // bucketLabel
            //
            this.bucketLabel.AutoSize = true;
            this.bucketLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.bucketLabel.Location = new System.Drawing.Point(3, 126);
            this.bucketLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.bucketLabel.Name = "bucketLabel";
            this.bucketLabel.Size = new System.Drawing.Size(172, 34);
            this.bucketLabel.TabIndex = 4;
            this.bucketLabel.Text = "Bucket name";
            this.bucketLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // bucketTextBox
            //
            this.bucketTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.bucketTextBox.Location = new System.Drawing.Point(181, 126);
            this.bucketTextBox.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.bucketTextBox.Name = "bucketTextBox";
            this.bucketTextBox.Size = new System.Drawing.Size(476, 34);
            this.bucketTextBox.TabIndex = 5;
            //
            // accessKeyIdLabel
            //
            this.accessKeyIdLabel.AutoSize = true;
            this.accessKeyIdLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.accessKeyIdLabel.Location = new System.Drawing.Point(3, 170);
            this.accessKeyIdLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.accessKeyIdLabel.Name = "accessKeyIdLabel";
            this.accessKeyIdLabel.Size = new System.Drawing.Size(172, 34);
            this.accessKeyIdLabel.TabIndex = 6;
            this.accessKeyIdLabel.Text = "Access Key ID";
            this.accessKeyIdLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // accessKeyIdTextBox
            //
            this.accessKeyIdTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.accessKeyIdTextBox.Location = new System.Drawing.Point(181, 170);
            this.accessKeyIdTextBox.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.accessKeyIdTextBox.Name = "accessKeyIdTextBox";
            this.accessKeyIdTextBox.Size = new System.Drawing.Size(476, 34);
            this.accessKeyIdTextBox.TabIndex = 7;
            //
            // secretKeyLabel
            //
            this.secretKeyLabel.AutoSize = true;
            this.secretKeyLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.secretKeyLabel.Location = new System.Drawing.Point(3, 214);
            this.secretKeyLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.secretKeyLabel.Name = "secretKeyLabel";
            this.secretKeyLabel.Size = new System.Drawing.Size(172, 34);
            this.secretKeyLabel.TabIndex = 8;
            this.secretKeyLabel.Text = "Secret Access Key";
            this.secretKeyLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // secretKeyTextBox
            //
            this.secretKeyTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.secretKeyTextBox.Location = new System.Drawing.Point(181, 214);
            this.secretKeyTextBox.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.secretKeyTextBox.Name = "secretKeyTextBox";
            this.secretKeyTextBox.Size = new System.Drawing.Size(476, 34);
            this.secretKeyTextBox.TabIndex = 9;
            this.secretKeyTextBox.UseSystemPasswordChar = true;
            //
            // endpointLabel
            //
            this.endpointLabel.AutoSize = true;
            this.endpointLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.endpointLabel.Location = new System.Drawing.Point(3, 258);
            this.endpointLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.endpointLabel.Name = "endpointLabel";
            this.endpointLabel.Size = new System.Drawing.Size(172, 34);
            this.endpointLabel.TabIndex = 10;
            this.endpointLabel.Text = "Custom endpoint";
            this.endpointLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // endpointTextBox
            //
            this.endpointTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.endpointTextBox.Location = new System.Drawing.Point(181, 258);
            this.endpointTextBox.Margin = new System.Windows.Forms.Padding(3, 0, 3, 4);
            this.endpointTextBox.Name = "endpointTextBox";
            this.endpointTextBox.PlaceholderText = "Leave empty — this is only for unusual setups";
            this.endpointTextBox.Size = new System.Drawing.Size(476, 34);
            this.endpointTextBox.TabIndex = 11;
            //
            // resolvedEndpointLabel
            //
            this.resolvedEndpointLabel.AutoSize = true;
            this.resolvedEndpointLabel.Location = new System.Drawing.Point(181, 296);
            this.resolvedEndpointLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 12);
            this.resolvedEndpointLabel.Name = "resolvedEndpointLabel";
            this.resolvedEndpointLabel.Size = new System.Drawing.Size(300, 15);
            this.resolvedEndpointLabel.TabIndex = 12;
            this.resolvedEndpointLabel.Text = "endpoint";
            //
            // publicUrlLabel
            //
            this.publicUrlLabel.AutoSize = true;
            this.publicUrlLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.publicUrlLabel.Location = new System.Drawing.Point(3, 323);
            this.publicUrlLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.publicUrlLabel.Name = "publicUrlLabel";
            this.publicUrlLabel.Size = new System.Drawing.Size(172, 34);
            this.publicUrlLabel.TabIndex = 13;
            this.publicUrlLabel.Text = "Public domain";
            this.publicUrlLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // publicUrlTextBox
            //
            this.publicUrlTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.publicUrlTextBox.Location = new System.Drawing.Point(181, 323);
            this.publicUrlTextBox.Margin = new System.Windows.Forms.Padding(3, 0, 3, 18);
            this.publicUrlTextBox.Name = "publicUrlTextBox";
            this.publicUrlTextBox.PlaceholderText = "files.example.com — used only to display links";
            this.publicUrlTextBox.Size = new System.Drawing.Size(476, 34);
            this.publicUrlTextBox.TabIndex = 14;
            //
            // transferHeaderLabel
            //
            this.transferHeaderLabel.AutoSize = true;
            this.formLayout.SetColumnSpan(this.transferHeaderLabel, 2);
            this.transferHeaderLabel.Location = new System.Drawing.Point(3, 375);
            this.transferHeaderLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 12);
            this.transferHeaderLabel.Name = "transferHeaderLabel";
            this.transferHeaderLabel.Size = new System.Drawing.Size(120, 17);
            this.transferHeaderLabel.TabIndex = 15;
            this.transferHeaderLabel.Text = "TRANSFER";
            //
            // thresholdLabel
            //
            this.thresholdLabel.AutoSize = true;
            this.thresholdLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.thresholdLabel.Location = new System.Drawing.Point(3, 404);
            this.thresholdLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.thresholdLabel.Name = "thresholdLabel";
            this.thresholdLabel.Size = new System.Drawing.Size(172, 30);
            this.thresholdLabel.TabIndex = 16;
            this.thresholdLabel.Text = "Multipart threshold (MiB)";
            this.thresholdLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // thresholdNumeric
            //
            this.thresholdNumeric.Location = new System.Drawing.Point(181, 404);
            this.thresholdNumeric.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.thresholdNumeric.Name = "thresholdNumeric";
            this.thresholdNumeric.Size = new System.Drawing.Size(120, 25);
            this.thresholdNumeric.TabIndex = 17;
            //
            // partSizeLabel
            //
            this.partSizeLabel.AutoSize = true;
            this.partSizeLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.partSizeLabel.Location = new System.Drawing.Point(3, 444);
            this.partSizeLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.partSizeLabel.Name = "partSizeLabel";
            this.partSizeLabel.Size = new System.Drawing.Size(172, 30);
            this.partSizeLabel.TabIndex = 18;
            this.partSizeLabel.Text = "Multipart part size (MiB)";
            this.partSizeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // partSizeNumeric
            //
            this.partSizeNumeric.Location = new System.Drawing.Point(181, 444);
            this.partSizeNumeric.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.partSizeNumeric.Name = "partSizeNumeric";
            this.partSizeNumeric.Size = new System.Drawing.Size(120, 25);
            this.partSizeNumeric.TabIndex = 19;
            //
            // parallelLabel
            //
            this.parallelLabel.AutoSize = true;
            this.parallelLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.parallelLabel.Location = new System.Drawing.Point(3, 484);
            this.parallelLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.parallelLabel.Name = "parallelLabel";
            this.parallelLabel.Size = new System.Drawing.Size(172, 30);
            this.parallelLabel.TabIndex = 20;
            this.parallelLabel.Text = "Parallel parts (1–8)";
            this.parallelLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // parallelNumeric
            //
            this.parallelNumeric.Location = new System.Drawing.Point(181, 484);
            this.parallelNumeric.Margin = new System.Windows.Forms.Padding(3, 0, 3, 10);
            this.parallelNumeric.Name = "parallelNumeric";
            this.parallelNumeric.Size = new System.Drawing.Size(120, 25);
            this.parallelNumeric.TabIndex = 21;
            //
            // retryLabel
            //
            this.retryLabel.AutoSize = true;
            this.retryLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.retryLabel.Location = new System.Drawing.Point(3, 524);
            this.retryLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 18);
            this.retryLabel.Name = "retryLabel";
            this.retryLabel.Size = new System.Drawing.Size(172, 30);
            this.retryLabel.TabIndex = 22;
            this.retryLabel.Text = "Automatic retries";
            this.retryLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // retryNumeric
            //
            this.retryNumeric.Location = new System.Drawing.Point(181, 524);
            this.retryNumeric.Margin = new System.Windows.Forms.Padding(3, 0, 3, 18);
            this.retryNumeric.Name = "retryNumeric";
            this.retryNumeric.Size = new System.Drawing.Size(120, 25);
            this.retryNumeric.TabIndex = 23;
            //
            // securityHeaderLabel
            //
            this.securityHeaderLabel.AutoSize = true;
            this.formLayout.SetColumnSpan(this.securityHeaderLabel, 2);
            this.securityHeaderLabel.Location = new System.Drawing.Point(3, 572);
            this.securityHeaderLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 12);
            this.securityHeaderLabel.Name = "securityHeaderLabel";
            this.securityHeaderLabel.Size = new System.Drawing.Size(120, 17);
            this.securityHeaderLabel.TabIndex = 24;
            this.securityHeaderLabel.Text = "CREDENTIAL STORAGE";
            //
            // rememberCheckBox
            //
            this.rememberCheckBox.AutoSize = true;
            this.rememberCheckBox.Location = new System.Drawing.Point(181, 601);
            this.rememberCheckBox.Margin = new System.Windows.Forms.Padding(3, 0, 3, 6);
            this.rememberCheckBox.Name = "rememberCheckBox";
            this.rememberCheckBox.Size = new System.Drawing.Size(280, 19);
            this.rememberCheckBox.TabIndex = 25;
            this.rememberCheckBox.Text = "Remember credentials on this computer";
            this.rememberCheckBox.UseVisualStyleBackColor = true;
            //
            // securityHintLabel
            //
            this.securityHintLabel.AutoSize = true;
            this.securityHintLabel.Location = new System.Drawing.Point(181, 626);
            this.securityHintLabel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 8);
            this.securityHintLabel.MaximumSize = new System.Drawing.Size(470, 0);
            this.securityHintLabel.Name = "securityHintLabel";
            this.securityHintLabel.Size = new System.Drawing.Size(460, 30);
            this.securityHintLabel.TabIndex = 26;
            this.securityHintLabel.Text = "hint";
            //
            // statusPanel
            //
            this.statusPanel.Controls.Add(this.statusIndicator);
            this.statusPanel.Controls.Add(this.statusHeadlineLabel);
            this.statusPanel.Controls.Add(this.statusDetailLabel);
            this.statusPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusPanel.Location = new System.Drawing.Point(3, 555);
            this.statusPanel.Name = "statusPanel";
            this.statusPanel.Size = new System.Drawing.Size(678, 52);
            this.statusPanel.TabIndex = 2;
            //
            // statusIndicator
            //
            this.statusIndicator.Location = new System.Drawing.Point(2, 8);
            this.statusIndicator.Name = "statusIndicator";
            this.statusIndicator.Size = new System.Drawing.Size(16, 16);
            this.statusIndicator.TabIndex = 0;
            //
            // statusHeadlineLabel
            //
            this.statusHeadlineLabel.AutoSize = true;
            this.statusHeadlineLabel.Location = new System.Drawing.Point(26, 7);
            this.statusHeadlineLabel.Name = "statusHeadlineLabel";
            this.statusHeadlineLabel.Size = new System.Drawing.Size(200, 15);
            this.statusHeadlineLabel.TabIndex = 1;
            this.statusHeadlineLabel.Text = "Not tested yet";
            //
            // statusDetailLabel
            //
            this.statusDetailLabel.AutoSize = true;
            this.statusDetailLabel.Location = new System.Drawing.Point(26, 25);
            this.statusDetailLabel.MaximumSize = new System.Drawing.Size(640, 0);
            this.statusDetailLabel.Name = "statusDetailLabel";
            this.statusDetailLabel.Size = new System.Drawing.Size(400, 15);
            this.statusDetailLabel.TabIndex = 2;
            this.statusDetailLabel.Text = "Use Test connection to check these details before uploading.";
            //
            // buttonLayout
            //
            this.buttonLayout.ColumnCount = 2;
            this.buttonLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.buttonLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.buttonLayout.Controls.Add(this.leftButtonPanel, 0, 0);
            this.buttonLayout.Controls.Add(this.rightButtonPanel, 1, 0);
            this.buttonLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.buttonLayout.Location = new System.Drawing.Point(3, 613);
            this.buttonLayout.Name = "buttonLayout";
            this.buttonLayout.RowCount = 1;
            this.buttonLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.buttonLayout.Size = new System.Drawing.Size(678, 46);
            this.buttonLayout.TabIndex = 3;
            //
            // leftButtonPanel
            //
            this.leftButtonPanel.AutoSize = true;
            this.leftButtonPanel.Controls.Add(this.testConnectionButton);
            this.leftButtonPanel.Controls.Add(this.openLogsButton);
            this.leftButtonPanel.Controls.Add(this.clearCredentialsButton);
            this.leftButtonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.leftButtonPanel.Location = new System.Drawing.Point(0, 0);
            this.leftButtonPanel.Margin = new System.Windows.Forms.Padding(0);
            this.leftButtonPanel.Name = "leftButtonPanel";
            this.leftButtonPanel.Size = new System.Drawing.Size(440, 46);
            this.leftButtonPanel.TabIndex = 0;
            this.leftButtonPanel.WrapContents = false;
            //
            // testConnectionButton
            //
            this.testConnectionButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.testConnectionButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Refresh;
            this.testConnectionButton.Location = new System.Drawing.Point(3, 3);
            this.testConnectionButton.Name = "testConnectionButton";
            this.testConnectionButton.Size = new System.Drawing.Size(150, 36);
            this.testConnectionButton.TabIndex = 0;
            this.testConnectionButton.Text = "Test connection";
            //
            // openLogsButton
            //
            this.openLogsButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Ghost;
            this.openLogsButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Folder;
            this.openLogsButton.Location = new System.Drawing.Point(159, 3);
            this.openLogsButton.Name = "openLogsButton";
            this.openLogsButton.Size = new System.Drawing.Size(130, 36);
            this.openLogsButton.TabIndex = 1;
            this.openLogsButton.Text = "Open logs";
            //
            // clearCredentialsButton
            //
            this.clearCredentialsButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Danger;
            this.clearCredentialsButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Trash;
            this.clearCredentialsButton.Location = new System.Drawing.Point(295, 3);
            this.clearCredentialsButton.Name = "clearCredentialsButton";
            this.clearCredentialsButton.Size = new System.Drawing.Size(180, 36);
            this.clearCredentialsButton.TabIndex = 2;
            this.clearCredentialsButton.Text = "Clear saved credentials";
            //
            // rightButtonPanel
            //
            this.rightButtonPanel.AutoSize = true;
            this.rightButtonPanel.Controls.Add(this.saveButton);
            this.rightButtonPanel.Controls.Add(this.cancelButton);
            this.rightButtonPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightButtonPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.rightButtonPanel.Location = new System.Drawing.Point(440, 0);
            this.rightButtonPanel.Margin = new System.Windows.Forms.Padding(0);
            this.rightButtonPanel.Name = "rightButtonPanel";
            this.rightButtonPanel.Size = new System.Drawing.Size(238, 46);
            this.rightButtonPanel.TabIndex = 1;
            this.rightButtonPanel.WrapContents = false;
            //
            // saveButton
            //
            this.saveButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Primary;
            this.saveButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Check;
            this.saveButton.Location = new System.Drawing.Point(125, 3);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(110, 36);
            this.saveButton.TabIndex = 0;
            this.saveButton.Text = "Save";
            //
            // cancelButton
            //
            this.cancelButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Ghost;
            this.cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.cancelButton.Location = new System.Drawing.Point(9, 3);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(110, 36);
            this.cancelButton.TabIndex = 1;
            this.cancelButton.Text = "Cancel";
            //
            // SettingsForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.CancelButton = this.cancelButton;
            this.ClientSize = new System.Drawing.Size(720, 700);
            this.Controls.Add(this.rootLayout);
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(660, 560);
            this.Name = "SettingsForm";
            this.Padding = new System.Windows.Forms.Padding(18);
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Settings";
            this.rootLayout.ResumeLayout(false);
            this.headerPanel.ResumeLayout(false);
            this.headerPanel.PerformLayout();
            this.scrollPanel.ResumeLayout(false);
            this.scrollPanel.PerformLayout();
            this.formLayout.ResumeLayout(false);
            this.formLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.thresholdNumeric)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.partSizeNumeric)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.parallelNumeric)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.retryNumeric)).EndInit();
            this.statusPanel.ResumeLayout(false);
            this.statusPanel.PerformLayout();
            this.buttonLayout.ResumeLayout(false);
            this.buttonLayout.PerformLayout();
            this.leftButtonPanel.ResumeLayout(false);
            this.rightButtonPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.Panel headerPanel;
        private System.Windows.Forms.Label titleLabel;
        private System.Windows.Forms.Label subtitleLabel;
        private System.Windows.Forms.Panel scrollPanel;
        private System.Windows.Forms.TableLayoutPanel formLayout;
        private System.Windows.Forms.Label connectionHeaderLabel;
        private System.Windows.Forms.Label credentialHintLabel;
        private System.Windows.Forms.Label accountIdLabel;
        private CloudflareR2Uploader.Controls.ModernTextBox accountIdTextBox;
        private System.Windows.Forms.Label bucketLabel;
        private CloudflareR2Uploader.Controls.ModernTextBox bucketTextBox;
        private System.Windows.Forms.Label accessKeyIdLabel;
        private CloudflareR2Uploader.Controls.ModernTextBox accessKeyIdTextBox;
        private System.Windows.Forms.Label secretKeyLabel;
        private CloudflareR2Uploader.Controls.ModernTextBox secretKeyTextBox;
        private System.Windows.Forms.Label endpointLabel;
        private CloudflareR2Uploader.Controls.ModernTextBox endpointTextBox;
        private System.Windows.Forms.Label resolvedEndpointLabel;
        private System.Windows.Forms.Label publicUrlLabel;
        private CloudflareR2Uploader.Controls.ModernTextBox publicUrlTextBox;
        private System.Windows.Forms.Label transferHeaderLabel;
        private System.Windows.Forms.Label thresholdLabel;
        private System.Windows.Forms.NumericUpDown thresholdNumeric;
        private System.Windows.Forms.Label partSizeLabel;
        private System.Windows.Forms.NumericUpDown partSizeNumeric;
        private System.Windows.Forms.Label parallelLabel;
        private System.Windows.Forms.NumericUpDown parallelNumeric;
        private System.Windows.Forms.Label retryLabel;
        private System.Windows.Forms.NumericUpDown retryNumeric;
        private System.Windows.Forms.Label securityHeaderLabel;
        private System.Windows.Forms.CheckBox rememberCheckBox;
        private System.Windows.Forms.Label securityHintLabel;
        private System.Windows.Forms.Panel statusPanel;
        private CloudflareR2Uploader.Controls.CircularStatusIndicator statusIndicator;
        private System.Windows.Forms.Label statusHeadlineLabel;
        private System.Windows.Forms.Label statusDetailLabel;
        private System.Windows.Forms.TableLayoutPanel buttonLayout;
        private System.Windows.Forms.FlowLayoutPanel leftButtonPanel;
        private CloudflareR2Uploader.Controls.ModernButton testConnectionButton;
        private CloudflareR2Uploader.Controls.ModernButton openLogsButton;
        private CloudflareR2Uploader.Controls.ModernButton clearCredentialsButton;
        private System.Windows.Forms.FlowLayoutPanel rightButtonPanel;
        private CloudflareR2Uploader.Controls.ModernButton saveButton;
        private CloudflareR2Uploader.Controls.ModernButton cancelButton;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
