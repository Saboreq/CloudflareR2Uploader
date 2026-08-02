using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CloudflareR2Uploader.Controls;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Theming;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Forms
{
    /// <summary>
    /// The main window: drop area, destination settings, upload queue and overall status.
    /// <para>
    /// This form contains presentation and coordination only. Everything to do with R2 lives
    /// in the services it drives, and no network or file work happens on the UI thread.
    /// </para>
    /// </summary>
    public partial class MainForm : Form, IOverwritePrompt
    {
        private readonly ILoggingService _log;
        private readonly SettingsService _settingsService;
        private readonly CredentialProtectionService _credentialService;
        private readonly UploadStateStore _stateStore;
        private readonly UploadQueueService _queueService;
        private readonly R2ConnectionTester _connectionTester;

        private readonly Dictionary<Guid, UploadQueueItemControl> _rows = new Dictionary<Guid, UploadQueueItemControl>();
        private Dictionary<string, R2Credentials> _credentialsByProfile =
            new Dictionary<string, R2Credentials>(StringComparer.OrdinalIgnoreCase);

        private AppSettings _settings;
        private R2Credentials _credentials = R2Credentials.Empty;
        private ModernComboBox _bucketSelectorComboBox;

        private bool _connectionVerified;
        private bool _suppressDestinationEvents;
        private bool _suppressBucketSelection;
        private CancellationTokenSource _backgroundTest;

        /// <summary>Sticky answer when the user ticked "do the same for the rest of this queue".</summary>
        private OverwriteDecision? _stickyOverwriteDecision;

        public MainForm(ILoggingService log)
        {
            if (log != null) log.Debug("App.Startup", "Creating the main window controls.");
            InitializeComponent();
            CreateBucketSelector();
            if (log != null) log.Debug("App.Startup", "Main window controls created.");
            DoubleBuffered = true;

            _log = log;
            _settingsService = new SettingsService(log);
            _credentialService = new CredentialProtectionService(log);
            _stateStore = new UploadStateStore(log);
            _connectionTester = new R2ConnectionTester(log);
            _objectBrowserService = new R2ObjectBrowserService(log);
            _objectOperationService = new R2ObjectOperationService(log, _stateStore);

            _queueService = new UploadQueueService(log, _stateStore)
            {
                OverwritePrompt = this,
                SettingsProvider = () => _settings,
                CredentialsProvider = () => _credentials
            };

            Icon = Program.LoadApplicationIcon();
            Text = "Cloudflare R2 Uploader " + Program.GetVersion();

            ApplyTheme();
            if (_log != null) _log.Debug("App.Startup", "Main window theme applied.");
            PopulateOverwriteChoices();
            ApplyTooltips();
            WireEvents();
            InitializeBrowserActions();
            InitializePageTransition();

            LoadSettings();
            if (_log != null) _log.Debug("App.Startup", "Local settings loaded.");
            UpdateDestinationControls();
            UpdateConnectionDisplay();
            UpdateCommandStates();
            UpdateQueueVisibility();
            UpdateBrowserPathDisplay();
            UpdateBrowserCommandStates();
        }

        // -------------------------------------------------------------------------- set-up

        private void ApplyTheme()
        {
            BackColor = Theme.WindowBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;

            foreach (Control container in new Control[] { rootLayout, headerPanel, destinationLayout, queueToolbar })
            {
                container.BackColor = Color.Transparent;
            }

            ApplyBrowserTheme();

            queueListPanel.BackColor = Theme.CardBackground;

            brandEyebrowLabel.Font = ThemeFonts.Eyebrow;
            brandEyebrowLabel.ForeColor = Theme.AccentBright;
            brandEyebrowLabel.BackColor = Color.Transparent;

            appTitleLabel.Font = ThemeFonts.BrandTitle;
            appTitleLabel.ForeColor = Theme.TextPrimary;
            appTitleLabel.BackColor = Color.Transparent;

            connectionLabel.Font = ThemeFonts.Caption;
            connectionLabel.ForeColor = Theme.TextSecondary;
            connectionLabel.BackColor = Color.Transparent;

            bucketBadgeLabel.Font = ThemeFonts.Caption;
            bucketBadgeLabel.ForeColor = Theme.AccentBright;
            bucketBadgeLabel.BackColor = Color.Transparent;
            bucketBadgeLabel.Text = "TARGET BUCKET";

            _bucketSelectorComboBox.BackColor = Theme.InputBackground;
            _bucketSelectorComboBox.ForeColor = Theme.TextPrimary;

            foreach (Label label in new[] { prefixLabel, overwriteLabel, customNameLabel })
            {
                label.Font = ThemeFonts.BodySmall;
                label.ForeColor = Theme.TextSecondary;
            }

            preserveStructureCheckBox.Font = ThemeFonts.Body;
            preserveStructureCheckBox.ForeColor = Theme.TextSecondary;
            preserveStructureCheckBox.BackColor = Color.Transparent;
            preserveStructureCheckBox.FlatStyle = FlatStyle.Flat;

            queueSummaryLabel.Font = ThemeFonts.Caption;
            queueSummaryLabel.ForeColor = Theme.TextMuted;
            queueSummaryLabel.BackColor = Color.Transparent;

            emptyQueueLabel.Font = ThemeFonts.Body;
            emptyQueueLabel.ForeColor = Theme.TextMuted;
            emptyQueueLabel.BackColor = Color.Transparent;

            overallPercentLabel.Font = ThemeFonts.Metric;
            overallPercentLabel.ForeColor = Theme.TextPrimary;
            overallPercentLabel.BackColor = Color.Transparent;

            foreach (Label label in new[] { overallTransferLabel, overallSpeedLabel, overallTimeLabel, overallCountsLabel })
            {
                label.Font = ThemeFonts.Caption;
                label.ForeColor = Theme.TextMuted;
                label.BackColor = Color.Transparent;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.WindowBackground);

            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                DrawAmbientGlow(g, new Rectangle(-180, -190, 760, 480), Theme.Accent, 42);
                DrawAmbientGlow(g, new Rectangle(Width - 430, 10, 560, 380), Theme.Cyan, 13);

                int gridStart = Math.Max(Height / 2, 360);
                using (Pen grid = new Pen(Theme.WithAlpha(Theme.TextPrimary, 6), 1f))
                {
                    const int spacing = 52;
                    for (int x = 0; x < Width; x += spacing)
                        g.DrawLine(grid, x, gridStart, x, Height);
                    for (int y = gridStart; y < Height; y += spacing)
                        g.DrawLine(grid, 0, y, Width, y);
                }
            }
            finally
            {
                g.SmoothingMode = previous;
            }
        }

        private static void DrawAmbientGlow(Graphics g, Rectangle bounds, Color color, int alpha)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(bounds);
                using (PathGradientBrush glow = new PathGradientBrush(path))
                {
                    glow.CenterColor = Theme.WithAlpha(color, alpha);
                    glow.SurroundColors = new[] { Theme.WithAlpha(color, 0) };
                    g.FillEllipse(glow, bounds);
                }
            }
        }

        private void PopulateOverwriteChoices()
        {
            overwriteComboBox.Items.Clear();
            overwriteComboBox.Items.Add("Ask each time");
            overwriteComboBox.Items.Add("Overwrite");
            overwriteComboBox.Items.Add("Skip");
            overwriteComboBox.Items.Add("Rename to (1), (2)…");
            overwriteComboBox.SelectedIndex = 0;
        }

        private void CreateBucketSelector()
        {
            _bucketSelectorComboBox = new ModernComboBox
            {
                Name = "bucketSelectorComboBox",
                Size = new Size(238, 36),
                ItemHeight = 24,
                AccessibleName = "Target bucket",
                AccessibleDescription = "Select which saved R2 bucket receives queued uploads."
            };

            headerPanel.Controls.Add(_bucketSelectorComboBox);
            _bucketSelectorComboBox.BringToFront();
            PositionBucketSelector();
        }

        private void ApplyTooltips()
        {
            toolTip.SetToolTip(settingsButton, "Connection and transfer settings (Ctrl+,)");
            toolTip.SetToolTip(uploadAllButton, "Start uploading everything in the queue (Ctrl+U)");
            toolTip.SetToolTip(pauseButton, "Stop scheduling new parts; parts already in flight finish and are kept for resume");
            toolTip.SetToolTip(cancelButton, "Stop the run, choosing whether to keep the uploaded parts");
            toolTip.SetToolTip(retryFailedButton, "Requeue every failed file; resumable multipart uploads carry on rather than restart");
            toolTip.SetToolTip(clearCompletedButton, "Remove completed and skipped rows from the list");
            toolTip.SetToolTip(prefixTextBox, "Folder inside the bucket. Backslashes become '/', and leading or duplicate slashes are removed.");
            toolTip.SetToolTip(customNameTextBox, "Rename the object. Only applied when exactly one file is queued.");
            toolTip.SetToolTip(preserveStructureCheckBox, "Keep the folder layout of anything added from a folder.");
            toolTip.SetToolTip(overwriteComboBox, "What to do when an object with the same key already exists.");
            toolTip.SetToolTip(_bucketSelectorComboBox, "Choose the saved R2 bucket that receives uploads. Manage buckets in Settings.");
            ApplyBrowserTooltips();
        }

        private void WireEvents()
        {
            dropZone.BrowseFilesRequested += OnBrowseFiles;
            dropZone.BrowseFolderRequested += OnBrowseFolder;
            dropZone.PathsDropped += OnPathsDropped;

            settingsButton.Click += OnSettingsClick;
            uploadAllButton.Click += OnUploadAllClick;
            pauseButton.Click += OnPauseClick;
            cancelButton.Click += OnCancelClick;
            retryFailedButton.Click += OnRetryFailedClick;
            clearCompletedButton.Click += OnClearCompletedClick;

            prefixTextBox.TextChanged += OnDestinationChanged;
            customNameTextBox.TextChanged += OnDestinationChanged;
            preserveStructureCheckBox.CheckedChanged += OnDestinationChanged;
            overwriteComboBox.SelectedIndexChanged += OnDestinationChanged;
            _bucketSelectorComboBox.SelectedIndexChanged += OnBucketSelectionChanged;
            WireBrowserEvents();

            _queueService.ItemAdded += OnQueueItemAdded;
            _queueService.ItemRemoved += OnQueueItemRemoved;
            _queueService.ItemFinished += OnQueueItemFinished;
            _queueService.RunStateChanged += OnRunStateChanged;
            _queueService.UploadService.MultipartUploader.ResumeRejected += OnResumeRejected;

            queueListPanel.Resize += OnQueueListResize;
            headerPanel.Resize += OnHeaderPanelResize;
            uiTimer.Tick += OnUiTimerTick;

            // The whole window accepts drops, not just the zone, which is what people expect.
            AllowDrop = true;
            DragEnter += OnFormDragEnter;
            DragDrop += OnFormDragDrop;

            KeyPreview = true;
            KeyDown += OnFormKeyDown;
        }

        private void LoadSettings()
        {
            _settings = _settingsService.Load();
            _settings.NormalizeBucketProfiles();

            // A leftover encrypted blob may still exist if the user disabled remembering but
            // declined its deletion. Respect the setting: never bring that blob back into
            // memory unless credential persistence is explicitly enabled.
            _credentialsByProfile = _settings.RememberCredentials
                ? _credentialService.LoadProfiles(_settings.ActiveBucketProfileId)
                : new Dictionary<string, R2Credentials>(StringComparer.OrdinalIgnoreCase);
            _credentials = CredentialsForProfile(_settings.ActiveBucketProfileId);

            if (_credentials.IsComplete)
            {
                if (_log != null)
                    _log.Info("App.Settings", "Loaded saved credentials for '" +
                        _settings.BucketName + "' (" + _credentials.ToLogSafeString() + ").");
            }

            PopulateBucketSelector();

            _suppressDestinationEvents = true;
            try
            {
                prefixTextBox.Text = _settings.KeyPrefix;
                preserveStructureCheckBox.Checked = _settings.PreserveFolderStructure;
                overwriteComboBox.SelectedIndex = (int)_settings.OverwriteBehavior;
            }
            finally
            {
                _suppressDestinationEvents = false;
            }

            _stateStore.CleanupStale();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DarkTitleBar.Apply(this);
            DarkScrollBars.Apply(queueListPanel);
            DarkScrollBars.Apply(browserGrid);

            uiTimer.Start();

            // Verify the stored connection in the background so uploading is enabled without
            // the user having to open Settings first.
            if (HasEnoughToConnect()) StartBackgroundConnectionTest();
            else
            {
                SetConnectionState(ConnectionIndicatorState.Unknown, "Not configured — open Settings to add your R2 credentials");
            }
        }

        private bool HasEnoughToConnect()
        {
            return _settings != null
                   && !string.IsNullOrWhiteSpace(_settings.BucketName)
                   && (!string.IsNullOrWhiteSpace(_settings.AccountId) || !string.IsNullOrWhiteSpace(_settings.CustomEndpoint))
                   && _credentials != null
                   && _credentials.IsComplete;
        }

        // --------------------------------------------------------------------- connection

        private async void StartBackgroundConnectionTest()
        {
            CancelBackgroundConnectionTest();

            string profileId = _settings.ActiveBucketProfileId;
            AppSettings settings = _settings.Clone();
            R2Credentials credentials = _credentials;
            CancellationTokenSource cancellation = new CancellationTokenSource();
            _backgroundTest = cancellation;
            if (_browserPageActive) ShowBrowserAvailabilityState();

            SetConnectionState(ConnectionIndicatorState.Connecting, "Checking access to " + settings.BucketName + "…");
            UpdateCommandStates();

            try
            {
                ConnectionTestResult result = await _connectionTester
                    .TestAsync(settings, credentials, cancellation.Token)
                    .ConfigureAwait(true);

                if (_backgroundTest != cancellation ||
                    !string.Equals(_settings.ActiveBucketProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                    return;

                _connectionVerified = result.IsSuccess;

                SetConnectionState(
                    result.IsSuccess ? ConnectionIndicatorState.Connected : ConnectionIndicatorState.Error,
                    result.IsSuccess ? "Connected to " + settings.BucketName : result.Headline);

                if (!result.IsSuccess && _log != null)
                    _log.Warning("App.Connection", "Startup connection check failed: " + result.Headline);

                ShowBrowserConnectionResult(result);
            }
            catch (OperationCanceledException)
            {
                // Switching target buckets intentionally cancels the previous probe.
            }
            finally
            {
                if (_backgroundTest == cancellation)
                {
                    _backgroundTest = null;
                    UpdateConnectionDisplay();
                    UpdateCommandStates();
                }
                cancellation.Dispose();
            }
        }

        private void CancelBackgroundConnectionTest()
        {
            CancellationTokenSource cancellation = _backgroundTest;
            if (cancellation == null) return;

            _backgroundTest = null;
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private void SetConnectionState(ConnectionIndicatorState state, string text)
        {
            connectionIndicator.State = state;
            connectionLabel.Text = text;
            connectionLabel.ForeColor = state == ConnectionIndicatorState.Connected
                ? Theme.Success
                : (state == ConnectionIndicatorState.Error ? Theme.Error : Theme.TextSecondary);

            headerPanel.AccessibleDescription = "Connection status: " + text;
            PositionBucketSelector();
        }

        private void UpdateConnectionDisplay()
        {
            string endpoint = _settings != null ? R2ClientFactory.DescribeEndpoint(_settings) : string.Empty;
            toolTip.SetToolTip(_bucketSelectorComboBox,
                string.IsNullOrEmpty(endpoint)
                    ? "Choose the saved R2 bucket that receives uploads."
                    : "Uploads go to " + endpoint + ".");
            PositionBucketSelector();
        }

        private void PositionBucketSelector()
        {
            if (_bucketSelectorComboBox == null || settingsButton == null) return;

            int left = Math.Max(430, settingsButton.Left - _bucketSelectorComboBox.Width - 16);
            bucketBadgeLabel.Location = new Point(left, 0);
            _bucketSelectorComboBox.Location = new Point(left, 18);

            connectionLabel.AutoSize = false;
            connectionLabel.Size = new Size(Math.Max(120, left - connectionLabel.Left - 18), 20);
            connectionLabel.AutoEllipsis = true;
            bucketBadgeLabel.Visible = true;
            _bucketSelectorComboBox.Visible = true;
        }

        private void OnHeaderPanelResize(object sender, EventArgs e)
        {
            PositionBucketSelector();
        }

        private void PopulateBucketSelector()
        {
            _suppressBucketSelection = true;
            try
            {
                _bucketSelectorComboBox.Items.Clear();
                if (_settings == null) return;

                _settings.NormalizeBucketProfiles();
                int selectedIndex = -1;
                for (int i = 0; i < _settings.BucketProfiles.Count; i++)
                {
                    R2BucketProfile profile = _settings.BucketProfiles[i];
                    _bucketSelectorComboBox.Items.Add(new BucketProfileChoice(profile));
                    if (string.Equals(profile.Id, _settings.ActiveBucketProfileId, StringComparison.OrdinalIgnoreCase))
                        selectedIndex = i;
                }

                _bucketSelectorComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            }
            finally
            {
                _suppressBucketSelection = false;
            }
        }

        private void OnBucketSelectionChanged(object sender, EventArgs e)
        {
            if (_suppressBucketSelection || _settings == null || _queueService.IsRunning) return;

            BucketProfileChoice choice = _bucketSelectorComboBox.SelectedItem as BucketProfileChoice;
            if (choice == null ||
                string.Equals(choice.Profile.Id, _settings.ActiveBucketProfileId, StringComparison.OrdinalIgnoreCase))
                return;

            CancelBackgroundConnectionTest();
            _settings.SelectBucketProfile(choice.Profile.Id);
            _credentials = CredentialsForProfile(choice.Profile.Id);
            _connectionVerified = false;
            _settingsService.Save(_settings);
            ResetBrowserForTargetChange();

            UpdateConnectionDisplay();
            if (HasEnoughToConnect())
                StartBackgroundConnectionTest();
            else
                SetConnectionState(ConnectionIndicatorState.Unknown,
                    "No credentials saved for " + _settings.BucketName + " — open Settings");

            UpdateCommandStates();
        }

        private R2Credentials CredentialsForProfile(string profileId)
        {
            R2Credentials credentials;
            return !string.IsNullOrWhiteSpace(profileId) &&
                   _credentialsByProfile != null &&
                   _credentialsByProfile.TryGetValue(profileId, out credentials) &&
                   credentials != null
                ? credentials
                : R2Credentials.Empty;
        }

        private sealed class BucketProfileChoice
        {
            public BucketProfileChoice(R2BucketProfile profile) { Profile = profile; }
            public R2BucketProfile Profile { get; private set; }
            public override string ToString() { return Profile.DisplayName; }
        }

        // --------------------------------------------------------------------- adding files

        private void OnBrowseFiles(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select files to upload to R2",
                Multiselect = true,
                CheckFileExists = true,
                Filter = "All files (*.*)|*.*"
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) AddPaths(dialog.FileNames);
            }
        }

        private void OnBrowseFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog
            {
                Description = "Select a folder. Every file inside it, including subfolders, is added.",
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                    AddPaths(new[] { dialog.SelectedPath });
            }
        }

        private void OnPathsDropped(object sender, PathsDroppedEventArgs e)
        {
            AddPaths(e.Paths);
        }

        private void OnFormDragEnter(object sender, DragEventArgs e)
        {
            if (_browserPageActive)
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void OnFormDragDrop(object sender, DragEventArgs e)
        {
            if (_browserPageActive) return;
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null && paths.Length > 0) AddPaths(paths);
        }

        private void AddPaths(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;

            Cursor previous = Cursor;
            Cursor = Cursors.WaitCursor;

            AddFilesResult result;
            try
            {
                result = _queueService.AddPaths(paths, _settings);
            }
            finally
            {
                Cursor = previous;
            }

            UpdateDestinationControls();
            UpdateQueueVisibility();
            UpdateCommandStates();

            if (result.Inaccessible.Count > 0 || result.Unreadable.Count > 0)
            {
                ReportSkippedPaths(result);
            }
        }

        private void ReportSkippedPaths(AddFilesResult result)
        {
            System.Text.StringBuilder message = new System.Text.StringBuilder();
            message.Append(result.Added).AppendLine(" file(s) were added.").AppendLine();

            if (result.Unreadable.Count > 0)
            {
                message.Append(result.Unreadable.Count).AppendLine(" file(s) could not be read and were skipped.");
            }

            if (result.Inaccessible.Count > 0)
            {
                message.Append(result.Inaccessible.Count).AppendLine(" folder(s) could not be read and were skipped.");
            }

            System.Text.StringBuilder details = new System.Text.StringBuilder();
            details.AppendLine("Paths that could not be read:");
            foreach (string path in result.Unreadable) details.AppendLine("  file:   " + path);
            foreach (string path in result.Inaccessible) details.AppendLine("  folder: " + path);

            ErrorDialog.Show(this, "Some items were skipped", message.ToString(), details.ToString());
        }

        // -------------------------------------------------------------------- destination

        private void OnDestinationChanged(object sender, EventArgs e)
        {
            if (_suppressDestinationEvents) return;

            _settings.KeyPrefix = ObjectKeyUtility.Normalize(prefixTextBox.Text);
            _settings.PreserveFolderStructure = preserveStructureCheckBox.Checked;

            int index = overwriteComboBox.SelectedIndex;
            if (index >= 0 && Enum.IsDefined(typeof(OverwriteBehavior), index))
                _settings.OverwriteBehavior = (OverwriteBehavior)index;

            // Changing the policy invalidates any sticky "apply to all" answer.
            _stickyOverwriteDecision = null;

            _queueService.RecomputeKeys(_settings, customNameTextBox.Text);
            UpdateDestinationControls();
            RefreshAllRows();
        }

        private void UpdateDestinationControls()
        {
            bool singleFile = _queueService.Count == 1;

            customNameTextBox.Enabled = singleFile;
            customNameLabel.ForeColor = singleFile ? Theme.TextSecondary : Theme.TextDisabled;
            customNameTextBox.PlaceholderText = singleFile
                ? "Leave empty to keep the original file name"
                : "Only used when exactly one file is queued";

            preserveStructureCheckBox.Enabled = true;
        }

        // ------------------------------------------------------------------- queue display

        private void OnQueueItemAdded(object sender, QueueItemEventArgs e)
        {
            UiThreadUtility.Post(this, () => AddRow(e.Item));
        }

        private void AddRow(UploadQueueItem item)
        {
            if (item == null || _rows.ContainsKey(item.Id)) return;

            UploadQueueItemControl row = new UploadQueueItemControl
            {
                Item = item,
                Width = GetRowWidth()
            };

            row.RemoveRequested += OnRowRemoveRequested;
            row.RetryRequested += OnRowRetryRequested;
            row.CopyKeyRequested += OnRowCopyKeyRequested;

            _rows[item.Id] = row;
            queueListPanel.Controls.Add(row);

            UpdateQueueVisibility();
        }

        private void OnQueueItemRemoved(object sender, QueueItemEventArgs e)
        {
            UiThreadUtility.Post(this, () =>
            {
                UploadQueueItemControl row;
                if (!_rows.TryGetValue(e.Item.Id, out row)) return;

                _rows.Remove(e.Item.Id);
                queueListPanel.Controls.Remove(row);

                row.RemoveRequested -= OnRowRemoveRequested;
                row.RetryRequested -= OnRowRetryRequested;
                row.CopyKeyRequested -= OnRowCopyKeyRequested;
                row.Item = null;
                row.Dispose();

                UpdateQueueVisibility();
                UpdateCommandStates();
            });
        }

        private void OnRowRemoveRequested(object sender, QueueItemActionEventArgs e)
        {
            if (e.Item == null) return;

            if (e.Item.Status.IsActive())
            {
                CancelChoice choice = AskCancelChoice(e.Item.TotalParts > 0 && e.Item.TransferredBytes > 0);
                if (choice == CancelChoice.ContinueUploading) return;

                e.Item.PreservePartsOnCancel = choice == CancelChoice.KeepPartsForResume;
                _queueService.CancelItem(e.Item);
                return;
            }

            _queueService.Remove(e.Item);
        }

        private void OnRowRetryRequested(object sender, QueueItemActionEventArgs e)
        {
            if (e.Item == null) return;

            // Pick up the file's current size and timestamp so a legitimately edited file can
            // be re-uploaded rather than failing the "file changed" check for ever.
            string problem;
            if (!e.Item.TryAdoptCurrentFileInfo(out problem))
            {
                ErrorDialog.Show(this, "This file cannot be retried", problem, null);
                return;
            }

            _queueService.RetryItem(e.Item);
            UpdateCommandStates();

            if (!_queueService.IsRunning && _connectionVerified) StartRun();
        }

        private void OnRowCopyKeyRequested(object sender, QueueItemActionEventArgs e)
        {
            if (e.Item == null || string.IsNullOrEmpty(e.Item.ObjectKey)) return;

            string text = !string.IsNullOrEmpty(e.Item.PublicUrl) && e.Item.Status == UploadItemStatus.Completed
                ? e.Item.PublicUrl
                : e.Item.ObjectKey;

            try
            {
                Clipboard.SetText(text);
                queueSummaryLabel.Text = "Copied: " + text;
            }
            catch (ExternalException)
            {
                queueSummaryLabel.Text = "The clipboard is in use by another program.";
            }
        }

        private int GetRowWidth()
        {
            int width = queueListPanel.ClientSize.Width - 6;
            // Leave room for the vertical scrollbar so rows never sit under it.
            if (queueListPanel.VerticalScroll.Visible) width -= SystemInformation.VerticalScrollBarWidth;
            return Math.Max(320, width);
        }

        private void OnQueueListResize(object sender, EventArgs e)
        {
            int width = GetRowWidth();
            foreach (UploadQueueItemControl row in _rows.Values)
            {
                if (row.Width != width) row.Width = width;
            }
        }

        private void RefreshAllRows()
        {
            foreach (UploadQueueItemControl row in _rows.Values) row.Invalidate();
        }

        private void UpdateQueueVisibility()
        {
            bool hasItems = _rows.Count > 0;
            emptyQueueLabel.Visible = !hasItems;
            queueListPanel.Visible = hasItems;

            if (hasItems) OnQueueListResize(this, EventArgs.Empty);
        }

        // ----------------------------------------------------------------- run commands

        private void OnUploadAllClick(object sender, EventArgs e)
        {
            if (_queueService.IsPaused)
            {
                _queueService.Resume();
                UpdateCommandStates();
                return;
            }

            StartRun();
        }

        private void StartRun()
        {
            if (!_connectionVerified)
            {
                ErrorDialog.Show(this, "The connection has not been verified",
                    "Open Settings and use Test connection first. Uploading stays disabled until the bucket is reachable with the current credentials.",
                    null);
                return;
            }

            string problem = ValidateQueueKeys();
            if (problem != null)
            {
                ErrorDialog.Show(this, "The destination is not valid", problem, null);
                return;
            }

            _stickyOverwriteDecision = null;

            // Fire and forget: the queue service owns the task, and every failure inside it is
            // reported through the queue rows rather than by throwing.
            Task runTask = _queueService.StartAsync();
            ObserveRunTask(runTask);

            UpdateCommandStates();
        }

        private async void ObserveRunTask(Task runTask)
        {
            if (runTask == null) return;

            try
            {
                await runTask.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Error("App.Run", "The upload run ended with an unhandled error.", ex);

                FriendlyError friendly = FriendlyErrorService.Describe(ex, _settings, null);
                ErrorDialog.Show(this, friendly.Headline, friendly.Detail, friendly.TechnicalDetails);
            }
            finally
            {
                UpdateCommandStates();
                UpdateOverallProgress();
            }
        }

        private string ValidateQueueKeys()
        {
            foreach (UploadQueueItem item in _queueService.GetItems())
            {
                if (item.Status.IsTerminal()) continue;

                string reason;
                if (!ObjectKeyUtility.TryValidate(item.ObjectKey, out reason))
                    return "\"" + item.FileName + "\" would be uploaded to an invalid key. " + reason;
            }
            return null;
        }

        private void OnPauseClick(object sender, EventArgs e)
        {
            if (_queueService.IsPaused) _queueService.Resume();
            else _queueService.Pause();

            UpdateCommandStates();
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            bool hasMultipartProgress = false;
            foreach (UploadQueueItem item in _queueService.GetItems())
            {
                if (item.Status.IsActive() && item.TotalParts > 0 && item.CompletedParts > 0)
                {
                    hasMultipartProgress = true;
                    break;
                }
            }

            CancelChoice choice = AskCancelChoice(hasMultipartProgress);
            if (choice == CancelChoice.ContinueUploading) return;

            _queueService.CancelAll(choice == CancelChoice.KeepPartsForResume);
            UpdateCommandStates();
        }

        private CancelChoice AskCancelChoice(bool multipartInProgress)
        {
            using (CancelChoiceDialog dialog = new CancelChoiceDialog(multipartInProgress))
            {
                dialog.ShowDialog(this);
                return dialog.Choice;
            }
        }

        private void OnRetryFailedClick(object sender, EventArgs e)
        {
            int count = _queueService.RetryFailed();
            if (count == 0) return;

            UpdateCommandStates();
            if (!_queueService.IsRunning && _connectionVerified) StartRun();
        }

        private void OnClearCompletedClick(object sender, EventArgs e)
        {
            _queueService.ClearCompleted();
            UpdateDestinationControls();
            UpdateQueueVisibility();
            UpdateCommandStates();
        }

        private void OnRunStateChanged(object sender, EventArgs e)
        {
            UiThreadUtility.Post(this, UpdateCommandStates);
        }

        private void OnQueueItemFinished(object sender, UploadFinishedEventArgs e)
        {
            UiThreadUtility.Post(this, () =>
            {
                UpdateCommandStates();

                if (e.Result != null && e.Result.Outcome == UploadOutcome.Succeeded && _log != null)
                {
                    _log.Info("App.Upload", "Completed " + e.Result.ObjectKey +
                                            " (" + FileSizeFormatter.Format(e.Result.RemoteLength) + ").");
                }

                if (e.Result != null && e.Result.Outcome == UploadOutcome.Succeeded)
                    MarkBrowserNeedsRefresh();
            });
        }

        private void OnResumeRejected(object sender, ResumeRejectedEventArgs e)
        {
            UiThreadUtility.Post(this, () =>
            {
                if (e.Item != null)
                {
                    e.Item.SetStatus(e.Item.Status, "Restarting: " + e.Reason);
                }

                queueSummaryLabel.Text = "Could not resume: " + e.Reason;
            });
        }

        private void UpdateCommandStates()
        {
            bool running = _queueService.IsRunning;
            bool paused = _queueService.IsPaused;

            int queued = 0, failed = 0, completed = 0;
            foreach (UploadQueueItem item in _queueService.GetItems())
            {
                switch (item.Status)
                {
                    case UploadItemStatus.Queued: queued++; break;
                    case UploadItemStatus.Paused: queued++; break;
                    case UploadItemStatus.Failed: failed++; break;
                    case UploadItemStatus.Cancelled: failed++; break;
                    case UploadItemStatus.Completed: completed++; break;
                    case UploadItemStatus.Skipped: completed++; break;
                }
            }

            uploadAllButton.Enabled = !_browserOperationInProgress && _connectionVerified && (queued > 0 || paused);
            uploadAllButton.Text = paused ? "Resume" : "Upload all";
            uploadAllButton.Icon = paused ? AppIcon.Play : AppIcon.UploadArrow;

            pauseButton.Enabled = running;
            pauseButton.Text = paused ? "Resume" : "Pause";
            pauseButton.Icon = paused ? AppIcon.Play : AppIcon.Pause;

            cancelButton.Enabled = running;
            retryFailedButton.Enabled = !_browserOperationInProgress && failed > 0 && _connectionVerified;
            clearCompletedButton.Enabled = completed > 0;

            settingsButton.Enabled = !running && !_browserOperationInProgress;
            if (_bucketSelectorComboBox != null) _bucketSelectorComboBox.Enabled = !running && !_browserOperationInProgress;

            dropZone.BrowseFilesButton.Enabled = !running && !_browserOperationInProgress;
            dropZone.BrowseFolderButton.Enabled = !running && !_browserOperationInProgress;

            if (!_connectionVerified && _queueService.Count > 0)
            {
                uploadAllButton.AccessibleDescription =
                    "Uploading is disabled until a connection to the bucket has been verified in Settings.";
            }
        }

        // ------------------------------------------------------------------ status updates

        private void OnUiTimerTick(object sender, EventArgs e)
        {
            UpdateOverallProgress();
        }

        private void UpdateOverallProgress()
        {
            OverallProgressInfo progress = _queueService.GetOverallProgress();

            overallProgressBar.Value = progress.Fraction;
            overallPercentLabel.Text = progress.Percent.ToString(CultureInfo.CurrentCulture) + "%";

            overallTransferLabel.Text = FileSizeFormatter.FormatProgress(progress.TransferredBytes, progress.TotalBytes);

            overallSpeedLabel.Text = _queueService.IsRunning && !_queueService.IsPaused
                ? FileSizeFormatter.FormatSpeed(progress.BytesPerSecond)
                : (_queueService.IsPaused ? "paused" : "—");

            string remaining = _queueService.IsRunning && !_queueService.IsPaused
                ? DurationFormatter.FormatEta(progress.RemainingBytes, progress.BytesPerSecond)
                : DurationFormatter.Unknown;

            overallTimeLabel.Text = "elapsed " + DurationFormatter.Format(progress.Elapsed) + "  ·  remaining " + remaining;

            overallCountsLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "{0} done  ·  {1} failed  ·  {2} queued  ·  {3} active",
                progress.Succeeded, progress.Failed, progress.Queued, progress.Active);

            int total = _queueService.Count;
            if (!queueSummaryLabel.Text.StartsWith("Copied:", StringComparison.Ordinal) &&
                !queueSummaryLabel.Text.StartsWith("Could not resume:", StringComparison.Ordinal))
            {
                queueSummaryLabel.Text = total == 0
                    ? "0 files"
                    : total + (total == 1 ? " file  ·  " : " files  ·  ") + FileSizeFormatter.Format(progress.TotalBytes);
            }

            statusCard.AccessibleDescription = string.Format(
                CultureInfo.CurrentCulture,
                "Overall progress {0} percent. {1}. {2}.",
                progress.Percent, overallTransferLabel.Text, overallCountsLabel.Text);
        }

        // --------------------------------------------------------------------- settings

        private void OnSettingsClick(object sender, EventArgs e)
        {
            using (SettingsForm dialog = new SettingsForm(
                _log, _settingsService, _credentialService, _settings, _credentialsByProfile))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                AppSettings previous = _settings;
                _settings = dialog.ResultSettings;
                _credentialsByProfile = dialog.ResultCredentialsByProfile;
                _credentials = CredentialsForProfile(_settings.ActiveBucketProfileId);
                PopulateBucketSelector();

                // Keep the destination fields the user set in the main window.
                _settings.KeyPrefix = ObjectKeyUtility.Normalize(prefixTextBox.Text);
                _settings.PreserveFolderStructure = preserveStructureCheckBox.Checked;
                if (overwriteComboBox.SelectedIndex >= 0)
                    _settings.OverwriteBehavior = (OverwriteBehavior)overwriteComboBox.SelectedIndex;

                bool connectionChanged =
                    previous == null ||
                    !string.Equals(previous.AccountId, _settings.AccountId, StringComparison.Ordinal) ||
                    !string.Equals(previous.BucketName, _settings.BucketName, StringComparison.Ordinal) ||
                    !string.Equals(previous.CustomEndpoint, _settings.CustomEndpoint, StringComparison.Ordinal);

                _connectionVerified = dialog.ConnectionVerified;
                ResetBrowserForTargetChange();

                _queueService.RecomputeKeys(_settings, customNameTextBox.Text);
                RefreshAllRows();
                UpdateConnectionDisplay();

                if (_connectionVerified)
                {
                    SetConnectionState(ConnectionIndicatorState.Connected, "Connected to " + _settings.BucketName);
                    if (_browserPageActive) BeginBrowserLoad();
                }
                else if (HasEnoughToConnect())
                {
                    StartBackgroundConnectionTest();
                }
                else
                {
                    SetConnectionState(ConnectionIndicatorState.Unknown, "Not configured — open Settings to add your R2 credentials");
                }

                if (connectionChanged && _log != null)
                    _log.Info("App.Settings", "Connection settings changed to bucket '" + _settings.BucketName + "' at " + R2ClientFactory.DescribeEndpoint(_settings) + ".");

                UpdateCommandStates();
            }
        }

        // ------------------------------------------------------------------ overwrite prompt

        /// <summary>
        /// Called from the upload worker. Marshals to the UI thread, shows the prompt and
        /// hands the answer back without blocking either thread.
        /// </summary>
        public Task<OverwriteDecision> AskAsync(UploadQueueItem item, ExistingObjectInfo existing)
        {
            if (_stickyOverwriteDecision.HasValue)
                return Task.FromResult(_stickyOverwriteDecision.Value);

            TaskCompletionSource<OverwriteDecision> completion = new TaskCompletionSource<OverwriteDecision>();

            UiThreadUtility.Post(this, () =>
            {
                try
                {
                    using (OverwritePromptDialog dialog = new OverwritePromptDialog(item, existing))
                    {
                        dialog.ShowDialog(this);

                        if (dialog.ApplyToAll) _stickyOverwriteDecision = dialog.Decision;
                        completion.TrySetResult(dialog.Decision);
                    }
                }
                catch (Exception ex)
                {
                    if (_log != null) _log.Error("App.OverwritePrompt", "The overwrite prompt failed; skipping the file.", ex);
                    completion.TrySetResult(OverwriteDecision.Skip);
                }
            });

            // If the window is already gone the callback never runs, so never leave the worker
            // waiting for ever.
            if (IsDisposed || !IsHandleCreated) completion.TrySetResult(OverwriteDecision.Skip);

            return completion.Task;
        }

        // ---------------------------------------------------------------------- keyboard

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Alt && e.KeyCode == Keys.B)
            {
                ShowBucketFilesPage();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (HandleBrowserShortcut(e))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.Control && e.KeyCode == Keys.Oemcomma)
            {
                OnSettingsClick(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.U)
            {
                if (uploadAllButton.Enabled) OnUploadAllClick(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.O)
            {
                if (dropZone.BrowseFilesButton.Enabled) OnBrowseFiles(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        // ------------------------------------------------------------------------ closing

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_queueService.IsRunning && e.CloseReason == CloseReason.UserClosing)
            {
                CancelChoice choice = AskCancelChoice(true);

                if (choice == CancelChoice.ContinueUploading)
                {
                    e.Cancel = true;
                    return;
                }

                _queueService.CancelAll(choice == CancelChoice.KeepPartsForResume);
            }

            uiTimer.Stop();

            CancelBackgroundConnectionTest();
            CancelBrowserLoad(true);
            CancelBrowserOperation();

            // Persist the destination fields so the next session starts where this one left off.
            try
            {
                if (_settings != null)
                {
                    _settings.KeyPrefix = ObjectKeyUtility.Normalize(prefixTextBox.Text);
                    _settings.PreserveFolderStructure = preserveStructureCheckBox.Checked;
                    if (overwriteComboBox.SelectedIndex >= 0)
                        _settings.OverwriteBehavior = (OverwriteBehavior)overwriteComboBox.SelectedIndex;

                    _settingsService.Save(_settings);
                }
            }
            catch (IOException ex)
            {
                if (_log != null) _log.Error("App.Exit", "Settings could not be saved on exit.", ex);
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Dispose(bool) belongs to the designer file, so teardown that is not
            // designer-generated happens here instead.
            if (_queueService != null) _queueService.Dispose();
            DisposeBrowserActions();
            DisposePageTransition();
            base.OnFormClosed(e);
        }
    }
}
