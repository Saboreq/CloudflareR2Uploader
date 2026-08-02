using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
    /// Connection and transfer settings.
    /// <para>
    /// The Secret Access Key is masked by default, has a temporary show/hide button, is
    /// re-masked whenever the dialog closes, and is never written to the settings JSON or to
    /// any log line.
    /// </para>
    /// </summary>
    public partial class SettingsForm : Form
    {
        private readonly ILoggingService _log;
        private readonly SettingsService _settingsService;
        private readonly CredentialProtectionService _credentialService;
        private readonly R2ConnectionTester _connectionTester;
        private readonly Dictionary<string, R2Credentials> _workingCredentials;

        private CancellationTokenSource _testCancellation;
        private bool _connectionVerified;
        private bool _suppressProfileSelection;
        private string _currentProfileId;
        private ModernComboBox _profileComboBox;
        private ModernButton _addProfileButton;
        private ModernButton _removeProfileButton;
        private Label _profileLabel;

        public SettingsForm(
            ILoggingService log,
            SettingsService settingsService,
            CredentialProtectionService credentialService,
            AppSettings settings,
            R2Credentials credentials)
            : this(log, settingsService, credentialService, settings,
                CreateLegacyCredentialMap(settings, credentials))
        {
        }

        public SettingsForm(
            ILoggingService log,
            SettingsService settingsService,
            CredentialProtectionService credentialService,
            AppSettings settings,
            IDictionary<string, R2Credentials> credentialsByProfile)
        {
            InitializeComponent();
            CreateProfileControls();

            _log = log;
            _settingsService = settingsService;
            _credentialService = credentialService;
            _connectionTester = new R2ConnectionTester(log);
            _startupRegistration = new StartupRegistrationService(log);
            CreateApplicationBehaviorControls();

            Icon = Program.LoadApplicationIcon();

            ConfigureNumericRanges();
            ApplyTheme();
            ApplyTooltips();
            WireEvents();

            ResultSettings = settings != null ? settings.Clone() : new AppSettings();
            ResultSettings.Clamp();
            _workingCredentials = CopyCredentials(credentialsByProfile);
            _currentProfileId = ResultSettings.ActiveBucketProfileId;

            LoadValues(ResultSettings, CredentialsForProfile(_currentProfileId));
            PopulateProfileSelector();
            ResultCredentials = CredentialsForProfile(_currentProfileId);
            ResultCredentialsByProfile = CopyCredentials(_workingCredentials);
            UpdateResolvedEndpoint();
        }

        /// <summary>The settings as edited. Only meaningful when the dialog returned OK.</summary>
        public AppSettings ResultSettings { get; private set; }

        /// <summary>The credentials as edited. Only meaningful when the dialog returned OK.</summary>
        public R2Credentials ResultCredentials { get; private set; }

        /// <summary>Session credentials keyed by the stable bucket profile identifier.</summary>
        public Dictionary<string, R2Credentials> ResultCredentialsByProfile { get; private set; }

        /// <summary>True when "Test connection" succeeded with the values currently shown.</summary>
        public bool ConnectionVerified { get { return _connectionVerified; } }

        // -------------------------------------------------------------------------- set-up

        private void ConfigureNumericRanges()
        {
            thresholdNumeric.Minimum = AppSettings.MinThresholdMiB;
            thresholdNumeric.Maximum = AppSettings.MaxThresholdMiB;
            thresholdNumeric.Increment = 25;

            partSizeNumeric.Minimum = AppSettings.MinPartSizeMiB;
            partSizeNumeric.Maximum = AppSettings.MaxPartSizeMiB;
            partSizeNumeric.Increment = 8;

            parallelNumeric.Minimum = AppSettings.MinParallelParts;
            parallelNumeric.Maximum = AppSettings.MaxParallelParts;

            retryNumeric.Minimum = AppSettings.MinRetryAttempts;
            retryNumeric.Maximum = AppSettings.MaxRetryAttempts;
        }

        private void CreateProfileControls()
        {
            rootLayout.RowStyles[0].Height = 112F;

            _profileLabel = new Label
            {
                AutoSize = true,
                Name = "profileLabel",
                Text = "SAVED BUCKETS",
                Location = new Point(1, 51)
            };

            _profileComboBox = new ModernComboBox
            {
                Name = "profileComboBox",
                Location = new Point(0, 68),
                Size = new Size(300, 36),
                ItemHeight = 24,
                AccessibleName = "Saved buckets"
            };

            _addProfileButton = new ModernButton
            {
                Name = "addProfileButton",
                Text = "Add bucket",
                Icon = AppIcon.Plus,
                ButtonStyle = ModernButtonStyle.Secondary,
                Size = new Size(112, 36),
                Location = new Point(310, 68)
            };

            _removeProfileButton = new ModernButton
            {
                Name = "removeProfileButton",
                Text = "Remove",
                Icon = AppIcon.Trash,
                ButtonStyle = ModernButtonStyle.Ghost,
                Size = new Size(100, 36),
                Location = new Point(430, 68)
            };

            headerPanel.Controls.Add(_profileLabel);
            headerPanel.Controls.Add(_profileComboBox);
            headerPanel.Controls.Add(_addProfileButton);
            headerPanel.Controls.Add(_removeProfileButton);
            headerPanel.Resize += delegate { PositionProfileControls(); };
            PositionProfileControls();
        }

        private void PositionProfileControls()
        {
            if (_profileComboBox == null) return;

            int buttonsWidth = _addProfileButton.Width + _removeProfileButton.Width + 20;
            _profileComboBox.Width = Math.Max(180, headerPanel.ClientSize.Width - buttonsWidth);
            _addProfileButton.Left = _profileComboBox.Right + 10;
            _removeProfileButton.Left = _addProfileButton.Right + 10;
        }

        private void ApplyTheme()
        {
            BackColor = Theme.WindowBackground;
            ForeColor = Theme.TextPrimary;
            Font = ThemeFonts.Body;

            foreach (Control container in new Control[] { rootLayout, headerPanel, scrollPanel, formLayout, statusPanel, buttonLayout, leftButtonPanel, rightButtonPanel })
            {
                container.BackColor = Color.Transparent;
            }

            titleLabel.Font = ThemeFonts.Title;
            titleLabel.ForeColor = Theme.TextPrimary;

            subtitleLabel.Font = ThemeFonts.Caption;
            subtitleLabel.ForeColor = Theme.TextMuted;

            _profileLabel.Font = ThemeFonts.Eyebrow;
            _profileLabel.ForeColor = Theme.AccentBright;
            _profileLabel.BackColor = Color.Transparent;

            foreach (Label header in new[] { connectionHeaderLabel, transferHeaderLabel, securityHeaderLabel })
            {
                header.Font = ThemeFonts.Eyebrow;
                header.ForeColor = Theme.AccentBright;
            }

            foreach (Label label in new[] { accountIdLabel, bucketLabel, accessKeyIdLabel, secretKeyLabel, endpointLabel, publicUrlLabel, thresholdLabel, partSizeLabel, parallelLabel, retryLabel })
            {
                label.Font = ThemeFonts.Body;
                label.ForeColor = Theme.TextSecondary;
            }

            credentialHintLabel.Font = ThemeFonts.Caption;
            credentialHintLabel.ForeColor = Theme.TextMuted;
            credentialHintLabel.Text =
                "These are the R2 S3 credentials — an Access Key ID and a Secret Access Key created with an R2 API token. " +
                "They are not the same thing as a Cloudflare dashboard API token string, which will not work here. " +
                "Create the token in Cloudflare dashboard → R2 → API → Manage API tokens, and copy the S3 credentials it shows.";

            securityHintLabel.Font = ThemeFonts.Caption;
            securityHintLabel.ForeColor = Theme.TextMuted;
            securityHintLabel.Text =
                "When enabled, both keys are encrypted with Windows DPAPI for your Windows account only and stored in " +
                AppPaths.Root + ". They are never written in plain text and never appear in the logs.";

            resolvedEndpointLabel.Font = ThemeFonts.Mono;
            resolvedEndpointLabel.ForeColor = Theme.TextMuted;

            rememberCheckBox.Font = ThemeFonts.Body;
            rememberCheckBox.ForeColor = Theme.TextSecondary;
            rememberCheckBox.BackColor = Color.Transparent;
            rememberCheckBox.FlatStyle = FlatStyle.Flat;

            statusHeadlineLabel.Font = ThemeFonts.BodyBold;
            statusHeadlineLabel.ForeColor = Theme.TextSecondary;

            statusDetailLabel.Font = ThemeFonts.Caption;
            statusDetailLabel.ForeColor = Theme.TextMuted;

            foreach (NumericUpDown numeric in new[] { thresholdNumeric, partSizeNumeric, parallelNumeric, retryNumeric })
            {
                numeric.BackColor = Theme.InputBackground;
                numeric.ForeColor = Theme.TextPrimary;
                numeric.BorderStyle = BorderStyle.FixedSingle;
                numeric.Font = ThemeFonts.Body;
            }
        }

        private void ApplyTooltips()
        {
            toolTip.SetToolTip(accountIdTextBox, "Cloudflare dashboard → R2 → Overview. The endpoint is built from this ID.");
            toolTip.SetToolTip(bucketTextBox, "The exact name of the R2 bucket to upload into.");
            toolTip.SetToolTip(accessKeyIdTextBox, "The Access Key ID from the R2 API token's S3 credentials.");
            toolTip.SetToolTip(secretKeyTextBox, "The Secret Access Key. Cloudflare shows it only once, when the token is created.");
            toolTip.SetToolTip(endpointTextBox, "Leave empty. Only needed for a non-standard or proxied R2 endpoint. Must be HTTPS.");
            toolTip.SetToolTip(publicUrlTextBox, "Optional. Used only to show a shareable link after an upload; it is never used to upload.");
            toolTip.SetToolTip(thresholdNumeric, "Files at or above this size are uploaded as a multipart upload. Default 100 MiB.");
            toolTip.SetToolTip(partSizeNumeric, "Size of each multipart part. Minimum 5 MiB; grown automatically to stay under 10,000 parts.");
            toolTip.SetToolTip(parallelNumeric, "How many parts of one file upload at the same time.");
            toolTip.SetToolTip(retryNumeric, "Attempts per request before a transient failure is reported.");
            toolTip.SetToolTip(testConnectionButton, "Checks the credentials against this bucket only; no account-wide permission is needed.");
            toolTip.SetToolTip(openLogsButton, "Opens the folder holding the sanitised log files.");
            toolTip.SetToolTip(clearCredentialsButton, "Removes the credentials for the selected bucket when you save.");
            toolTip.SetToolTip(_profileComboBox, "Choose which saved bucket connection to edit.");
            toolTip.SetToolTip(_addProfileButton, "Add another R2 bucket connection.");
            toolTip.SetToolTip(_removeProfileButton, "Remove this bucket profile when you save.");
        }

        private void WireEvents()
        {
            testConnectionButton.Click += OnTestConnectionClick;
            saveButton.Click += OnSaveClick;
            openLogsButton.Click += OnOpenLogsClick;
            clearCredentialsButton.Click += OnClearCredentialsClick;
            _addProfileButton.Click += OnAddProfileClick;
            _removeProfileButton.Click += OnRemoveProfileClick;
            _profileComboBox.SelectedIndexChanged += OnProfileSelectionChanged;

            accountIdTextBox.TextChanged += OnConnectionFieldChanged;
            endpointTextBox.TextChanged += OnConnectionFieldChanged;
            bucketTextBox.TextChanged += OnConnectionFieldChanged;
            accessKeyIdTextBox.TextChanged += OnConnectionFieldChanged;
            secretKeyTextBox.TextChanged += OnConnectionFieldChanged;
        }

        private void LoadValues(AppSettings settings, R2Credentials credentials)
        {
            LoadApplicationBehaviorValues(settings);
            accountIdTextBox.Text = settings.AccountId;
            bucketTextBox.Text = settings.BucketName;
            endpointTextBox.Text = settings.CustomEndpoint;
            publicUrlTextBox.Text = settings.PublicBaseUrl;

            thresholdNumeric.Value = Clamp(settings.MultipartThresholdMiB, thresholdNumeric);
            partSizeNumeric.Value = Clamp(settings.PartSizeMiB, partSizeNumeric);
            parallelNumeric.Value = Clamp(settings.ParallelParts, parallelNumeric);
            retryNumeric.Value = Clamp(settings.RetryAttempts, retryNumeric);

            rememberCheckBox.Checked = settings.RememberCredentials;

            accessKeyIdTextBox.Text = credentials.AccessKeyId;
            secretKeyTextBox.Text = credentials.SecretAccessKey;

            UpdateClearCredentialsState();
        }

        private static decimal Clamp(int value, NumericUpDown numeric)
        {
            decimal candidate = value;
            if (candidate < numeric.Minimum) return numeric.Minimum;
            if (candidate > numeric.Maximum) return numeric.Maximum;
            return candidate;
        }

        private void PopulateProfileSelector()
        {
            _suppressProfileSelection = true;
            try
            {
                _profileComboBox.Items.Clear();
                int selectedIndex = -1;
                for (int i = 0; i < ResultSettings.BucketProfiles.Count; i++)
                {
                    R2BucketProfile profile = ResultSettings.BucketProfiles[i];
                    _profileComboBox.Items.Add(new ProfileChoice(profile));
                    if (string.Equals(profile.Id, _currentProfileId, StringComparison.OrdinalIgnoreCase))
                        selectedIndex = i;
                }

                _profileComboBox.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            }
            finally
            {
                _suppressProfileSelection = false;
            }
        }

        private void OnProfileSelectionChanged(object sender, EventArgs e)
        {
            if (_suppressProfileSelection) return;
            ProfileChoice choice = _profileComboBox.SelectedItem as ProfileChoice;
            if (choice == null ||
                string.Equals(choice.Profile.Id, _currentProfileId, StringComparison.OrdinalIgnoreCase))
                return;

            SaveCurrentProfileFromEditor();
            _currentProfileId = choice.Profile.Id;
            ResultSettings.SelectBucketProfile(_currentProfileId);
            LoadProfileValues(choice.Profile, CredentialsForProfile(_currentProfileId));
            InvalidateConnectionTest("This bucket has not been tested in this dialog.");
        }

        private void OnAddProfileClick(object sender, EventArgs e)
        {
            SaveCurrentProfileFromEditor();

            R2BucketProfile profile = new R2BucketProfile();
            ResultSettings.BucketProfiles.Add(profile);
            _currentProfileId = profile.Id;
            ResultSettings.SelectBucketProfile(profile.Id);
            PopulateProfileSelector();
            LoadProfileValues(profile, R2Credentials.Empty);
            InvalidateConnectionTest("Enter this bucket's connection details, then test the connection.");
            bucketTextBox.Focus();
        }

        private void OnRemoveProfileClick(object sender, EventArgs e)
        {
            R2BucketProfile profile = ResultSettings.GetBucketProfile(_currentProfileId);
            if (profile == null) return;

            ResultSettings.BucketProfiles.Remove(profile);
            _workingCredentials.Remove(profile.Id);

            if (ResultSettings.BucketProfiles.Count == 0)
                ResultSettings.BucketProfiles.Add(new R2BucketProfile());

            _currentProfileId = ResultSettings.BucketProfiles[0].Id;
            ResultSettings.ActiveBucketProfileId = _currentProfileId;
            ResultSettings.SelectBucketProfile(_currentProfileId);
            PopulateProfileSelector();
            LoadProfileValues(ResultSettings.GetActiveBucketProfile(), CredentialsForProfile(_currentProfileId));
            InvalidateConnectionTest("Bucket removed. The change is applied when you save.");
        }

        private void SaveCurrentProfileFromEditor()
        {
            R2BucketProfile profile = ResultSettings.GetBucketProfile(_currentProfileId);
            if (profile == null) return;

            profile.AccountId = accountIdTextBox.Text.Trim();
            profile.BucketName = bucketTextBox.Text.Trim();
            profile.CustomEndpoint = endpointTextBox.Text.Trim();
            profile.PublicBaseUrl = publicUrlTextBox.Text.Trim();
            profile.Clamp();

            R2Credentials credentials = BuildCredentials();
            if (string.IsNullOrEmpty(credentials.AccessKeyId) && string.IsNullOrEmpty(credentials.SecretAccessKey))
                _workingCredentials.Remove(profile.Id);
            else
                _workingCredentials[profile.Id] = credentials;
        }

        private void LoadProfileValues(R2BucketProfile profile, R2Credentials credentials)
        {
            if (profile == null) return;

            accountIdTextBox.Text = profile.AccountId;
            bucketTextBox.Text = profile.BucketName;
            endpointTextBox.Text = profile.CustomEndpoint;
            publicUrlTextBox.Text = profile.PublicBaseUrl;
            accessKeyIdTextBox.Text = credentials != null ? credentials.AccessKeyId : string.Empty;
            secretKeyTextBox.Text = credentials != null ? credentials.SecretAccessKey : string.Empty;
            secretKeyTextBox.HideSecret();
            UpdateClearCredentialsState();
            UpdateResolvedEndpoint();
        }

        private void InvalidateConnectionTest(string detail)
        {
            _connectionVerified = false;
            LastTechnicalDetails = null;
            SetStatus(ConnectionIndicatorState.Unknown, "Not tested yet", detail);
        }

        private void UpdateClearCredentialsState()
        {
            if (clearCredentialsButton == null) return;
            R2Credentials credentials = BuildCredentials();
            clearCredentialsButton.Enabled =
                !string.IsNullOrEmpty(credentials.AccessKeyId) ||
                !string.IsNullOrEmpty(credentials.SecretAccessKey) ||
                _workingCredentials != null && _workingCredentials.ContainsKey(_currentProfileId);
        }

        private R2Credentials CredentialsForProfile(string profileId)
        {
            R2Credentials credentials;
            return !string.IsNullOrWhiteSpace(profileId) &&
                   _workingCredentials != null &&
                   _workingCredentials.TryGetValue(profileId, out credentials) &&
                   credentials != null
                ? credentials
                : R2Credentials.Empty;
        }

        private sealed class ProfileChoice
        {
            public ProfileChoice(R2BucketProfile profile) { Profile = profile; }
            public R2BucketProfile Profile { get; private set; }
            public override string ToString() { return Profile.DisplayName; }
        }

        // ------------------------------------------------------------------------- editing

        private void OnConnectionFieldChanged(object sender, EventArgs e)
        {
            // Any change invalidates the previous successful test.
            if (_connectionVerified)
            {
                _connectionVerified = false;
                SetStatus(ConnectionIndicatorState.Unknown, "Not tested yet",
                    "The connection details changed. Test the connection again before uploading.");
            }

            UpdateResolvedEndpoint();
            UpdateClearCredentialsState();
        }

        private void UpdateResolvedEndpoint()
        {
            AppSettings preview = new AppSettings
            {
                AccountId = accountIdTextBox.Text,
                CustomEndpoint = endpointTextBox.Text
            };

            string url = preview.ResolveServiceUrl();
            resolvedEndpointLabel.Text = string.IsNullOrEmpty(url)
                ? "Endpoint: enter an Account ID to derive https://{account-id}.r2.cloudflarestorage.com"
                : "Endpoint: " + url;
        }

        private AppSettings BuildSettings()
        {
            SaveCurrentProfileFromEditor();
            AppSettings settings = ResultSettings.Clone();

            settings.ActiveBucketProfileId = _currentProfileId;
            settings.SelectBucketProfile(_currentProfileId);
            settings.MultipartThresholdMiB = (int)thresholdNumeric.Value;
            settings.PartSizeMiB = (int)partSizeNumeric.Value;
            settings.ParallelParts = (int)parallelNumeric.Value;
            settings.RetryAttempts = (int)retryNumeric.Value;
            settings.RememberCredentials = rememberCheckBox.Checked;
            ApplyApplicationBehaviorValues(settings);
            settings.Clamp();

            return settings;
        }

        private R2Credentials BuildCredentials()
        {
            return new R2Credentials(accessKeyIdTextBox.Text.Trim(), secretKeyTextBox.Text);
        }

        // ---------------------------------------------------------------- connection testing

        private async void OnTestConnectionClick(object sender, EventArgs e)
        {
            if (_testCancellation != null)
            {
                // A second click cancels the running test.
                _testCancellation.Cancel();
                return;
            }

            AppSettings settings = BuildSettings();
            R2Credentials credentials = BuildCredentials();
            string testedProfileId = _currentProfileId;

            SetStatus(ConnectionIndicatorState.Connecting, "Testing…",
                "Checking access to \"" + (settings.BucketName.Length == 0 ? "(no bucket)" : settings.BucketName) + "\".");

            testConnectionButton.Text = "Cancel test";
            saveButton.Enabled = false;
            _profileComboBox.Enabled = false;
            _addProfileButton.Enabled = false;
            _removeProfileButton.Enabled = false;

            _testCancellation = new CancellationTokenSource();

            try
            {
                ConnectionTestResult result = await _connectionTester
                    .TestAsync(settings, credentials, _testCancellation.Token)
                    .ConfigureAwait(true);

                _connectionVerified =
                    result.IsSuccess &&
                    string.Equals(_currentProfileId, testedProfileId, StringComparison.OrdinalIgnoreCase);
                SetStatus(MapState(result.Status), result.Headline, result.Detail);

                if (!result.IsSuccess && !string.IsNullOrEmpty(result.TechnicalDetails))
                {
                    LastTechnicalDetails = result.TechnicalDetails;
                    statusDetailLabel.Text = result.Detail + "  (double-click for technical details)";
                }
                else
                {
                    LastTechnicalDetails = null;
                }
            }
            finally
            {
                _testCancellation.Dispose();
                _testCancellation = null;
                testConnectionButton.Text = "Test connection";
                saveButton.Enabled = true;
                _profileComboBox.Enabled = true;
                _addProfileButton.Enabled = true;
                _removeProfileButton.Enabled = true;
            }
        }

        private string LastTechnicalDetails { get; set; }

        private static ConnectionIndicatorState MapState(ConnectionTestStatus status)
        {
            switch (status)
            {
                case ConnectionTestStatus.Success: return ConnectionIndicatorState.Connected;
                case ConnectionTestStatus.MissingConfiguration: return ConnectionIndicatorState.Warning;
                case ConnectionTestStatus.RateLimited: return ConnectionIndicatorState.Warning;
                default: return ConnectionIndicatorState.Error;
            }
        }

        private void SetStatus(ConnectionIndicatorState state, string headline, string detail)
        {
            statusIndicator.State = state;
            statusHeadlineLabel.Text = headline ?? string.Empty;
            statusDetailLabel.Text = detail ?? string.Empty;

            statusHeadlineLabel.ForeColor = statusIndicator.GetStateColor();

            // The state is in the text as well as the colour.
            statusPanel.AccessibleName = "Connection status";
            statusPanel.AccessibleDescription = headline + ". " + detail;
        }

        // ------------------------------------------------------------------------- commands

        private void OnSaveClick(object sender, EventArgs e)
        {
            AppSettings settings = BuildSettings();
            R2Credentials credentials = BuildCredentials();

            string validation = Validate(settings, credentials);
            if (validation != null)
            {
                SetStatus(ConnectionIndicatorState.Warning, "Check the settings", validation);
                return;
            }

            string startupError;
            bool actualStartup = _startupRegistration.IsEnabled(Application.ExecutablePath);
            if (settings.StartWithWindows != actualStartup &&
                !_startupRegistration.SetEnabled(settings.StartWithWindows, Application.ExecutablePath, out startupError))
            {
                settings.StartWithWindows = actualStartup;
                _startWithWindowsCheckBox.Checked = actualStartup;
                ErrorDialog.Show(this, "Start with Windows could not be updated", startupError, null);
                return;
            }

            settings.StartWithWindows = _startupRegistration.IsEnabled(Application.ExecutablePath);
            if (!_settingsService.Save(settings))
            {
                ErrorDialog.Show(this, "The settings could not be saved",
                    "Writing to " + _settingsService.FilePath + " failed. Check that the folder is writable.",
                    null);
                return;
            }

            if (settings.RememberCredentials)
            {
                if (!_credentialService.SaveProfiles(_workingCredentials))
                {
                    // Do not leave the non-secret setting claiming that credentials should
                    // be loaded next time when DPAPI persistence actually failed. This also
                    // prevents an older saved pair from being loaded accidentally.
                    settings.RememberCredentials = false;
                    rememberCheckBox.Checked = false;
                    _settingsService.Save(settings);

                    ErrorDialog.Show(this, "The credentials could not be saved",
                        "Windows DPAPI encryption failed, so the credentials were not stored. They will still be used for this session, but “Remember credentials” has been switched off.",
                        null);
                }
            }
            else if (_credentialService.HasSavedCredentials)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "\"Remember credentials\" is switched off, and credentials are currently saved on this computer." +
                    Environment.NewLine + Environment.NewLine +
                    "Delete the saved credentials now?",
                    "Remove saved credentials",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);

                if (answer == DialogResult.Yes) _credentialService.Clear();
            }

            ResultSettings = settings;
            ResultCredentials = credentials;
            ResultCredentialsByProfile = CopyCredentials(_workingCredentials);

            DialogResult = DialogResult.OK;
            Close();
        }

        private static string Validate(AppSettings settings, R2Credentials credentials)
        {
            bool hasCustomEndpoint = !string.IsNullOrWhiteSpace(settings.CustomEndpoint);

            if (string.IsNullOrWhiteSpace(settings.AccountId) && !hasCustomEndpoint)
                return "Enter the Cloudflare Account ID, or a custom endpoint.";

            if (string.IsNullOrWhiteSpace(settings.BucketName))
                return "Enter the bucket name.";

            string serviceUrl = settings.ResolveServiceUrl();
            Uri uri;
            if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out uri))
                return "The endpoint \"" + serviceUrl + "\" is not a valid URL.";

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return "The endpoint must use HTTPS.";

            if (settings.RememberCredentials && !credentials.IsComplete)
                return "Enter both the Access Key ID and the Secret Access Key, or switch off \"Remember credentials\".";

            return null;
        }

        private void OnOpenLogsClick(object sender, EventArgs e)
        {
            try
            {
                AppPaths.EnsureDirectory(AppPaths.LogDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppPaths.LogDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Error("Settings.OpenLogs", "The log folder could not be opened.", ex);
                ErrorDialog.Show(this, "The log folder could not be opened",
                    "Open it manually: " + AppPaths.LogDirectory, null);
            }
        }

        private void OnClearCredentialsClick(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show(
                this,
                "Remove the credentials for \"" +
                (string.IsNullOrWhiteSpace(bucketTextBox.Text) ? "this bucket" : bucketTextBox.Text.Trim()) +
                "\"?" + Environment.NewLine + Environment.NewLine +
                "The encrypted vault is updated when you click Save.",
                "Remove bucket credentials",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes) return;

            _workingCredentials.Remove(_currentProfileId);
            accessKeyIdTextBox.Text = string.Empty;
            secretKeyTextBox.Text = string.Empty;
            secretKeyTextBox.HideSecret();
            clearCredentialsButton.Enabled = false;
            InvalidateConnectionTest("Credentials removed from this bucket. Click Save to apply the change.");
        }

        // --------------------------------------------------------------------------- window

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            DarkTitleBar.Apply(this);
            DarkScrollBars.Apply(scrollPanel);
            foreach (NumericUpDown numeric in new[] { thresholdNumeric, partSizeNumeric, parallelNumeric, retryNumeric })
                DarkScrollBars.Apply(numeric);

            statusDetailLabel.DoubleClick += OnStatusDoubleClick;

            if (string.IsNullOrEmpty(accountIdTextBox.Text)) accountIdTextBox.Focus();
            else if (string.IsNullOrEmpty(secretKeyTextBox.Text)) secretKeyTextBox.Focus();
            else testConnectionButton.Focus();
        }

        private static Dictionary<string, R2Credentials> CreateLegacyCredentialMap(
            AppSettings settings,
            R2Credentials credentials)
        {
            Dictionary<string, R2Credentials> result =
                new Dictionary<string, R2Credentials>(StringComparer.OrdinalIgnoreCase);
            if (credentials == null ||
                string.IsNullOrEmpty(credentials.AccessKeyId) && string.IsNullOrEmpty(credentials.SecretAccessKey))
                return result;

            AppSettings copy = settings != null ? settings.Clone() : new AppSettings();
            copy.Clamp();
            result[copy.ActiveBucketProfileId] = credentials;
            return result;
        }

        private static Dictionary<string, R2Credentials> CopyCredentials(
            IDictionary<string, R2Credentials> source)
        {
            Dictionary<string, R2Credentials> result =
                new Dictionary<string, R2Credentials>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return result;

            foreach (KeyValuePair<string, R2Credentials> entry in source)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && entry.Value != null)
                    result[entry.Key] = new R2Credentials(
                        entry.Value.AccessKeyId,
                        entry.Value.SecretAccessKey);
            }
            return result;
        }

        private void OnStatusDoubleClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(LastTechnicalDetails)) return;
            ErrorDialog.Show(this, statusHeadlineLabel.Text, statusDetailLabel.Text, LastTechnicalDetails);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Never leave a secret revealed behind a closed dialog.
            secretKeyTextBox.HideSecret();

            if (_testCancellation != null)
            {
                try { _testCancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            base.OnFormClosing(e);
        }
    }
}
