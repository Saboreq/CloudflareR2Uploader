using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Loads and saves the non-secret settings JSON. Uses
    /// <see cref="DataContractJsonSerializer"/> from the framework, so no JSON package needs
    /// to be shipped inside the executable.
    /// <para>Credentials are handled separately by <see cref="CredentialProtectionService"/>
    /// and never appear in this file.</para>
    /// </summary>
    public sealed class SettingsService
    {
        private readonly ILoggingService _log;
        private readonly string _filePath;

        public SettingsService(ILoggingService log, string filePath = null)
        {
            _log = log;
            _filePath = string.IsNullOrEmpty(filePath) ? AppPaths.SettingsFilePath : filePath;
        }

        public string FilePath { get { return _filePath; } }

        /// <summary>Reads settings, falling back to defaults when the file is absent or unreadable.</summary>
        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    AppSettings fresh = new AppSettings();
                    fresh.SchemaVersion = AppSettings.CurrentSchemaVersion;
                    fresh.Clamp();
                    return fresh;
                }

                AppSettings settings;
                using (FileStream stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    DataContractJsonSerializer serializer = CreateSerializer();
                    settings = serializer.ReadObject(stream) as AppSettings;
                }

                if (settings == null)
                {
                    if (_log != null) _log.Warning("Settings.Load", "The settings file was empty; defaults applied.");
                    settings = new AppSettings();
                    settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
                }

                settings.Clamp();
                ApplySchemaMigration(settings);
                return settings;
            }
            catch (Exception ex) when (IsRecoverableLoadFailure(ex))
            {
                if (_log != null)
                    _log.Error("Settings.Load", "The settings file could not be read; defaults applied.", ex);

                TryQuarantineCorruptFile();

                AppSettings fallback = new AppSettings();
                fallback.Clamp();
                return fallback;
            }
        }

        /// <summary>Writes settings atomically. Returns false if the file could not be written.</summary>
        public bool Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            try
            {
                settings.Clamp();
                AppPaths.EnsureDirectory(Path.GetDirectoryName(_filePath));

                string temp = _filePath + ".tmp";

                using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (JsonWriter writer = new JsonWriter(stream))
                {
                    CreateSerializer().WriteObject(writer.Writer, settings);
                }

                if (File.Exists(_filePath)) File.Delete(_filePath);
                File.Move(temp, _filePath);

                if (_log != null) _log.Info("Settings.Save", "Settings saved (no credential fields are stored in this file).");
                return true;
            }
            catch (IOException ex)
            {
                if (_log != null) _log.Error("Settings.Save", "The settings file could not be written.", ex);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                if (_log != null) _log.Error("Settings.Save", "Access to the settings file was denied.", ex);
                return false;
            }
        }

        /// <summary>
        /// Upgrades a file written by an older schema and keeps a one-off copy of the
        /// original next to it, so downgrading to the previous build stays possible.
        /// A failure here never prevents the application from starting: the in-memory
        /// settings are already migrated, only the backup and the stamp are best effort.
        /// </summary>
        private void ApplySchemaMigration(AppSettings settings)
        {
            SettingsMigrationResult result = SettingsMigrator.Migrate(settings);
            if (!result.Changed || result.FromVersion == result.ToVersion) return;

            if (_log != null)
            {
                _log.Info("Settings.Migrate",
                    "Settings upgraded from schema " + result.FromVersion + " to " + result.ToVersion + ".");
            }

            try
            {
                string backup = _filePath + ".schema" + result.FromVersion + ".bak";
                if (!File.Exists(backup) && File.Exists(_filePath)) File.Copy(_filePath, backup);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            Save(settings);
        }

        private static DataContractJsonSerializer CreateSerializer()
        {
            return new DataContractJsonSerializer(typeof(AppSettings));
        }

        private static bool IsRecoverableLoadFailure(Exception ex)
        {
            return ex is IOException
                || ex is UnauthorizedAccessException
                || ex is System.Runtime.Serialization.SerializationException
                || ex is System.Xml.XmlException
                || ex is FormatException
                || ex is ArgumentException;
        }

        private void TryQuarantineCorruptFile()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                string backup = _filePath + ".invalid";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(_filePath, backup);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>
        /// Wraps a <see cref="JsonWriterDelegator"/>-style indented writer so the settings file
        /// is readable when a user opens it, instead of one long line.
        /// </summary>
        private sealed class JsonWriter : IDisposable
        {
            private readonly System.Xml.XmlDictionaryWriter _writer;

            public JsonWriter(Stream stream)
            {
                _writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  ");
            }

            public System.Xml.XmlDictionaryWriter Writer { get { return _writer; } }

            public void Dispose()
            {
                _writer.Flush();
                _writer.Close();
            }
        }
    }
}
