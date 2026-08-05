using System;
using System.Collections.Generic;
using System.Linq;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;

namespace CloudflareR2Uploader.Wpf.Services
{
    /// <summary>Identifies which bucket profile a result belongs to.</summary>
    /// <remarks>
    /// Switching profiles must never let a listing, preview or metadata response that was
    /// already in flight paint into the new profile's view. Every asynchronous caller
    /// captures the token before it starts and compares it before it applies a result.
    /// </remarks>
    public readonly record struct ProfileToken(string ProfileId, long Generation)
    {
        public static readonly ProfileToken None = new(string.Empty, 0);
    }

    /// <summary>
    /// Owns the live application state: the current settings, the credential vault for every
    /// bucket profile, and which profile is active.
    /// <para>
    /// Views and view models never read <c>settings.json</c> or the DPAPI vault directly.
    /// They ask for a snapshot, which is a clone, so a background operation can never observe
    /// settings being edited in the Settings dialog half-way through.
    /// </para>
    /// </summary>
    public interface IAppSession
    {
        /// <summary>An immutable snapshot of the settings currently in force.</summary>
        AppSettings Settings { get; }

        /// <summary>Credentials for the active profile. Never null; may be incomplete.</summary>
        R2Credentials Credentials { get; }

        ProfileToken Token { get; }

        R2BucketProfile ActiveProfile { get; }

        IReadOnlyList<R2BucketProfile> Profiles { get; }

        bool IsConfigured { get; }

        /// <summary>Raised after settings, credentials or the active profile change.</summary>
        event EventHandler? Changed;

        /// <summary>Raised specifically when the active profile changes.</summary>
        event EventHandler? ProfileChanged;

        void SelectProfile(string profileId);

        /// <summary>Replaces the in-memory settings and persists them. Returns false on write failure.</summary>
        bool ApplyAndSave(AppSettings settings);

        /// <summary>Replaces the credential for one profile in memory, and on disk when remembered.</summary>
        bool SetCredentials(string profileId, R2Credentials credentials);

        R2Credentials GetCredentials(string profileId);

        /// <summary>Removes every stored credential from memory and from disk.</summary>
        bool ForgetAllCredentials();
    }

    public sealed class AppSession : IAppSession
    {
        private readonly ILoggingService _log;
        private readonly SettingsService _settingsService;
        private readonly CredentialProtectionService _credentialService;
        private readonly Dictionary<string, R2Credentials> _credentials;
        private readonly object _sync = new();

        private AppSettings _settings;
        private long _generation;

        public AppSession(ILoggingService log, SettingsService settingsService, CredentialProtectionService credentialService)
        {
            _log = log;
            _settingsService = settingsService;
            _credentialService = credentialService;

            _settings = _settingsService.Load();
            _settings.NormalizeBucketProfiles();

            _credentials = _credentialService.LoadProfiles(_settings.ActiveBucketProfileId)
                           ?? new Dictionary<string, R2Credentials>(StringComparer.OrdinalIgnoreCase);
        }

        public AppSettings Settings { get { lock (_sync) { return _settings.Clone(); } } }

        public R2Credentials Credentials
        {
            get
            {
                lock (_sync) { return GetCredentialsUnlocked(_settings.ActiveBucketProfileId); }
            }
        }

        public ProfileToken Token
        {
            get { lock (_sync) { return new ProfileToken(_settings.ActiveBucketProfileId ?? string.Empty, _generation); } }
        }

        public R2BucketProfile ActiveProfile
        {
            get { lock (_sync) { return (_settings.GetActiveBucketProfile() ?? new R2BucketProfile()).Clone(); } }
        }

        public IReadOnlyList<R2BucketProfile> Profiles
        {
            get
            {
                lock (_sync)
                {
                    return _settings.BucketProfiles.Select(static profile => profile.Clone()).ToList();
                }
            }
        }

        public bool IsConfigured
        {
            get
            {
                lock (_sync)
                {
                    if (string.IsNullOrWhiteSpace(_settings.BucketName)) return false;
                    if (string.IsNullOrWhiteSpace(_settings.AccountId) &&
                        string.IsNullOrWhiteSpace(_settings.CustomEndpoint)) return false;

                    return GetCredentialsUnlocked(_settings.ActiveBucketProfileId).IsComplete;
                }
            }
        }

        public event EventHandler? Changed;

        public event EventHandler? ProfileChanged;

        public void SelectProfile(string profileId)
        {
            bool switched;
            lock (_sync)
            {
                if (string.Equals(_settings.ActiveBucketProfileId, profileId, StringComparison.OrdinalIgnoreCase)) return;

                switched = _settings.SelectBucketProfile(profileId);
                if (switched) _generation++;
            }

            if (!switched) return;

            // The active profile is part of the persisted settings, so a switch is saved
            // immediately: it is a navigation choice, not an unsaved edit in a dialog.
            _settingsService.Save(Settings);

            _log?.Info("Session.Profile", "Active bucket profile changed.");
            ProfileChanged?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public bool ApplyAndSave(AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            AppSettings candidate = settings.Clone();
            candidate.Clamp();

            bool profileChanged;
            lock (_sync)
            {
                profileChanged = !string.Equals(
                    _settings.ActiveBucketProfileId, candidate.ActiveBucketProfileId, StringComparison.OrdinalIgnoreCase);

                _settings = candidate;
                if (profileChanged) _generation++;
            }

            bool saved = _settingsService.Save(candidate.Clone());

            if (profileChanged) ProfileChanged?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
            return saved;
        }

        public bool SetCredentials(string profileId, R2Credentials credentials)
        {
            if (string.IsNullOrWhiteSpace(profileId)) return false;

            bool remember;
            Dictionary<string, R2Credentials> snapshot;

            lock (_sync)
            {
                _credentials[profileId.Trim()] = credentials ?? R2Credentials.Empty;
                remember = _settings.RememberCredentials;
                snapshot = new Dictionary<string, R2Credentials>(_credentials, StringComparer.OrdinalIgnoreCase);
            }

            bool saved = true;
            if (remember) saved = _credentialService.SaveProfiles(snapshot);
            else _credentialService.Clear();

            Changed?.Invoke(this, EventArgs.Empty);
            return saved;
        }

        public R2Credentials GetCredentials(string profileId)
        {
            lock (_sync) { return GetCredentialsUnlocked(profileId); }
        }

        public bool ForgetAllCredentials()
        {
            lock (_sync) { _credentials.Clear(); }

            bool cleared = _credentialService.Clear();
            _log?.Info("Session.Credentials", "Stored credentials were cleared at the user's request.");
            Changed?.Invoke(this, EventArgs.Empty);
            return cleared;
        }

        private R2Credentials GetCredentialsUnlocked(string? profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId)) return R2Credentials.Empty;
            return _credentials.TryGetValue(profileId!.Trim(), out R2Credentials? found)
                ? found
                : R2Credentials.Empty;
        }
    }
}
