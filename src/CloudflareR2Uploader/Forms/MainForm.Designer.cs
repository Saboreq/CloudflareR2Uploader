namespace CloudflareR2Uploader.Forms
{
    partial class MainForm
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
            this.brandMark = new CloudflareR2Uploader.Controls.BrandMarkControl();
            this.brandEyebrowLabel = new System.Windows.Forms.Label();
            this.appTitleLabel = new System.Windows.Forms.Label();
            this.connectionIndicator = new CloudflareR2Uploader.Controls.CircularStatusIndicator();
            this.connectionLabel = new System.Windows.Forms.Label();
            this.bucketBadgeLabel = new System.Windows.Forms.Label();
            this.settingsButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.pageNavigationPanel = new System.Windows.Forms.Panel();
            this.uploadPageButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.bucketFilesPageButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.dropZone = new CloudflareR2Uploader.Controls.UploadDropZone();
            this.destinationCard = new CloudflareR2Uploader.Controls.CardPanel();
            this.destinationLayout = new System.Windows.Forms.TableLayoutPanel();
            this.prefixLabel = new System.Windows.Forms.Label();
            this.prefixTextBox = new CloudflareR2Uploader.Controls.ModernTextBox();
            this.overwriteLabel = new System.Windows.Forms.Label();
            this.overwriteComboBox = new CloudflareR2Uploader.Controls.ModernComboBox();
            this.customNameLabel = new System.Windows.Forms.Label();
            this.customNameTextBox = new CloudflareR2Uploader.Controls.ModernTextBox();
            this.preserveStructureCheckBox = new System.Windows.Forms.CheckBox();
            this.queueCard = new CloudflareR2Uploader.Controls.CardPanel();
            this.queueListPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.emptyQueueLabel = new System.Windows.Forms.Label();
            this.queueToolbar = new System.Windows.Forms.Panel();
            this.uploadAllButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.pauseButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.cancelButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.retryFailedButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.clearCompletedButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.queueSummaryLabel = new System.Windows.Forms.Label();
            this.statusCard = new CloudflareR2Uploader.Controls.CardPanel();
            this.overallProgressBar = new CloudflareR2Uploader.Controls.ModernProgressBar();
            this.overallPercentLabel = new System.Windows.Forms.Label();
            this.overallTransferLabel = new System.Windows.Forms.Label();
            this.overallSpeedLabel = new System.Windows.Forms.Label();
            this.overallTimeLabel = new System.Windows.Forms.Label();
            this.overallCountsLabel = new System.Windows.Forms.Label();
            this.bucketBrowserPagePanel = new System.Windows.Forms.Panel();
            this.browserGrid = new System.Windows.Forms.DataGridView();
            this.browserNameColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.browserTypeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.browserSizeColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.browserModifiedColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.browserStateLabel = new System.Windows.Forms.Label();
            this.browserToolbarPanel = new System.Windows.Forms.Panel();
            this.browserBackButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.browserUpButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.browserRefreshButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.browserNewFolderButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.browserPathLabel = new System.Windows.Forms.Label();
            this.browserFooterPanel = new System.Windows.Forms.Panel();
            this.browserStatusLabel = new System.Windows.Forms.Label();
            this.browserPreviousButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.browserNextButton = new CloudflareR2Uploader.Controls.ModernButton();
            this.uiTimer = new System.Windows.Forms.Timer(this.components);
            this.toolTip = new System.Windows.Forms.ToolTip(this.components);
            this.rootLayout.SuspendLayout();
            this.headerPanel.SuspendLayout();
            this.pageNavigationPanel.SuspendLayout();
            this.destinationCard.SuspendLayout();
            this.destinationLayout.SuspendLayout();
            this.queueCard.SuspendLayout();
            this.queueToolbar.SuspendLayout();
            this.statusCard.SuspendLayout();
            this.bucketBrowserPagePanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.browserGrid)).BeginInit();
            this.browserToolbarPanel.SuspendLayout();
            this.browserFooterPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // rootLayout
            //
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.headerPanel, 0, 0);
            this.rootLayout.Controls.Add(this.pageNavigationPanel, 0, 1);
            this.rootLayout.Controls.Add(this.dropZone, 0, 2);
            this.rootLayout.Controls.Add(this.destinationCard, 0, 3);
            this.rootLayout.Controls.Add(this.queueCard, 0, 4);
            this.rootLayout.Controls.Add(this.statusCard, 0, 5);
            this.rootLayout.Controls.Add(this.bucketBrowserPagePanel, 0, 2);
            this.rootLayout.SetRowSpan(this.bucketBrowserPagePanel, 4);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Location = new System.Drawing.Point(16, 12);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 6;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 76F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 48F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 190F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 152F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 124F));
            this.rootLayout.Size = new System.Drawing.Size(1088, 826);
            this.rootLayout.TabIndex = 0;
            //
            // headerPanel
            //
            this.headerPanel.Controls.Add(this.brandMark);
            this.headerPanel.Controls.Add(this.brandEyebrowLabel);
            this.headerPanel.Controls.Add(this.appTitleLabel);
            this.headerPanel.Controls.Add(this.connectionIndicator);
            this.headerPanel.Controls.Add(this.connectionLabel);
            this.headerPanel.Controls.Add(this.bucketBadgeLabel);
            this.headerPanel.Controls.Add(this.settingsButton);
            this.headerPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.headerPanel.Location = new System.Drawing.Point(3, 0);
            this.headerPanel.Margin = new System.Windows.Forms.Padding(3, 0, 3, 3);
            this.headerPanel.Name = "headerPanel";
            this.headerPanel.Size = new System.Drawing.Size(1082, 73);
            this.headerPanel.TabIndex = 0;
            //
            // brandMark
            //
            this.brandMark.Location = new System.Drawing.Point(0, 9);
            this.brandMark.Name = "brandMark";
            this.brandMark.Size = new System.Drawing.Size(44, 44);
            this.brandMark.TabIndex = 0;
            //
            // brandEyebrowLabel
            //
            this.brandEyebrowLabel.AutoSize = true;
            this.brandEyebrowLabel.Location = new System.Drawing.Point(56, 3);
            this.brandEyebrowLabel.Name = "brandEyebrowLabel";
            this.brandEyebrowLabel.Size = new System.Drawing.Size(190, 13);
            this.brandEyebrowLabel.TabIndex = 1;
            this.brandEyebrowLabel.Text = "SABOREQ / R2 TRANSFER";
            //
            // appTitleLabel
            //
            this.appTitleLabel.AutoSize = true;
            this.appTitleLabel.Location = new System.Drawing.Point(56, 18);
            this.appTitleLabel.Name = "appTitleLabel";
            this.appTitleLabel.Size = new System.Drawing.Size(250, 25);
            this.appTitleLabel.TabIndex = 2;
            this.appTitleLabel.Text = "Cloudflare R2 Uploader";
            //
            // connectionIndicator
            //
            this.connectionIndicator.Location = new System.Drawing.Point(57, 51);
            this.connectionIndicator.Name = "connectionIndicator";
            this.connectionIndicator.Size = new System.Drawing.Size(12, 12);
            this.connectionIndicator.TabIndex = 3;
            //
            // connectionLabel
            //
            this.connectionLabel.AutoSize = true;
            this.connectionLabel.Location = new System.Drawing.Point(75, 48);
            this.connectionLabel.Name = "connectionLabel";
            this.connectionLabel.Size = new System.Drawing.Size(160, 15);
            this.connectionLabel.TabIndex = 4;
            this.connectionLabel.Text = "Not configured";
            //
            // bucketBadgeLabel
            //
            this.bucketBadgeLabel.AutoSize = true;
            this.bucketBadgeLabel.Location = new System.Drawing.Point(300, 48);
            this.bucketBadgeLabel.Name = "bucketBadgeLabel";
            this.bucketBadgeLabel.Size = new System.Drawing.Size(160, 15);
            this.bucketBadgeLabel.TabIndex = 5;
            this.bucketBadgeLabel.Text = "No bucket selected";
            //
            // settingsButton
            //
            this.settingsButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.settingsButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.settingsButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Gear;
            this.settingsButton.Location = new System.Drawing.Point(958, 18);
            this.settingsButton.Name = "settingsButton";
            this.settingsButton.Size = new System.Drawing.Size(124, 36);
            this.settingsButton.TabIndex = 6;
            this.settingsButton.Text = "Settings";
            //
            // pageNavigationPanel
            //
            this.pageNavigationPanel.Controls.Add(this.uploadPageButton);
            this.pageNavigationPanel.Controls.Add(this.bucketFilesPageButton);
            this.pageNavigationPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pageNavigationPanel.Location = new System.Drawing.Point(3, 79);
            this.pageNavigationPanel.Margin = new System.Windows.Forms.Padding(3, 3, 3, 5);
            this.pageNavigationPanel.Name = "pageNavigationPanel";
            this.pageNavigationPanel.Size = new System.Drawing.Size(1082, 40);
            this.pageNavigationPanel.TabIndex = 1;
            //
            // uploadPageButton
            //
            this.uploadPageButton.AccessibleDescription = "Show the upload queue and destination controls.";
            this.uploadPageButton.AccessibleName = "Upload page";
            this.uploadPageButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Primary;
            this.uploadPageButton.Icon = CloudflareR2Uploader.Controls.AppIcon.UploadArrow;
            this.uploadPageButton.Location = new System.Drawing.Point(0, 1);
            this.uploadPageButton.Name = "uploadPageButton";
            this.uploadPageButton.Size = new System.Drawing.Size(132, 36);
            this.uploadPageButton.TabIndex = 0;
            this.uploadPageButton.Text = "Upload";
            //
            // bucketFilesPageButton
            //
            this.bucketFilesPageButton.AccessibleDescription = "Browse objects and virtual folders in the selected R2 bucket.";
            this.bucketFilesPageButton.AccessibleName = "Files page";
            this.bucketFilesPageButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Ghost;
            this.bucketFilesPageButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Folder;
            this.bucketFilesPageButton.Location = new System.Drawing.Point(140, 1);
            this.bucketFilesPageButton.Name = "bucketFilesPageButton";
            this.bucketFilesPageButton.Size = new System.Drawing.Size(154, 36);
            this.bucketFilesPageButton.TabIndex = 1;
            this.bucketFilesPageButton.Text = "Files";
            //
            // dropZone
            //
            this.dropZone.AllowDrop = true;
            this.dropZone.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dropZone.Location = new System.Drawing.Point(3, 79);
            this.dropZone.Margin = new System.Windows.Forms.Padding(3, 3, 3, 8);
            this.dropZone.Name = "dropZone";
            this.dropZone.Size = new System.Drawing.Size(1082, 179);
            this.dropZone.TabIndex = 1;
            //
            // destinationCard
            //
            this.destinationCard.Controls.Add(this.destinationLayout);
            this.destinationCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.destinationCard.Heading = "Destination";
            this.destinationCard.HeadingIcon = CloudflareR2Uploader.Controls.AppIcon.Folder;
            this.destinationCard.Location = new System.Drawing.Point(3, 269);
            this.destinationCard.Margin = new System.Windows.Forms.Padding(3, 3, 3, 8);
            this.destinationCard.Name = "destinationCard";
            this.destinationCard.Padding = new System.Windows.Forms.Padding(18, 34, 18, 12);
            this.destinationCard.ShowShadow = false;
            this.destinationCard.Size = new System.Drawing.Size(1082, 141);
            this.destinationCard.TabIndex = 2;
            //
            // destinationLayout
            //
            this.destinationLayout.ColumnCount = 4;
            this.destinationLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 128F));
            this.destinationLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.destinationLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 150F));
            this.destinationLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 190F));
            this.destinationLayout.Controls.Add(this.prefixLabel, 0, 0);
            this.destinationLayout.Controls.Add(this.prefixTextBox, 1, 0);
            this.destinationLayout.Controls.Add(this.overwriteLabel, 2, 0);
            this.destinationLayout.Controls.Add(this.overwriteComboBox, 3, 0);
            this.destinationLayout.Controls.Add(this.customNameLabel, 0, 1);
            this.destinationLayout.Controls.Add(this.customNameTextBox, 1, 1);
            this.destinationLayout.Controls.Add(this.preserveStructureCheckBox, 3, 1);
            this.destinationLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.destinationLayout.Location = new System.Drawing.Point(18, 34);
            this.destinationLayout.Name = "destinationLayout";
            this.destinationLayout.RowCount = 2;
            this.destinationLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 44F));
            this.destinationLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 44F));
            this.destinationLayout.Size = new System.Drawing.Size(1046, 95);
            this.destinationLayout.TabIndex = 0;
            //
            // prefixLabel
            //
            this.prefixLabel.AutoSize = true;
            this.prefixLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.prefixLabel.Location = new System.Drawing.Point(3, 0);
            this.prefixLabel.Name = "prefixLabel";
            this.prefixLabel.Size = new System.Drawing.Size(122, 44);
            this.prefixLabel.TabIndex = 0;
            this.prefixLabel.Text = "Folder / prefix";
            this.prefixLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // prefixTextBox
            //
            this.prefixTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.prefixTextBox.Location = new System.Drawing.Point(131, 3);
            this.prefixTextBox.Margin = new System.Windows.Forms.Padding(3, 3, 12, 3);
            this.prefixTextBox.Name = "prefixTextBox";
            this.prefixTextBox.PlaceholderText = "uploads/2026 — optional";
            this.prefixTextBox.Size = new System.Drawing.Size(563, 38);
            this.prefixTextBox.TabIndex = 1;
            //
            // overwriteLabel
            //
            this.overwriteLabel.AutoSize = true;
            this.overwriteLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.overwriteLabel.Location = new System.Drawing.Point(709, 0);
            this.overwriteLabel.Name = "overwriteLabel";
            this.overwriteLabel.Size = new System.Drawing.Size(144, 44);
            this.overwriteLabel.TabIndex = 2;
            this.overwriteLabel.Text = "If the key exists";
            this.overwriteLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // overwriteComboBox
            //
            this.overwriteComboBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.overwriteComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.overwriteComboBox.FormattingEnabled = true;
            this.overwriteComboBox.Location = new System.Drawing.Point(859, 5);
            this.overwriteComboBox.Margin = new System.Windows.Forms.Padding(3, 5, 3, 5);
            this.overwriteComboBox.Name = "overwriteComboBox";
            this.overwriteComboBox.Size = new System.Drawing.Size(184, 34);
            this.overwriteComboBox.TabIndex = 3;
            //
            // customNameLabel
            //
            this.customNameLabel.AutoSize = true;
            this.customNameLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.customNameLabel.Location = new System.Drawing.Point(3, 44);
            this.customNameLabel.Name = "customNameLabel";
            this.customNameLabel.Size = new System.Drawing.Size(122, 44);
            this.customNameLabel.TabIndex = 4;
            this.customNameLabel.Text = "Object name";
            this.customNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // customNameTextBox
            //
            this.customNameTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.customNameTextBox.Location = new System.Drawing.Point(131, 47);
            this.customNameTextBox.Margin = new System.Windows.Forms.Padding(3, 3, 12, 3);
            this.customNameTextBox.Name = "customNameTextBox";
            this.customNameTextBox.PlaceholderText = "Only used when exactly one file is queued";
            this.customNameTextBox.Size = new System.Drawing.Size(563, 38);
            this.customNameTextBox.TabIndex = 5;
            //
            // preserveStructureCheckBox
            //
            this.preserveStructureCheckBox.AutoSize = true;
            this.destinationLayout.SetColumnSpan(this.preserveStructureCheckBox, 1);
            this.preserveStructureCheckBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.preserveStructureCheckBox.Location = new System.Drawing.Point(859, 47);
            this.preserveStructureCheckBox.Name = "preserveStructureCheckBox";
            this.preserveStructureCheckBox.Size = new System.Drawing.Size(184, 38);
            this.preserveStructureCheckBox.TabIndex = 6;
            this.preserveStructureCheckBox.Text = "Keep folder structure";
            this.preserveStructureCheckBox.UseVisualStyleBackColor = true;
            //
            // queueCard
            //
            this.queueCard.Controls.Add(this.queueListPanel);
            this.queueCard.Controls.Add(this.emptyQueueLabel);
            this.queueCard.Controls.Add(this.queueToolbar);
            this.queueCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.queueCard.Heading = "Upload queue";
            this.queueCard.HeadingIcon = CloudflareR2Uploader.Controls.AppIcon.UploadArrow;
            this.queueCard.Location = new System.Drawing.Point(3, 421);
            this.queueCard.Margin = new System.Windows.Forms.Padding(3, 3, 3, 8);
            this.queueCard.Name = "queueCard";
            this.queueCard.Padding = new System.Windows.Forms.Padding(14, 34, 14, 12);
            this.queueCard.ShowShadow = false;
            this.queueCard.Size = new System.Drawing.Size(1082, 273);
            this.queueCard.TabIndex = 3;
            //
            // queueListPanel
            //
            this.queueListPanel.AutoScroll = true;
            this.queueListPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.queueListPanel.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.queueListPanel.Location = new System.Drawing.Point(14, 84);
            this.queueListPanel.Name = "queueListPanel";
            this.queueListPanel.Size = new System.Drawing.Size(1054, 177);
            this.queueListPanel.TabIndex = 2;
            this.queueListPanel.WrapContents = false;
            //
            // emptyQueueLabel
            //
            this.emptyQueueLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.emptyQueueLabel.Location = new System.Drawing.Point(14, 84);
            this.emptyQueueLabel.Name = "emptyQueueLabel";
            this.emptyQueueLabel.Size = new System.Drawing.Size(1054, 177);
            this.emptyQueueLabel.TabIndex = 1;
            this.emptyQueueLabel.Text = "QUEUE EMPTY\r\nDrop files above, or choose Browse files.";
            this.emptyQueueLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // queueToolbar
            //
            this.queueToolbar.Controls.Add(this.uploadAllButton);
            this.queueToolbar.Controls.Add(this.pauseButton);
            this.queueToolbar.Controls.Add(this.cancelButton);
            this.queueToolbar.Controls.Add(this.retryFailedButton);
            this.queueToolbar.Controls.Add(this.clearCompletedButton);
            this.queueToolbar.Controls.Add(this.queueSummaryLabel);
            this.queueToolbar.Dock = System.Windows.Forms.DockStyle.Top;
            this.queueToolbar.Location = new System.Drawing.Point(14, 34);
            this.queueToolbar.Name = "queueToolbar";
            this.queueToolbar.Size = new System.Drawing.Size(1054, 50);
            this.queueToolbar.TabIndex = 0;
            //
            // uploadAllButton
            //
            this.uploadAllButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Primary;
            this.uploadAllButton.Icon = CloudflareR2Uploader.Controls.AppIcon.UploadArrow;
            this.uploadAllButton.Location = new System.Drawing.Point(0, 7);
            this.uploadAllButton.Name = "uploadAllButton";
            this.uploadAllButton.Size = new System.Drawing.Size(130, 36);
            this.uploadAllButton.TabIndex = 0;
            this.uploadAllButton.Text = "Upload all";
            //
            // pauseButton
            //
            this.pauseButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.pauseButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Pause;
            this.pauseButton.Location = new System.Drawing.Point(138, 7);
            this.pauseButton.Name = "pauseButton";
            this.pauseButton.Size = new System.Drawing.Size(110, 36);
            this.pauseButton.TabIndex = 1;
            this.pauseButton.Text = "Pause";
            //
            // cancelButton
            //
            this.cancelButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Danger;
            this.cancelButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Cross;
            this.cancelButton.Location = new System.Drawing.Point(256, 7);
            this.cancelButton.Name = "cancelButton";
            this.cancelButton.Size = new System.Drawing.Size(110, 36);
            this.cancelButton.TabIndex = 2;
            this.cancelButton.Text = "Cancel";
            //
            // retryFailedButton
            //
            this.retryFailedButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.retryFailedButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Refresh;
            this.retryFailedButton.Location = new System.Drawing.Point(382, 7);
            this.retryFailedButton.Name = "retryFailedButton";
            this.retryFailedButton.Size = new System.Drawing.Size(130, 36);
            this.retryFailedButton.TabIndex = 3;
            this.retryFailedButton.Text = "Retry failed";
            //
            // clearCompletedButton
            //
            this.clearCompletedButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Ghost;
            this.clearCompletedButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Trash;
            this.clearCompletedButton.Location = new System.Drawing.Point(520, 7);
            this.clearCompletedButton.Name = "clearCompletedButton";
            this.clearCompletedButton.Size = new System.Drawing.Size(160, 36);
            this.clearCompletedButton.TabIndex = 4;
            this.clearCompletedButton.Text = "Clear completed";
            //
            // queueSummaryLabel
            //
            this.queueSummaryLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.queueSummaryLabel.Location = new System.Drawing.Point(754, 7);
            this.queueSummaryLabel.Name = "queueSummaryLabel";
            this.queueSummaryLabel.Size = new System.Drawing.Size(300, 36);
            this.queueSummaryLabel.TabIndex = 5;
            this.queueSummaryLabel.Text = "0 files";
            this.queueSummaryLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // statusCard
            //
            this.statusCard.Controls.Add(this.overallProgressBar);
            this.statusCard.Controls.Add(this.overallPercentLabel);
            this.statusCard.Controls.Add(this.overallTransferLabel);
            this.statusCard.Controls.Add(this.overallSpeedLabel);
            this.statusCard.Controls.Add(this.overallTimeLabel);
            this.statusCard.Controls.Add(this.overallCountsLabel);
            this.statusCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusCard.Heading = "Overall progress";
            this.statusCard.HeadingIcon = CloudflareR2Uploader.Controls.AppIcon.Info;
            this.statusCard.Location = new System.Drawing.Point(3, 705);
            this.statusCard.Margin = new System.Windows.Forms.Padding(3, 3, 3, 0);
            this.statusCard.Name = "statusCard";
            this.statusCard.Padding = new System.Windows.Forms.Padding(18, 34, 18, 12);
            this.statusCard.ShowShadow = false;
            this.statusCard.Size = new System.Drawing.Size(1082, 121);
            this.statusCard.TabIndex = 4;
            //
            // overallProgressBar
            //
            this.overallProgressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.overallProgressBar.Location = new System.Drawing.Point(18, 50);
            this.overallProgressBar.Name = "overallProgressBar";
            this.overallProgressBar.Size = new System.Drawing.Size(980, 10);
            this.overallProgressBar.TabIndex = 0;
            //
            // overallPercentLabel
            //
            this.overallPercentLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.overallPercentLabel.Location = new System.Drawing.Point(1004, 38);
            this.overallPercentLabel.Name = "overallPercentLabel";
            this.overallPercentLabel.Size = new System.Drawing.Size(60, 28);
            this.overallPercentLabel.TabIndex = 1;
            this.overallPercentLabel.Text = "0%";
            this.overallPercentLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // overallTransferLabel
            //
            this.overallTransferLabel.AutoSize = true;
            this.overallTransferLabel.Location = new System.Drawing.Point(18, 74);
            this.overallTransferLabel.Name = "overallTransferLabel";
            this.overallTransferLabel.Size = new System.Drawing.Size(180, 15);
            this.overallTransferLabel.TabIndex = 2;
            this.overallTransferLabel.Text = "0 B / 0 B";
            //
            // overallSpeedLabel
            //
            this.overallSpeedLabel.AutoSize = true;
            this.overallSpeedLabel.Location = new System.Drawing.Point(250, 74);
            this.overallSpeedLabel.Name = "overallSpeedLabel";
            this.overallSpeedLabel.Size = new System.Drawing.Size(140, 15);
            this.overallSpeedLabel.TabIndex = 3;
            this.overallSpeedLabel.Text = "—";
            //
            // overallTimeLabel
            //
            this.overallTimeLabel.AutoSize = true;
            this.overallTimeLabel.Location = new System.Drawing.Point(450, 74);
            this.overallTimeLabel.Name = "overallTimeLabel";
            this.overallTimeLabel.Size = new System.Drawing.Size(220, 15);
            this.overallTimeLabel.TabIndex = 4;
            this.overallTimeLabel.Text = "elapsed 0:00 · remaining —";
            //
            // overallCountsLabel
            //
            this.overallCountsLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.overallCountsLabel.Location = new System.Drawing.Point(720, 74);
            this.overallCountsLabel.Name = "overallCountsLabel";
            this.overallCountsLabel.Size = new System.Drawing.Size(344, 18);
            this.overallCountsLabel.TabIndex = 5;
            this.overallCountsLabel.Text = "0 done · 0 failed · 0 queued";
            this.overallCountsLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // bucketBrowserPagePanel
            //
            this.bucketBrowserPagePanel.Controls.Add(this.browserGrid);
            this.bucketBrowserPagePanel.Controls.Add(this.browserStateLabel);
            this.bucketBrowserPagePanel.Controls.Add(this.browserFooterPanel);
            this.bucketBrowserPagePanel.Controls.Add(this.browserToolbarPanel);
            this.bucketBrowserPagePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.bucketBrowserPagePanel.Location = new System.Drawing.Point(3, 127);
            this.bucketBrowserPagePanel.Margin = new System.Windows.Forms.Padding(3, 3, 3, 0);
            this.bucketBrowserPagePanel.Name = "bucketBrowserPagePanel";
            this.bucketBrowserPagePanel.Padding = new System.Windows.Forms.Padding(14, 10, 14, 10);
            this.bucketBrowserPagePanel.Size = new System.Drawing.Size(1082, 699);
            this.bucketBrowserPagePanel.TabIndex = 6;
            this.bucketBrowserPagePanel.Visible = false;
            //
            // browserGrid
            //
            this.browserGrid.AllowUserToAddRows = false;
            this.browserGrid.AllowUserToDeleteRows = false;
            this.browserGrid.AllowUserToOrderColumns = false;
            this.browserGrid.AllowUserToResizeRows = false;
            this.browserGrid.AutoGenerateColumns = false;
            this.browserGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.browserGrid.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.browserGrid.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.SingleHorizontal;
            this.browserGrid.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.None;
            this.browserGrid.ColumnHeadersHeight = 40;
            this.browserGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.browserGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.browserNameColumn,
            this.browserTypeColumn,
            this.browserSizeColumn,
            this.browserModifiedColumn});
            this.browserGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.browserGrid.EnableHeadersVisualStyles = false;
            this.browserGrid.Location = new System.Drawing.Point(14, 66);
            this.browserGrid.MultiSelect = true;
            this.browserGrid.Name = "browserGrid";
            this.browserGrid.ReadOnly = true;
            this.browserGrid.RowHeadersVisible = false;
            this.browserGrid.RowTemplate.Height = 38;
            this.browserGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.browserGrid.Size = new System.Drawing.Size(1054, 573);
            this.browserGrid.TabIndex = 1;
            this.browserGrid.AccessibleName = "Files";
            this.browserGrid.AccessibleDescription = "Read-only objects and virtual folders in the selected R2 bucket.";
            //
            // browserNameColumn
            //
            this.browserNameColumn.FillWeight = 44F;
            this.browserNameColumn.HeaderText = "Name";
            this.browserNameColumn.Name = "browserNameColumn";
            this.browserNameColumn.ReadOnly = true;
            this.browserNameColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // browserTypeColumn
            //
            this.browserTypeColumn.FillWeight = 18F;
            this.browserTypeColumn.HeaderText = "Type";
            this.browserTypeColumn.Name = "browserTypeColumn";
            this.browserTypeColumn.ReadOnly = true;
            this.browserTypeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // browserSizeColumn
            //
            this.browserSizeColumn.FillWeight = 14F;
            this.browserSizeColumn.HeaderText = "Size";
            this.browserSizeColumn.Name = "browserSizeColumn";
            this.browserSizeColumn.ReadOnly = true;
            this.browserSizeColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // browserModifiedColumn
            //
            this.browserModifiedColumn.FillWeight = 24F;
            this.browserModifiedColumn.HeaderText = "Last modified";
            this.browserModifiedColumn.Name = "browserModifiedColumn";
            this.browserModifiedColumn.ReadOnly = true;
            this.browserModifiedColumn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // browserStateLabel
            //
            this.browserStateLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.browserStateLabel.Location = new System.Drawing.Point(14, 66);
            this.browserStateLabel.Name = "browserStateLabel";
            this.browserStateLabel.Padding = new System.Windows.Forms.Padding(40);
            this.browserStateLabel.Size = new System.Drawing.Size(1054, 573);
            this.browserStateLabel.TabIndex = 2;
            this.browserStateLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.browserStateLabel.Visible = false;
            //
            // browserToolbarPanel
            //
            this.browserToolbarPanel.Controls.Add(this.browserBackButton);
            this.browserToolbarPanel.Controls.Add(this.browserUpButton);
            this.browserToolbarPanel.Controls.Add(this.browserRefreshButton);
            this.browserToolbarPanel.Controls.Add(this.browserNewFolderButton);
            this.browserToolbarPanel.Controls.Add(this.browserPathLabel);
            this.browserToolbarPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.browserToolbarPanel.Location = new System.Drawing.Point(14, 10);
            this.browserToolbarPanel.Name = "browserToolbarPanel";
            this.browserToolbarPanel.Size = new System.Drawing.Size(1054, 56);
            this.browserToolbarPanel.TabIndex = 0;
            //
            // browserBackButton
            //
            this.browserBackButton.AccessibleName = "Back to previous folder";
            this.browserBackButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.browserBackButton.Icon = CloudflareR2Uploader.Controls.AppIcon.ArrowLeft;
            this.browserBackButton.Location = new System.Drawing.Point(0, 7);
            this.browserBackButton.Name = "browserBackButton";
            this.browserBackButton.Size = new System.Drawing.Size(92, 38);
            this.browserBackButton.TabIndex = 0;
            this.browserBackButton.Text = "Back";
            //
            // browserUpButton
            //
            this.browserUpButton.AccessibleName = "Up to parent folder";
            this.browserUpButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.browserUpButton.Icon = CloudflareR2Uploader.Controls.AppIcon.ArrowUp;
            this.browserUpButton.Location = new System.Drawing.Point(100, 7);
            this.browserUpButton.Name = "browserUpButton";
            this.browserUpButton.Size = new System.Drawing.Size(80, 38);
            this.browserUpButton.TabIndex = 1;
            this.browserUpButton.Text = "Up";
            //
            // browserRefreshButton
            //
            this.browserRefreshButton.AccessibleName = "Refresh files";
            this.browserRefreshButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.browserRefreshButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Refresh;
            this.browserRefreshButton.Location = new System.Drawing.Point(188, 7);
            this.browserRefreshButton.Name = "browserRefreshButton";
            this.browserRefreshButton.Size = new System.Drawing.Size(112, 38);
            this.browserRefreshButton.TabIndex = 2;
            this.browserRefreshButton.Text = "Refresh";
            //
            // browserNewFolderButton
            //
            this.browserNewFolderButton.AccessibleName = "Create a new folder";
            this.browserNewFolderButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.browserNewFolderButton.Icon = CloudflareR2Uploader.Controls.AppIcon.Plus;
            this.browserNewFolderButton.Location = new System.Drawing.Point(308, 7);
            this.browserNewFolderButton.Name = "browserNewFolderButton";
            this.browserNewFolderButton.Size = new System.Drawing.Size(132, 38);
            this.browserNewFolderButton.TabIndex = 3;
            this.browserNewFolderButton.Text = "New folder";
            //
            // browserPathLabel
            //
            this.browserPathLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.browserPathLabel.AutoEllipsis = true;
            this.browserPathLabel.Location = new System.Drawing.Point(454, 7);
            this.browserPathLabel.Name = "browserPathLabel";
            this.browserPathLabel.Padding = new System.Windows.Forms.Padding(12, 0, 12, 0);
            this.browserPathLabel.Size = new System.Drawing.Size(600, 38);
            this.browserPathLabel.TabIndex = 4;
            this.browserPathLabel.Text = "Bucket root";
            this.browserPathLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // browserFooterPanel
            //
            this.browserFooterPanel.Controls.Add(this.browserStatusLabel);
            this.browserFooterPanel.Controls.Add(this.browserPreviousButton);
            this.browserFooterPanel.Controls.Add(this.browserNextButton);
            this.browserFooterPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.browserFooterPanel.Location = new System.Drawing.Point(14, 639);
            this.browserFooterPanel.Name = "browserFooterPanel";
            this.browserFooterPanel.Size = new System.Drawing.Size(1054, 50);
            this.browserFooterPanel.TabIndex = 3;
            //
            // browserStatusLabel
            //
            this.browserStatusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.browserStatusLabel.AutoEllipsis = true;
            this.browserStatusLabel.Location = new System.Drawing.Point(0, 7);
            this.browserStatusLabel.Name = "browserStatusLabel";
            this.browserStatusLabel.Size = new System.Drawing.Size(818, 36);
            this.browserStatusLabel.TabIndex = 0;
            this.browserStatusLabel.Text = "Open Files to load this bucket.";
            this.browserStatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // browserPreviousButton
            //
            this.browserPreviousButton.AccessibleName = "Previous object page";
            this.browserPreviousButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.browserPreviousButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.browserPreviousButton.Location = new System.Drawing.Point(826, 7);
            this.browserPreviousButton.Name = "browserPreviousButton";
            this.browserPreviousButton.Size = new System.Drawing.Size(106, 36);
            this.browserPreviousButton.TabIndex = 1;
            this.browserPreviousButton.Text = "Previous";
            //
            // browserNextButton
            //
            this.browserNextButton.AccessibleName = "Next object page";
            this.browserNextButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.browserNextButton.ButtonStyle = CloudflareR2Uploader.Controls.ModernButtonStyle.Secondary;
            this.browserNextButton.Location = new System.Drawing.Point(940, 7);
            this.browserNextButton.Name = "browserNextButton";
            this.browserNextButton.Size = new System.Drawing.Size(114, 36);
            this.browserNextButton.TabIndex = 2;
            this.browserNextButton.Text = "Next";
            //
            // uiTimer
            //
            this.uiTimer.Interval = 400;
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(1120, 850);
            this.Controls.Add(this.rootLayout);
            this.MinimumSize = new System.Drawing.Size(940, 760);
            this.Name = "MainForm";
            this.Padding = new System.Windows.Forms.Padding(16, 12, 16, 12);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Cloudflare R2 Uploader";
            this.rootLayout.ResumeLayout(false);
            this.headerPanel.ResumeLayout(false);
            this.headerPanel.PerformLayout();
            this.pageNavigationPanel.ResumeLayout(false);
            this.destinationCard.ResumeLayout(false);
            this.destinationLayout.ResumeLayout(false);
            this.destinationLayout.PerformLayout();
            this.queueCard.ResumeLayout(false);
            this.queueToolbar.ResumeLayout(false);
            this.statusCard.ResumeLayout(false);
            this.statusCard.PerformLayout();
            this.bucketBrowserPagePanel.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.browserGrid)).EndInit();
            this.browserToolbarPanel.ResumeLayout(false);
            this.browserFooterPanel.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.Panel headerPanel;
        private CloudflareR2Uploader.Controls.BrandMarkControl brandMark;
        private System.Windows.Forms.Label brandEyebrowLabel;
        private System.Windows.Forms.Label appTitleLabel;
        private CloudflareR2Uploader.Controls.CircularStatusIndicator connectionIndicator;
        private System.Windows.Forms.Label connectionLabel;
        private System.Windows.Forms.Label bucketBadgeLabel;
        private CloudflareR2Uploader.Controls.ModernButton settingsButton;
        private System.Windows.Forms.Panel pageNavigationPanel;
        private CloudflareR2Uploader.Controls.ModernButton uploadPageButton;
        private CloudflareR2Uploader.Controls.ModernButton bucketFilesPageButton;
        private CloudflareR2Uploader.Controls.UploadDropZone dropZone;
        private CloudflareR2Uploader.Controls.CardPanel destinationCard;
        private System.Windows.Forms.TableLayoutPanel destinationLayout;
        private System.Windows.Forms.Label prefixLabel;
        private CloudflareR2Uploader.Controls.ModernTextBox prefixTextBox;
        private System.Windows.Forms.Label overwriteLabel;
        private CloudflareR2Uploader.Controls.ModernComboBox overwriteComboBox;
        private System.Windows.Forms.Label customNameLabel;
        private CloudflareR2Uploader.Controls.ModernTextBox customNameTextBox;
        private System.Windows.Forms.CheckBox preserveStructureCheckBox;
        private CloudflareR2Uploader.Controls.CardPanel queueCard;
        private System.Windows.Forms.Panel queueToolbar;
        private CloudflareR2Uploader.Controls.ModernButton uploadAllButton;
        private CloudflareR2Uploader.Controls.ModernButton pauseButton;
        private CloudflareR2Uploader.Controls.ModernButton cancelButton;
        private CloudflareR2Uploader.Controls.ModernButton retryFailedButton;
        private CloudflareR2Uploader.Controls.ModernButton clearCompletedButton;
        private System.Windows.Forms.Label queueSummaryLabel;
        private System.Windows.Forms.FlowLayoutPanel queueListPanel;
        private System.Windows.Forms.Label emptyQueueLabel;
        private CloudflareR2Uploader.Controls.CardPanel statusCard;
        private CloudflareR2Uploader.Controls.ModernProgressBar overallProgressBar;
        private System.Windows.Forms.Label overallPercentLabel;
        private System.Windows.Forms.Label overallTransferLabel;
        private System.Windows.Forms.Label overallSpeedLabel;
        private System.Windows.Forms.Label overallTimeLabel;
        private System.Windows.Forms.Label overallCountsLabel;
        private System.Windows.Forms.Panel bucketBrowserPagePanel;
        private System.Windows.Forms.DataGridView browserGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn browserNameColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn browserTypeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn browserSizeColumn;
        private System.Windows.Forms.DataGridViewTextBoxColumn browserModifiedColumn;
        private System.Windows.Forms.Label browserStateLabel;
        private System.Windows.Forms.Panel browserToolbarPanel;
        private CloudflareR2Uploader.Controls.ModernButton browserBackButton;
        private CloudflareR2Uploader.Controls.ModernButton browserUpButton;
        private CloudflareR2Uploader.Controls.ModernButton browserRefreshButton;
        private CloudflareR2Uploader.Controls.ModernButton browserNewFolderButton;
        private System.Windows.Forms.Label browserPathLabel;
        private System.Windows.Forms.Panel browserFooterPanel;
        private System.Windows.Forms.Label browserStatusLabel;
        private CloudflareR2Uploader.Controls.ModernButton browserPreviousButton;
        private CloudflareR2Uploader.Controls.ModernButton browserNextButton;
        private System.Windows.Forms.Timer uiTimer;
        private System.Windows.Forms.ToolTip toolTip;
    }
}
