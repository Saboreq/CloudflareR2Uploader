using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Wpf.Services;
using CloudflareR2Uploader.Wpf.ViewModels.Activity;
using CloudflareR2Uploader.Wpf.ViewModels.Files;
using CloudflareR2Uploader.Wpf.ViewModels.Upload;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudflareR2Uploader.Wpf.ViewModels
{
    public enum ShellPage
    {
        Upload = 0,
        Files = 1,
        Activity = 2
    }

    /// <summary>One entry in the bucket selector at the top right of the header.</summary>
    public sealed class BucketChoice
    {
        public BucketChoice(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }

        public string DisplayName { get; }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// The application shell: header, navigation and which page is showing.
    /// <para>
    /// Owns nothing to do with R2 itself. It holds the three page view models, reflects the
    /// connection state in the header, and switches the active bucket profile.
    /// </para>
    /// </summary>
    public sealed partial class ShellViewModel : ObservableObject, IDisposable
    {
        private readonly IAppSession _session;
        private readonly IDialogService _dialogs;
        private readonly IThemeService _theme;
        private readonly R2ConnectionTester _connectionTester;
        private readonly ILoggingService _log;

        private CancellationTokenSource? _connectionCheck;
        private bool _suppressProfileWrite;
        private bool _disposed;

        public ShellViewModel(
            IAppSession session,
            IDialogService dialogs,
            IThemeService theme,
            R2ConnectionTester connectionTester,
            ILoggingService log,
            FilesViewModel files,
            UploadViewModel upload,
            ActivityViewModel activity,
            IToastService toasts)
        {
            _session = session;
            _dialogs = dialogs;
            _theme = theme;
            _connectionTester = connectionTester;
            _log = log;

            Files = files;
            Upload = upload;
            Activity = activity;
            Toasts = toasts;

            _session.Changed += OnSessionChanged;
            _session.ProfileChanged += OnProfileChanged;

            // Only the shell owns navigation, so the Files toolbar's Upload button asks for
            // the page change rather than performing it.
            Files.UploadPageRequested += OnUploadPageRequested;

            RefreshHeader();
            CurrentPageViewModel = Upload;
            Upload.OnActivated();
            _ = VerifyConnectionAsync();
        }

        public FilesViewModel Files { get; }

        public UploadViewModel Upload { get; }

        public ActivityViewModel Activity { get; }

        public IToastService Toasts { get; }

        public string Version { get; } = ApplicationInfo.DisplayVersion;

        public string Title => "Cloudflare R2 Uploader " + Version;

        [ObservableProperty]
        private ShellPage _currentPage = ShellPage.Upload;

        [ObservableProperty]
        private object? _currentPageViewModel;

        [ObservableProperty]
        private string _connectionText = "Not configured";

        [ObservableProperty]
        private ConnectionState _connectionState = ConnectionState.Unknown;

        /// <summary>"saboreq · auto (eu)" — account and region context beside the status dot.</summary>
        [ObservableProperty]
        private string _profileSummary = string.Empty;

        [ObservableProperty]
        private ObservableCollection<BucketChoice> _buckets = new();

        [ObservableProperty]
        private BucketChoice? _selectedBucket;

        public int ActivityCount => Activity.UnreadCount;

        partial void OnCurrentPageChanged(ShellPage value)
        {
            CurrentPageViewModel = value switch
            {
                ShellPage.Files => Files,
                ShellPage.Activity => Activity,
                _ => Upload
            };

            switch (value)
            {
                case ShellPage.Files:
                    Files.OnActivated();
                    break;
                case ShellPage.Activity:
                    Activity.OnActivated();
                    break;
                case ShellPage.Upload:
                    Upload.OnActivated();
                    break;
            }
        }

        partial void OnSelectedBucketChanged(BucketChoice? value)
        {
            if (_suppressProfileWrite || value is null) return;
            _session.SelectProfile(value.Id);
        }

        [RelayCommand]
        private void Navigate(ShellPage page) => CurrentPage = page;

        [RelayCommand]
        private void BrowseFiles()
        {
            CurrentPage = ShellPage.Upload;
            Upload.BrowseFilesCommand.Execute(null);
        }

        [RelayCommand]
        private void BrowseFolder()
        {
            CurrentPage = ShellPage.Upload;
            Upload.BrowseFolderCommand.Execute(null);
        }

        [RelayCommand]
        private void StartUploads()
        {
            CurrentPage = ShellPage.Upload;
            if (Upload.UploadAllCommand.CanExecute(null))
            {
                Upload.UploadAllCommand.Execute(null);
            }
        }

        [RelayCommand]
        private async Task OpenSettingsAsync() => await ShowSettingsAsync().ConfigureAwait(true);

        public async Task ShowSettingsAsync(string? initialSection = null)
        {
            bool saved = await _dialogs.ShowSettingsAsync(initialSection).ConfigureAwait(true);
            if (!saved) return;

            _theme.Apply(_session.Settings.Theme);
            RefreshHeader();
            await VerifyConnectionAsync().ConfigureAwait(true);
            Files.OnConnectionSettingsChanged();
            Upload.OnConnectionSettingsChanged();
        }

        [RelayCommand]
        private async Task TestConnectionAsync() => await VerifyConnectionAsync().ConfigureAwait(true);

        /// <summary>
        /// Runs a read-only bucket check so the header can say something truthful before the
        /// user tries an operation. Cancels any check already running, so switching profiles
        /// quickly cannot leave a stale result in the header.
        /// </summary>
        private async Task VerifyConnectionAsync()
        {
            CancellationTokenSource? previous = Interlocked.Exchange(ref _connectionCheck, new CancellationTokenSource());
            if (previous is not null)
            {
                try { previous.Cancel(); } catch (ObjectDisposedException) { }
                previous.Dispose();
            }

            CancellationTokenSource? current = _connectionCheck;
            if (current is null) return;

            AppSettings settings = _session.Settings;
            R2Credentials credentials = _session.Credentials;
            ProfileToken token = _session.Token;

            if (!_session.IsConfigured)
            {
                ConnectionState = ConnectionState.NotConfigured;
                ConnectionText = "Not configured";
                return;
            }

            ConnectionState = ConnectionState.Testing;
            ConnectionText = "Checking…";

            try
            {
                ConnectionTestResult result = await _connectionTester
                    .TestAsync(settings, credentials, current.Token)
                    .ConfigureAwait(true);

                // A slow check for the profile the user just left must not paint the header.
                if (_session.Token != token) return;

                if (result.IsSuccess)
                {
                    ConnectionState = ConnectionState.Connected;
                    ConnectionText = "Connected";
                }
                else
                {
                    ConnectionState = ConnectionState.Failed;
                    ConnectionText = result.Headline;
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer check.
            }
            catch (Exception ex)
            {
                if (_session.Token != token) return;
                ConnectionState = ConnectionState.Failed;
                ConnectionText = "Connection failed";
                _log?.Warning("Shell.Connection", "The header connection check failed (" + ex.GetType().Name + ").");
            }
        }

        private void OnSessionChanged(object? sender, EventArgs e) => RefreshHeader();

        private void OnUploadPageRequested(object? sender, EventArgs e) => CurrentPage = ShellPage.Upload;

        private void OnProfileChanged(object? sender, EventArgs e)
        {
            RefreshHeader();
            _ = VerifyConnectionAsync();
            Files.OnProfileChanged();
            Upload.OnProfileChanged();
            Activity.OnProfileChanged();
        }

        private void RefreshHeader()
        {
            AppSettings settings = _session.Settings;

            _suppressProfileWrite = true;
            try
            {
                Buckets = new ObservableCollection<BucketChoice>(
                    _session.Profiles.Select(static profile => new BucketChoice(profile.Id, profile.DisplayName)));

                SelectedBucket = Buckets.FirstOrDefault(
                    choice => string.Equals(choice.Id, settings.ActiveBucketProfileId, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _suppressProfileWrite = false;
            }

            string account = string.IsNullOrWhiteSpace(settings.AccountId) ? "no account" : settings.AccountId;
            if (account.Length > 12) account = account[..8] + "…";

            ProfileSummary = account + " · " + R2ClientFactory.SigningRegion;

            if (!_session.IsConfigured)
            {
                ConnectionState = ConnectionState.NotConfigured;
                ConnectionText = "Not configured";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _session.Changed -= OnSessionChanged;
            _session.ProfileChanged -= OnProfileChanged;
            Files.UploadPageRequested -= OnUploadPageRequested;

            CancellationTokenSource? check = Interlocked.Exchange(ref _connectionCheck, null);
            if (check is not null)
            {
                try { check.Cancel(); } catch (ObjectDisposedException) { }
                check.Dispose();
            }

            Files.Dispose();
            Upload.Dispose();
            Activity.Dispose();
        }
    }

    public enum ConnectionState
    {
        Unknown = 0,
        NotConfigured = 1,
        Testing = 2,
        Connected = 3,
        Failed = 4
    }
}
