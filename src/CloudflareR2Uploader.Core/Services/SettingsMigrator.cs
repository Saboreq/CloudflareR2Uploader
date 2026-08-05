using System;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    /// <summary>What a migration did, so the caller can log it and back the old file up.</summary>
    public sealed class SettingsMigrationResult
    {
        public SettingsMigrationResult(int fromVersion, int toVersion, bool changed)
        {
            FromVersion = fromVersion;
            ToVersion = toVersion;
            Changed = changed;
        }

        /// <summary>The version found in the file. 1 when the file predates <c>schemaVersion</c>.</summary>
        public int FromVersion { get; private set; }

        public int ToVersion { get; private set; }

        /// <summary>True when the settings object was actually upgraded and should be saved.</summary>
        public bool Changed { get; private set; }
    }

    /// <summary>
    /// Upgrades a settings object loaded from disk to <see cref="AppSettings.CurrentSchemaVersion"/>.
    /// <para>
    /// The rules are deliberately additive. A field that an older build did not know about
    /// is seeded with the value that reproduces that build's behaviour, and no existing
    /// field is ever dropped or reinterpreted. Bucket profiles and their DPAPI-protected
    /// credentials are never touched here, and neither is resumable multipart state.
    /// </para>
    /// <para>
    /// Both front ends run this, so a settings file stays usable if the user moves back to
    /// the WinForms build.
    /// </para>
    /// </summary>
    public static class SettingsMigrator
    {
        /// <summary>
        /// A file written before the WPF front end has no <c>schemaVersion</c> member at all,
        /// so <see cref="AppSettings.SchemaVersion"/> deserialises as 0. It is version 1.
        /// </summary>
        private const int UnversionedSchema = 1;

        public static SettingsMigrationResult Migrate(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            int fromVersion = settings.SchemaVersion <= 0 ? UnversionedSchema : settings.SchemaVersion;

            // A file from a newer build must not be silently downgraded: unknown members are
            // already preserved by the serialiser, so the safest action is to leave it alone.
            if (fromVersion >= AppSettings.CurrentSchemaVersion)
            {
                bool stamped = settings.SchemaVersion != fromVersion;
                settings.SchemaVersion = fromVersion;
                return new SettingsMigrationResult(fromVersion, fromVersion, stamped);
            }

            if (fromVersion < 2) MigrateOneToTwo(settings);

            settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
            settings.Clamp();
            return new SettingsMigrationResult(fromVersion, AppSettings.CurrentSchemaVersion, true);
        }

        /// <summary>
        /// Schema 1 → 2: the fields the WPF front end added. Every seed reproduces what the
        /// WinForms build did, so nothing about the user's experience changes on upgrade.
        /// </summary>
        private static void MigrateOneToTwo(AppSettings settings)
        {
            // The WinForms build had no theme picker and was dark only.
            settings.Theme = AppTheme.Dark;

            // It also had no configurable expiry; the temporary-link dialog defaulted to a day.
            if (settings.TemporaryLinkExpiryHours <= 0) settings.TemporaryLinkExpiryHours = 24;

            // "Copy public URL" was an explicit, separate action there. Preferring the public
            // URL is only meaningful when the profile actually has a public base URL, so the
            // preference starts on exactly for users who configured one.
            settings.PreferPublicUrls = !string.IsNullOrWhiteSpace(settings.PublicBaseUrl);

            // These were unconditional behaviours rather than settings.
            settings.VerifyETagAfterUpload = true;
            settings.ContinueTransfersWhileMinimized = true;
            settings.ConfirmDeletes = true;

            // Credentials were kept unless the user cleared them.
            settings.ForgetCredentialsOnExit = false;

            // Activity history is new; it starts enabled with the documented default window.
            if (settings.ActivityRetentionDays <= 0) settings.ActivityRetentionDays = 30;

            // The WinForms build had one aggregate "show tray notifications" switch. Honour it
            // rather than turning every new per-event toggle on regardless.
            settings.NotifyOnTransferComplete = settings.ShowTrayNotifications;
            settings.NotifyOnTransferFailure = settings.ShowTrayNotifications;
            settings.NotifyOnConnectionError = settings.ShowTrayNotifications;
            settings.NotifyOnUpdateAvailable = settings.CheckForUpdatesAutomatically;
        }
    }
}
