using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using CloudflareR2Uploader.Wpf.Services;
using CloudflareR2Uploader.Wpf.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudflareR2Uploader.Wpf.ViewModels
{
    public sealed record SettingsSection(string Key, string DisplayName, string IconKey)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record ExpiryChoice(string DisplayName, int Hours)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record RetryChoice(string DisplayName, int Attempts)
    {
        public override string ToString() => DisplayName;
    }

    public sealed record ParallelChoice(string DisplayName, int Count)
    {
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// The modal Settings window, implementing Images/06-settings-connection.png and
    /// Images/07-settings-transfers-application.png.
    /// <para>
    /// Every edit happens on a draft clone. Nothing is written to <c>settings.json</c> or to
    /// the DPAPI vault until Save succeeds, and Save stays disabled until something actually
    /// changed.
    /// </para>
    /// </summary>
    public sealed partial class SettingsViewModel : ObservableObject, IDialogCloseRequester, IDisposable
    {
        private readonly IAppSession _session;
        private readonly IDialogService _dialogs;
        private readonly IClipboardService _clipboard;
        private readonly IStartupRegistrationService _startup;
        private readonly R2ConnectionTester _tester;
        private readonly UpdateService _updates;
        private readonly ILoggingService _log;
        private readonly IProcessLauncher _processLauncher;
        private readonly IApplicationExitCoordinator _exitCoordinator;
        private readonly IApplicationController _applicationController;

        private readonly AppSettings _original;
        private readonly Dictionary<string, R2Credentials> _originalCredentials = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, R2Credentials> _draftCredentials = new(StringComparer.OrdinalIgnoreCase);

        private AppSettings _draft;
        private CancellationTokenSource? _testCancellation;
        private bool _loading;
        private bool _disposed;
        private UpdateManifest? _availableUpdate;
        private UpdateSourceConfiguration? _availableUpdateSource;

        public SettingsViewModel(
            IAppSession session,
            IDialogService dialogs,
            IClipboardService clipboard,
            IStartupRegistrationService startup,
            R2ConnectionTester tester,
            UpdateService updates,
            ILoggingService log,
            IProcessLauncher processLauncher,
            IApplicationExitCoordinator exitCoordinator,
            IApplicationController applicationController)
        {
            _session = session;
            _dialogs = dialogs;
            _clipboard = clipboard;
            _startup = startup;
            _tester = tester;
            _updates = updates;
            _log = log;
            _processLauncher = processLauncher;
            _exitCoordinator = exitCoordinator;
            _applicationController = applicationController;

            _original = _session.Settings;
            _draft = _session.Settings;

            foreach (R2BucketProfile profile in _session.Profiles)
            {
                R2Credentials credentials = _session.GetCredentials(profile.Id);
                _originalCredentials[profile.Id] = credentials;
                _draftCredentials[profile.Id] = credentials;
            }

            Profiles = new ObservableCollection<R2BucketProfile>(_session.Profiles);
            _selectedProfile = Profiles.FirstOrDefault(
                                   profile => string.Equals(profile.Id, _draft.ActiveBucketProfileId, StringComparison.OrdinalIgnoreCase))
                               ?? Profiles.FirstOrDefault();

            _selectedSection = Sections[1];

            LoadFromDraft();
        }

        public IReadOnlyList<SettingsSection> Sections { get; } = new[]
        {
            new SettingsSection("buckets", "Buckets", "Icon.Bucket"),
            new SettingsSection("connection", "Connection", "Icon.Link"),
            new SettingsSection("transfers", "Transfers", "Icon.Upload"),
            new SettingsSection("application", "Application", "Icon.Inspector"),
            new SettingsSection("notifications", "Notifications", "Icon.Info"),
            new SettingsSection("updates", "Updates", "Icon.Refresh"),
            new SettingsSection("about", "About", "Icon.Info")
        };

        public ObservableCollection<R2BucketProfile> Profiles { get; }

        public IReadOnlyList<ExpiryChoice> ExpiryChoices { get; } = new[]
        {
            new ExpiryChoice("1 hour", 1),
            new ExpiryChoice("6 hours", 6),
            new ExpiryChoice("24 hours", 24),
            new ExpiryChoice("3 days", 72),
            new ExpiryChoice("7 days", 168)
        };

        public IReadOnlyList<ParallelChoice> ParallelChoices { get; } =
            Enumerable.Range(AppSettings.MinParallelParts, AppSettings.MaxParallelParts - AppSettings.MinParallelParts + 1)
                .Select(value => new ParallelChoice(value.ToString(CultureInfo.CurrentCulture), value))
                .ToList();

        public IReadOnlyList<RetryChoice> RetryChoices { get; } =
            Enumerable.Range(AppSettings.MinRetryAttempts, AppSettings.MaxRetryAttempts - AppSettings.MinRetryAttempts + 1)
                .Select(value => new RetryChoice(
                    value == 1 ? "1 attempt" : value.ToString(CultureInfo.CurrentCulture) + " attempts", value))
                .ToList();

        public IReadOnlyList<AppTheme> Themes { get; } = new[] { AppTheme.Dark, AppTheme.Light, AppTheme.System };

        [ObservableProperty]
        private SettingsSection _selectedSection;

        [ObservableProperty]
        private R2BucketProfile? _selectedProfile;

        // Connection
        [ObservableProperty]
        private string _accountId = string.Empty;

        [ObservableProperty]
        private string _bucketName = string.Empty;

        [ObservableProperty]
        private string _accessKeyId = string.Empty;

        [ObservableProperty]
        private string _secretAccessKey = string.Empty;

        [ObservableProperty]
        private bool _isSecretRevealed;

        [ObservableProperty]
        private string _endpoint = string.Empty;

        [ObservableProperty]
        private string? _endpointError;

        [ObservableProperty]
        private string _publicBaseUrl = string.Empty;

        [ObservableProperty]
        private string? _publicBaseUrlError;

        [ObservableProperty]
        private ExpiryChoice _selectedExpiry = new("24 hours", 24);

        [ObservableProperty]
        private bool _preferPublicUrls;

        [ObservableProperty]
        private bool _rememberCredentials;

        [ObservableProperty]
        private bool _forgetCredentialsOnExit;

        // Transfers
        [ObservableProperty]
        private ParallelChoice _selectedParallel = new("4", 4);

        [ObservableProperty]
        private string _multipartThresholdText = "100";

        [ObservableProperty]
        private string? _multipartThresholdError;

        [ObservableProperty]
        private string _partSizeText = "64";

        [ObservableProperty]
        private string? _partSizeError;

        [ObservableProperty]
        private RetryChoice _selectedRetry = new("5 attempts", 5);

        [ObservableProperty]
        private bool _verifyETagAfterUpload = true;

        // Application
        [ObservableProperty]
        private bool _startWithWindows;

        [ObservableProperty]
        private bool _startMinimized;

        public bool StartWithWindowsAndMinimized
        {
            get => StartWithWindows && StartMinimized;
            set
            {
                if (StartWithWindows == value && StartMinimized == value) return;
                StartWithWindows = value;
                StartMinimized = value;
                OnPropertyChanged();
            }
        }

        [ObservableProperty]
        private bool _closeToTray = true;

        [ObservableProperty]
        private bool _minimizeToTray = true;

        [ObservableProperty]
        private bool _continueTransfersWhileMinimized = true;

        [ObservableProperty]
        private bool _confirmDeletes = true;

        [ObservableProperty]
        private AppTheme _theme = AppTheme.Dark;

        // Notifications
        [ObservableProperty]
        private bool _showTrayNotifications = true;

        [ObservableProperty]
        private bool _notifyOnTransferComplete = true;

        [ObservableProperty]
        private bool _notifyOnTransferFailure = true;

        [ObservableProperty]
        private bool _notifyOnConnectionError = true;

        [ObservableProperty]
        private bool _notifyOnUpdateAvailable = true;

        // Updates
        [ObservableProperty]
        private bool _checkForUpdatesAutomatically = true;

        [ObservableProperty]
        private string _updateStatus = "No update check has run yet.";

        [ObservableProperty]
        private string _availableVersion = string.Empty;

        [ObservableProperty]
        private string _releaseNotes = string.Empty;

        [ObservableProperty]
        private bool _isCheckingForUpdates;

        [ObservableProperty]
        private bool _isDownloadingUpdate;

        [ObservableProperty]
        private string _activityRetentionText = "30";

        [ObservableProperty]
        private string? _activityRetentionError;

        // Footer
        [ObservableProperty]
        private ConnectionState _testState = ConnectionState.Unknown;

        /// <summary>Wording for the footer chip. Never shows the raw enum name.</summary>
        public string TestStateText => TestState switch
        {
            ConnectionState.Testing => "Testing…",
            ConnectionState.Connected => "Connected",
            ConnectionState.Failed => "Failed",
            ConnectionState.NotConfigured => "Not configured",
            _ => string.Empty
        };

        [ObservableProperty]
        private string _testSummary = string.Empty;

        [ObservableProperty]
        private bool _isDirty;

        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF bindings require instance properties.")]
        public string CurrentVersion => ApplicationInfo.DisplayVersion;

        public string LogDirectory => _log?.LogDirectory ?? AppPaths.LogDirectory;

        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF bindings require instance properties.")]
        public string DataDirectory => AppPaths.Root;

        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF bindings require instance properties.")]
        public string RepositoryUrl => "https://github.com/Saboreq/CloudflareR2Uploader";

        /// <summary>
        /// The wording deliberately does not say "Windows Credential Manager", which is what
        /// the mock shows. The implementation is DPAPI under the current Windows user, and
        /// the UI must describe what actually happens.
        /// </summary>
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "WPF bindings require instance properties.")]
        public string CredentialStorageDescription =>
            "Protect saved credentials with Windows DPAPI (current user)";

        public bool HasValidationErrors =>
            EndpointError is not null ||
            PublicBaseUrlError is not null ||
            MultipartThresholdError is not null ||
            PartSizeError is not null ||
            ActivityRetentionError is not null;

        public bool CanSave => IsDirty && !HasValidationErrors;

        public event EventHandler<bool>? CloseRequested;

        public void SelectSection(string key)
        {
            SettingsSection? section = Sections.FirstOrDefault(
                candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));

            if (section is not null) SelectedSection = section;
        }

        // ----------------------------------------------------------------- draft binding

        private void LoadFromDraft()
        {
            _loading = true;
            try
            {
                R2BucketProfile? profile = SelectedProfile;

                AccountId = profile?.AccountId ?? string.Empty;
                BucketName = profile?.BucketName ?? string.Empty;
                Endpoint = string.IsNullOrWhiteSpace(profile?.CustomEndpoint)
                    ? BuildDerivedEndpoint(profile?.AccountId)
                    : profile!.CustomEndpoint;
                PublicBaseUrl = profile?.PublicBaseUrl ?? string.Empty;

                R2Credentials credentials = profile is null
                    ? R2Credentials.Empty
                    : _draftCredentials.TryGetValue(profile.Id, out R2Credentials? found) ? found : R2Credentials.Empty;

                AccessKeyId = credentials.AccessKeyId;
                SecretAccessKey = credentials.SecretAccessKey;

                SelectedExpiry = ExpiryChoices.FirstOrDefault(choice => choice.Hours == _draft.TemporaryLinkExpiryHours)
                                 ?? ExpiryChoices[2];
                PreferPublicUrls = _draft.PreferPublicUrls;
                RememberCredentials = _draft.RememberCredentials;
                ForgetCredentialsOnExit = _draft.ForgetCredentialsOnExit;

                SelectedParallel = ParallelChoices.First(choice => choice.Count == _draft.ParallelParts);
                MultipartThresholdText = _draft.MultipartThresholdMiB.ToString(CultureInfo.CurrentCulture);
                PartSizeText = _draft.PartSizeMiB.ToString(CultureInfo.CurrentCulture);
                SelectedRetry = RetryChoices.First(choice => choice.Attempts == _draft.RetryAttempts);
                VerifyETagAfterUpload = _draft.VerifyETagAfterUpload;

                StartWithWindows = _draft.StartWithWindows;
                StartMinimized = _draft.StartMinimized;
                CloseToTray = _draft.CloseToTray;
                MinimizeToTray = _draft.MinimizeToTray;
                ContinueTransfersWhileMinimized = _draft.ContinueTransfersWhileMinimized;
                ConfirmDeletes = _draft.ConfirmDeletes;
                Theme = _draft.Theme;

                ShowTrayNotifications = _draft.ShowTrayNotifications;
                NotifyOnTransferComplete = _draft.NotifyOnTransferComplete;
                NotifyOnTransferFailure = _draft.NotifyOnTransferFailure;
                NotifyOnConnectionError = _draft.NotifyOnConnectionError;
                NotifyOnUpdateAvailable = _draft.NotifyOnUpdateAvailable;

                CheckForUpdatesAutomatically = _draft.CheckForUpdatesAutomatically;
                ActivityRetentionText = _draft.ActivityRetentionDays.ToString(CultureInfo.CurrentCulture);
            }
            finally
            {
                _loading = false;
            }

            Validate();
            UpdateDirty();
        }

        private static string BuildDerivedEndpoint(string? accountId)
        {
            string account = (accountId ?? string.Empty).Trim();
            return account.Length == 0 ? string.Empty : "https://" + account + ".r2.cloudflarestorage.com";
        }

        /// <summary>Writes the editable fields back into the draft settings and credential map.</summary>
        private void PushToDraft()
        {
            if (_loading) return;

            R2BucketProfile? profile = SelectedProfile;
            if (profile is not null)
            {
                profile.AccountId = AccountId.Trim();
                profile.BucketName = BucketName.Trim();

                // An endpoint that matches the derived one is stored as "no override" to keep
                // settings.json tidy and preserve the existing on-disk contract.
                string derived = BuildDerivedEndpoint(AccountId);
                profile.CustomEndpoint = string.Equals(Endpoint.Trim(), derived, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : Endpoint.Trim();

                profile.PublicBaseUrl = PublicBaseUrl.Trim();

                _draftCredentials[profile.Id] = new R2Credentials(AccessKeyId.Trim(), SecretAccessKey);

                R2BucketProfile? stored = _draft.BucketProfiles.FirstOrDefault(
                    candidate => string.Equals(candidate.Id, profile.Id, StringComparison.OrdinalIgnoreCase));

                if (stored is not null)
                {
                    stored.AccountId = profile.AccountId;
                    stored.BucketName = profile.BucketName;
                    stored.CustomEndpoint = profile.CustomEndpoint;
                    stored.PublicBaseUrl = profile.PublicBaseUrl;
                }

                _draft.ActiveBucketProfileId = profile.Id;
            }

            _draft.TemporaryLinkExpiryHours = SelectedExpiry.Hours;
            _draft.PreferPublicUrls = PreferPublicUrls;
            _draft.RememberCredentials = RememberCredentials;
            _draft.ForgetCredentialsOnExit = ForgetCredentialsOnExit;

            _draft.ParallelParts = SelectedParallel.Count;
            if (int.TryParse(MultipartThresholdText, NumberStyles.Integer, CultureInfo.CurrentCulture, out int threshold))
                _draft.MultipartThresholdMiB = threshold;
            if (int.TryParse(PartSizeText, NumberStyles.Integer, CultureInfo.CurrentCulture, out int partSize))
                _draft.PartSizeMiB = partSize;
            _draft.RetryAttempts = SelectedRetry.Attempts;
            _draft.VerifyETagAfterUpload = VerifyETagAfterUpload;

            _draft.StartWithWindows = StartWithWindows;
            _draft.StartMinimized = StartMinimized;
            _draft.CloseToTray = CloseToTray;
            _draft.MinimizeToTray = MinimizeToTray;
            _draft.ContinueTransfersWhileMinimized = ContinueTransfersWhileMinimized;
            _draft.ConfirmDeletes = ConfirmDeletes;
            _draft.Theme = Theme;

            _draft.ShowTrayNotifications = ShowTrayNotifications;
            _draft.NotifyOnTransferComplete = NotifyOnTransferComplete;
            _draft.NotifyOnTransferFailure = NotifyOnTransferFailure;
            _draft.NotifyOnConnectionError = NotifyOnConnectionError;
            _draft.NotifyOnUpdateAvailable = NotifyOnUpdateAvailable;

            _draft.CheckForUpdatesAutomatically = CheckForUpdatesAutomatically;
            if (int.TryParse(ActivityRetentionText, NumberStyles.Integer, CultureInfo.CurrentCulture, out int retention))
                _draft.ActivityRetentionDays = retention;

            _draft.NormalizeBucketProfiles();
            Validate();
            UpdateDirty();
        }

        // -------------------------------------------------------------------- validation

        internal void Validate()
        {
            EndpointError = ValidateEndpoint(Endpoint, AccountId);
            PublicBaseUrlError = ValidatePublicBaseUrl(PublicBaseUrl);
            MultipartThresholdError = ValidateRange(
                MultipartThresholdText, AppSettings.MinThresholdMiB, AppSettings.MaxThresholdMiB, "threshold");
            PartSizeError = ValidatePartSize(PartSizeText);
            ActivityRetentionError = ValidateRange(
                ActivityRetentionText, AppSettings.MinActivityRetentionDays, AppSettings.MaxActivityRetentionDays, "retention");

            OnPropertyChanged(nameof(HasValidationErrors));
            OnPropertyChanged(nameof(CanSave));
            SaveCommand.NotifyCanExecuteChanged();
        }

        internal static string? ValidateEndpoint(string endpoint, string accountId)
        {
            string value = (endpoint ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return string.IsNullOrWhiteSpace(accountId)
                    ? "Enter an Account ID, or a custom endpoint."
                    : null;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
                return "The endpoint is not a valid URL.";

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return "The endpoint must use HTTPS. Uploads send an unsigned payload, which is only safe over TLS.";

            if (!string.IsNullOrEmpty(uri.UserInfo))
                return "The endpoint must not contain embedded credentials.";

            // The mock's inline error. R2's account endpoint always ends in .com; a truncated
            // host is the most common paste mistake.
            if (uri.Host.EndsWith(".r2.cloudflarestorage", StringComparison.OrdinalIgnoreCase))
                return "Endpoint must end in .com — expected https://<account>.r2.cloudflarestorage.com";

            return null;
        }

        internal static string? ValidatePublicBaseUrl(string publicBaseUrl)
        {
            string value = (publicBaseUrl ?? string.Empty).Trim();
            if (value.Length == 0) return null;

            string candidate = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri)) return "That is not a valid URL.";

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return "The public base URL must use HTTPS.";

            return null;
        }

        internal static string? ValidateRange(string text, int minimum, int maximum, string what)
        {
            if (!int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int value))
                return "Enter a whole number.";

            if (value < minimum || value > maximum)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "The {0} must be between {1} and {2}.", what, minimum, maximum);
            }

            return null;
        }

        /// <summary>
        /// S3 and R2 reject any part except the last below 5 MiB and above 5 GiB, so the
        /// field is validated against the protocol, not against a preference.
        /// </summary>
        internal static string? ValidatePartSize(string text)
        {
            string? rangeError = ValidateRange(text, AppSettings.MinPartSizeMiB, AppSettings.MaxPartSizeMiB, "part size");
            if (rangeError is not null) return rangeError;
            return null;
        }

        private void UpdateDirty()
        {
            bool settingsChanged = !SettingsEqual(_original, _draft);
            bool credentialsChanged = _draftCredentials.Any(entry =>
            {
                R2Credentials before = _originalCredentials.TryGetValue(entry.Key, out R2Credentials? found)
                    ? found
                    : R2Credentials.Empty;

                return !string.Equals(before.AccessKeyId, entry.Value.AccessKeyId, StringComparison.Ordinal) ||
                       !string.Equals(before.SecretAccessKey, entry.Value.SecretAccessKey, StringComparison.Ordinal);
            });

            IsDirty = settingsChanged || credentialsChanged;
            OnPropertyChanged(nameof(CanSave));
            SaveCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Compares the persisted shape of two settings objects. Serialising both and
        /// comparing the text is the only way to be sure a newly added member is included.
        /// </summary>
        internal static bool SettingsEqual(AppSettings left, AppSettings right)
        {
            return string.Equals(Serialize(left), Serialize(right), StringComparison.Ordinal);
        }

        private static string Serialize(AppSettings settings)
        {
            AppSettings clone = settings.Clone();
            clone.Clamp();

            using System.IO.MemoryStream stream = new();
            new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AppSettings))
                .WriteObject(stream, clone);

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        // ----------------------------------------------------------------------- commands

        [RelayCommand]
        private void ToggleSecretVisibility() => IsSecretRevealed = !IsSecretRevealed;

        [RelayCommand]
        private void CopyEndpoint()
        {
            if (string.IsNullOrWhiteSpace(Endpoint)) return;
            _clipboard.SetText(Endpoint.Trim());
        }

        [RelayCommand]
        private void CopyPublicBaseUrl()
        {
            if (string.IsNullOrWhiteSpace(PublicBaseUrl)) return;
            _clipboard.SetText(PublicBaseUrl.Trim());
        }

        [RelayCommand]
        private void AddProfile()
        {
            R2BucketProfile profile = new();
            profile.Clamp();

            _draft.BucketProfiles.Add(profile);
            _draftCredentials[profile.Id] = R2Credentials.Empty;

            Profiles.Add(profile);
            SelectedProfile = profile;
            SelectSection("connection");
            UpdateDirty();
        }

        private bool CanRemoveProfile() => Profiles.Count > 1 && SelectedProfile is not null;

        [RelayCommand(CanExecute = nameof(CanRemoveProfile))]
        private async Task RemoveProfileAsync()
        {
            R2BucketProfile? profile = SelectedProfile;
            if (profile is null) return;

            bool confirmed = await _dialogs.ConfirmAsync(
                "Remove this bucket profile?",
                "\"" + profile.DisplayName + "\" and its saved credentials are removed from this computer when you save. " +
                "Nothing in the bucket itself changes.",
                "Remove profile",
                isDestructive: true).ConfigureAwait(true);

            if (!confirmed) return;

            _draft.BucketProfiles.RemoveAll(
                candidate => string.Equals(candidate.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            _draftCredentials.Remove(profile.Id);
            Profiles.Remove(profile);

            _draft.NormalizeBucketProfiles();
            SelectedProfile = Profiles.FirstOrDefault();
            UpdateDirty();
        }

        [RelayCommand]
        private async Task TestConnectionAsync()
        {
            PushToDraft();

            if (HasValidationErrors)
            {
                TestState = ConnectionState.Failed;
                TestSummary = "Fix the highlighted fields first.";
                return;
            }

            CancellationTokenSource? previous = Interlocked.Exchange(ref _testCancellation, new CancellationTokenSource());
            if (previous is not null)
            {
                try { previous.Cancel(); } catch (ObjectDisposedException) { }
                previous.Dispose();
            }

            CancellationTokenSource? current = _testCancellation;
            if (current is null) return;

            TestState = ConnectionState.Testing;
            TestSummary = "Testing…";

            AppSettings probe = _draft.Clone();
            probe.Clamp();

            R2Credentials credentials = new(AccessKeyId.Trim(), SecretAccessKey);

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                ConnectionTestResult result = await _tester
                    .TestAsync(probe, credentials, current.Token)
                    .ConfigureAwait(true);

                stopwatch.Stop();

                TestState = result.IsSuccess ? ConnectionState.Connected : ConnectionState.Failed;
                TestSummary = result.IsSuccess
                    ? "Tested just now · " + stopwatch.ElapsedMilliseconds.ToString(CultureInfo.CurrentCulture) + " ms"
                    : result.Headline;

                if (!result.IsSuccess)
                {
                    await _dialogs.ShowErrorAsync("Connection test failed", result.Detail, result.TechnicalDetails)
                        .ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer test.
            }
            catch (Exception ex)
            {
                TestState = ConnectionState.Failed;
                TestSummary = "The test could not be completed.";
                _log?.Warning("Settings.Test", "The connection test failed (" + ex.GetType().Name + ").");
            }
        }

        private bool CanCheckForUpdates() => !IsDownloadingUpdate;

        [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
        private async Task CheckForUpdatesAsync()
        {
            _availableUpdate = null;
            _availableUpdateSource = null;
            InstallUpdateCommand.NotifyCanExecuteChanged();

            UpdateSourceConfiguration? updateSource = UpdateService.LoadUpdateSource(AppContext.BaseDirectory);
            if (updateSource is null)
            {
                UpdateStatus = "This build has no update channel configured.";
                return;
            }

            IsCheckingForUpdates = true;
            try
            {
                UpdateCheckResult result = await _updates
                    .CheckAsync(updateSource, ApplicationInfo.DisplayVersion, CancellationToken.None)
                    .ConfigureAwait(true);

                if (result.IsUpdateAvailable)
                {
                    _availableUpdate = result.Manifest;
                    _availableUpdateSource = updateSource;
                    AvailableVersion = result.Manifest.Version;
                    ReleaseNotes = result.Manifest.Notes ?? string.Empty;
                    UpdateStatus = "Version " + result.Manifest.Version + " is available (" +
                                   FileSizeFormatter.Format(result.Manifest.SizeBytes) +
                                   ", verified by size and SHA-256).";
                }
                else
                {
                    AvailableVersion = string.Empty;
                    ReleaseNotes = string.Empty;
                    UpdateStatus = "Cloudflare R2 Uploader is up to date.";
                }

                InstallUpdateCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                UpdateStatus = "The update check could not be completed.";
                _log?.Warning("Settings.Update", "The update check failed (" + ex.GetType().Name + ").");
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
        }

        private bool CanInstallUpdate() =>
            _availableUpdate is not null && _availableUpdateSource is not null && !IsDownloadingUpdate;

        partial void OnIsDownloadingUpdateChanged(bool value)
        {
            InstallUpdateCommand.NotifyCanExecuteChanged();
            CheckForUpdatesCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
        private async Task InstallUpdateAsync()
        {
            UpdateManifest? manifest = _availableUpdate;
            UpdateSourceConfiguration? updateSource = _availableUpdateSource;
            if (manifest is null || updateSource is null || IsDownloadingUpdate) return;

            IsDownloadingUpdate = true;
            try
            {
                Progress<UpdateDownloadProgress> progress = new(download =>
                {
                    UpdateStatus = FormatUpdateProgress(download);
                });

                string installer = await _updates
                    .DownloadInstallerAsync(updateSource, manifest, progress, CancellationToken.None)
                    .ConfigureAwait(true);

                UpdateStatus = "Download complete and SHA-256 verified.";
                bool confirmed = await _dialogs.ConfirmAsync(
                    "Install update now?",
                    "Cloudflare R2 Uploader " + manifest.Version +
                    " is ready. The app will close and the per-user installer will reopen it when finished.",
                    "Install now").ConfigureAwait(true);
                if (!confirmed) return;

                await StartVerifiedInstallerAsync(installer, updateSource, manifest).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                UpdateStatus = "The update download was cancelled.";
            }
            catch (Exception ex)
            {
                UpdateStatus = "The update download could not be completed.";
                _log.Warning("Settings.Update", "The verified update download failed (" + ex.GetType().Name + ").");
                await _dialogs.ShowErrorAsync(
                    "Update download failed",
                    "The installer was not opened because its download or verification failed.")
                    .ConfigureAwait(true);
            }
            finally
            {
                IsDownloadingUpdate = false;
            }
        }

        internal async Task<bool> StartVerifiedInstallerAsync(
            string installer,
            UpdateSourceConfiguration updateSource,
            UpdateManifest manifest)
        {
            if (!await _exitCoordinator.PrepareAsync().ConfigureAwait(true))
            {
                UpdateStatus = "Update downloaded. Installation was postponed.";
                return false;
            }

            try
            {
                using VerifiedInstallerLaunch verified = UpdateService.OpenVerifiedInstallerForLaunch(
                    updateSource,
                    manifest,
                    installer);
                _processLauncher.Start(
                    verified.InstallerPath,
                    "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /UPDATE=1");
            }
            catch (Exception ex)
            {
                _exitCoordinator.ReleasePreparation();
                UpdateStatus = "The verified installer could not be started.";
                _log.Warning("Settings.Update", "Starting the verified update installer failed (" + ex.GetType().Name + ").");
                await _dialogs.ShowErrorAsync(
                    "Update could not start",
                    "The installer was downloaded and verified, but Windows did not start it.")
                    .ConfigureAwait(true);
                return false;
            }

            _exitCoordinator.Commit();
            _applicationController.Shutdown();
            return true;
        }

        internal static string FormatUpdateProgress(UpdateDownloadProgress download) =>
            download.TotalBytes > 0
                ? "Downloading update… " + download.Percentage.ToString(CultureInfo.CurrentCulture) + "%"
                : "Downloading update… " + FileSizeFormatter.Format(download.BytesReceived);

        [RelayCommand]
        private void OpenLogFolder() => OpenFolder(LogDirectory);

        [RelayCommand]
        private void OpenDataFolder() => OpenFolder(DataDirectory);

        [RelayCommand]
        private void OpenRepository() => OpenFolder(RepositoryUrl);

        private void OpenFolder(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                _log?.Warning("Settings.Open", "Opening '" + target + "' failed (" + ex.GetType().Name + ").");
            }
        }

        [RelayCommand]
        private async Task ForgetCredentialsAsync()
        {
            bool confirmed = await _dialogs.ConfirmAsync(
                "Forget saved credentials?",
                "Every stored Access Key ID and Secret Access Key is removed from this computer immediately. " +
                "You will need to enter them again to upload or browse.",
                "Forget credentials",
                isDestructive: true).ConfigureAwait(true);

            if (!confirmed) return;

            _session.ForgetAllCredentials();

            _draftCredentials.Clear();
            _originalCredentials.Clear();
            AccessKeyId = string.Empty;
            SecretAccessKey = string.Empty;

            UpdateDirty();
        }

        private bool CanSaveSettings() => CanSave;

        [RelayCommand(CanExecute = nameof(CanSaveSettings))]
        private async Task SaveAsync()
        {
            PushToDraft();

            if (HasValidationErrors)
            {
                SelectSection(EndpointError is not null || PublicBaseUrlError is not null ? "connection" : "transfers");
                return;
            }

            if (!_session.ApplyAndSave(_draft))
            {
                await _dialogs.ShowErrorAsync(
                    "Settings could not be saved",
                    "The settings file could not be written. Check that %LocalAppData% is writable and try again.")
                    .ConfigureAwait(true);
                return;
            }

            foreach (KeyValuePair<string, R2Credentials> entry in _draftCredentials)
            {
                _session.SetCredentials(entry.Key, entry.Value);
            }

            ApplyStartupRegistration();

            CloseRequested?.Invoke(this, true);
        }

        /// <summary>
        /// The Run key is only touched when the preference actually changed, so saving an
        /// unrelated setting never rewrites the registry.
        /// </summary>
        private void ApplyStartupRegistration()
        {
            if (_original.StartWithWindows == _draft.StartWithWindows) return;

            string executable = Environment.ProcessPath ?? AppContext.BaseDirectory;

            if (!_startup.SetEnabled(_draft.StartWithWindows, executable, out string? error))
            {
                _log?.Warning("Settings.Startup", "Start with Windows could not be updated: " + error);
            }
        }

        [RelayCommand]
        private async Task CancelAsync()
        {
            if (IsDirty)
            {
                bool discard = await _dialogs.ConfirmAsync(
                    "Discard changes?",
                    "The changes you made in Settings have not been saved.",
                    "Discard changes").ConfigureAwait(true);

                if (!discard) return;
            }

            CloseRequested?.Invoke(this, false);
        }

        // ------------------------------------------------------------------ change hooks

        partial void OnSelectedProfileChanged(R2BucketProfile? value) => LoadFromDraft();

        partial void OnAccountIdChanged(string value)
        {
            if (_loading) return;

            // Keep the derived endpoint in step while the user has not overridden it.
            string previousDerived = BuildDerivedEndpoint(SelectedProfile?.AccountId);
            if (string.Equals(Endpoint.Trim(), previousDerived, StringComparison.OrdinalIgnoreCase) ||
                Endpoint.Trim().Length == 0)
            {
                Endpoint = BuildDerivedEndpoint(value);
            }

            PushToDraft();
        }

        partial void OnBucketNameChanged(string value) => PushToDraft();
        partial void OnAccessKeyIdChanged(string value) => PushToDraft();
        partial void OnSecretAccessKeyChanged(string value) => PushToDraft();
        partial void OnEndpointChanged(string value) => PushToDraft();
        partial void OnPublicBaseUrlChanged(string value) => PushToDraft();
        partial void OnSelectedExpiryChanged(ExpiryChoice value) => PushToDraft();
        partial void OnPreferPublicUrlsChanged(bool value) => PushToDraft();
        partial void OnRememberCredentialsChanged(bool value) => PushToDraft();
        partial void OnForgetCredentialsOnExitChanged(bool value) => PushToDraft();
        partial void OnSelectedParallelChanged(ParallelChoice value) => PushToDraft();
        partial void OnMultipartThresholdTextChanged(string value) => PushToDraft();
        partial void OnPartSizeTextChanged(string value) => PushToDraft();
        partial void OnSelectedRetryChanged(RetryChoice value) => PushToDraft();
        partial void OnVerifyETagAfterUploadChanged(bool value) => PushToDraft();
        partial void OnStartWithWindowsChanged(bool value)
        {
            OnPropertyChanged(nameof(StartWithWindowsAndMinimized));
            PushToDraft();
        }

        partial void OnStartMinimizedChanged(bool value)
        {
            OnPropertyChanged(nameof(StartWithWindowsAndMinimized));
            PushToDraft();
        }
        partial void OnCloseToTrayChanged(bool value) => PushToDraft();
        partial void OnMinimizeToTrayChanged(bool value) => PushToDraft();
        partial void OnContinueTransfersWhileMinimizedChanged(bool value) => PushToDraft();
        partial void OnConfirmDeletesChanged(bool value) => PushToDraft();
        partial void OnThemeChanged(AppTheme value) => PushToDraft();
        partial void OnShowTrayNotificationsChanged(bool value) => PushToDraft();
        partial void OnNotifyOnTransferCompleteChanged(bool value) => PushToDraft();
        partial void OnNotifyOnTransferFailureChanged(bool value) => PushToDraft();
        partial void OnNotifyOnConnectionErrorChanged(bool value) => PushToDraft();
        partial void OnNotifyOnUpdateAvailableChanged(bool value) => PushToDraft();
        partial void OnCheckForUpdatesAutomaticallyChanged(bool value) => PushToDraft();
        partial void OnActivityRetentionTextChanged(string value) => PushToDraft();

        partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(CanSave));

        partial void OnTestStateChanged(ConnectionState value) => OnPropertyChanged(nameof(TestStateText));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            CancellationTokenSource? test = Interlocked.Exchange(ref _testCancellation, null);
            if (test is not null)
            {
                try { test.Cancel(); } catch (ObjectDisposedException) { }
                test.Dispose();
            }
        }
    }
}
