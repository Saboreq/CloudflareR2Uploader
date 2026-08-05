using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Persists the R2 credential pair using Windows DPAPI under
    /// <see cref="DataProtectionScope.CurrentUser"/>. There is no key in the source: only the
    /// signed-in Windows user on this machine can decrypt the blob.
    /// </summary>
    public sealed class CredentialProtectionService
    {
        /// <summary>Extra entropy, mixed with the user's DPAPI key. Not a secret by itself.</summary>
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CloudflareR2Uploader.credentials.v1");

        private const string LegacyMagic = "R2CRED1";
        private const string ProfileMagic = "R2CRED2";
        private const int MaxProfileCount = 256;
        private const int MaxFieldBytes = 1024 * 1024;

        private readonly ILoggingService _log;
        private readonly string _filePath;

        public CredentialProtectionService(ILoggingService log, string filePath = null)
        {
            _log = log;
            _filePath = string.IsNullOrEmpty(filePath) ? AppPaths.CredentialFilePath : filePath;
        }

        public string FilePath { get { return _filePath; } }

        public bool HasSavedCredentials
        {
            get
            {
                try { return File.Exists(_filePath); }
                catch (IOException) { return false; }
            }
        }

        /// <summary>Encrypts and writes the credential pair. Overwrites any previous blob.</summary>
        public bool Save(R2Credentials credentials)
        {
            if (credentials == null) throw new ArgumentNullException("credentials");

            byte[] plaintext = null;
            try
            {
                plaintext = EncodeLegacy(credentials);
                return ProtectAndWrite(plaintext, "Encrypted credentials written with DPAPI (CurrentUser).");
            }
            finally
            {
                ZeroFill(plaintext);
            }
        }

        /// <summary>
        /// Encrypts credentials for multiple bucket profiles in one DPAPI-protected vault.
        /// Incomplete pairs are intentionally omitted.
        /// </summary>
        public bool SaveProfiles(IDictionary<string, R2Credentials> credentialsByProfile)
        {
            if (credentialsByProfile == null) throw new ArgumentNullException("credentialsByProfile");

            byte[] plaintext = null;
            try
            {
                plaintext = EncodeProfiles(credentialsByProfile);
                return ProtectAndWrite(plaintext,
                    "Encrypted credentials for " + CountComplete(credentialsByProfile) +
                    " bucket profile(s) written with DPAPI (CurrentUser).");
            }
            finally
            {
                ZeroFill(plaintext);
            }
        }

        /// <summary>
        /// Reads and decrypts the credential pair. Returns null when nothing is stored or the
        /// blob cannot be decrypted (for example after a Windows profile change).
        /// </summary>
        public R2Credentials Load()
        {
            byte[] plaintext = null;

            try
            {
                plaintext = ReadAndUnprotect();
                if (plaintext == null) return null;

                if (HasMagic(plaintext, LegacyMagic)) return DecodeLegacy(plaintext);

                Dictionary<string, R2Credentials> profiles = DecodeProfiles(plaintext);
                foreach (KeyValuePair<string, R2Credentials> entry in profiles)
                    return entry.Value;
                return null;
            }
            catch (FormatException ex)
            {
                if (_log != null)
                    _log.Warning("Credentials.Load", "The credential file has an unexpected format (" + ex.GetType().Name + ").");
                return null;
            }
            finally
            {
                ZeroFill(plaintext);
            }
        }

        /// <summary>
        /// Loads the credential vault. A legacy single-pair blob is assigned to
        /// <paramref name="legacyProfileId"/>, which makes upgrades transparent.
        /// </summary>
        public Dictionary<string, R2Credentials> LoadProfiles(string legacyProfileId)
        {
            byte[] plaintext = null;
            try
            {
                plaintext = ReadAndUnprotect();
                if (plaintext == null)
                    return new Dictionary<string, R2Credentials>(StringComparer.OrdinalIgnoreCase);

                if (HasMagic(plaintext, LegacyMagic))
                {
                    Dictionary<string, R2Credentials> migrated =
                        new Dictionary<string, R2Credentials>(StringComparer.OrdinalIgnoreCase);
                    R2Credentials legacy = DecodeLegacy(plaintext);
                    if (legacy != null && legacy.IsComplete && !string.IsNullOrWhiteSpace(legacyProfileId))
                        migrated[legacyProfileId.Trim()] = legacy;
                    return migrated;
                }

                return DecodeProfiles(plaintext);
            }
            catch (FormatException ex)
            {
                if (_log != null)
                    _log.Warning("Credentials.Load", "The credential file has an unexpected format (" + ex.GetType().Name + ").");
                return new Dictionary<string, R2Credentials>(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                ZeroFill(plaintext);
            }
        }

        private bool ProtectAndWrite(byte[] plaintext, string logMessage)
        {
            byte[] protectedBytes = null;
            try
            {
                protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

                AppPaths.EnsureDirectory(Path.GetDirectoryName(_filePath));
                string temp = _filePath + ".tmp";
                File.WriteAllBytes(temp, protectedBytes);
                if (File.Exists(_filePath)) File.Delete(_filePath);
                File.Move(temp, _filePath);

                if (_log != null) _log.Info("Credentials.Save", logMessage);
                return true;
            }
            catch (CryptographicException ex)
            {
                if (_log != null) _log.Error("Credentials.Save", "DPAPI protection failed.", ex);
                return false;
            }
            catch (IOException ex)
            {
                if (_log != null) _log.Error("Credentials.Save", "Could not write the credential file.", ex);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                if (_log != null) _log.Error("Credentials.Save", "Access to the credential file was denied.", ex);
                return false;
            }
            finally
            {
                ZeroFill(protectedBytes);
            }
        }

        private byte[] ReadAndUnprotect()
        {
            byte[] protectedBytes = null;
            try
            {
                if (!File.Exists(_filePath)) return null;
                protectedBytes = File.ReadAllBytes(_filePath);
                if (protectedBytes.Length == 0) return null;
                return ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException ex)
            {
                if (_log != null)
                    _log.Warning("Credentials.Load", "Saved credentials could not be decrypted for this Windows user (" + ex.GetType().Name + ").");
                return null;
            }
            catch (IOException ex)
            {
                if (_log != null) _log.Error("Credentials.Load", "Could not read the credential file.", ex);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                if (_log != null) _log.Error("Credentials.Load", "Access to the credential file was denied.", ex);
                return null;
            }
            catch (FormatException ex)
            {
                if (_log != null) _log.Warning("Credentials.Load", "The credential file has an unexpected format (" + ex.GetType().Name + ").");
                return null;
            }
            finally
            {
                ZeroFill(protectedBytes);
            }
        }

        /// <summary>Deletes the stored blob. Returns true when nothing remains on disk afterwards.</summary>
        public bool Clear()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    // Overwrite before deleting so the ciphertext does not linger in free space.
                    try
                    {
                        long length = new FileInfo(_filePath).Length;
                        if (length > 0 && length < 1024 * 64)
                        {
                            using (FileStream stream = new FileStream(_filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                            {
                                byte[] scratch = new byte[length];
                                stream.Write(scratch, 0, scratch.Length);
                                stream.Flush();
                            }
                        }
                    }
                    catch (IOException) { }

                    File.Delete(_filePath);
                }

                string temp = _filePath + ".tmp";
                if (File.Exists(temp)) File.Delete(temp);

                if (_log != null) _log.Info("Credentials.Clear", "Saved credentials removed.");
                return true;
            }
            catch (IOException ex)
            {
                if (_log != null) _log.Error("Credentials.Clear", "Could not delete the credential file.", ex);
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                if (_log != null) _log.Error("Credentials.Clear", "Access to the credential file was denied.", ex);
                return false;
            }
        }

        // Format: "R2CRED1" \n <accessKeyIdLength:int32-LE> <accessKeyId UTF8> <secret UTF8>
        // A hand-rolled frame keeps the secret out of any general-purpose serialiser.
        private static byte[] EncodeLegacy(R2Credentials credentials)
        {
            byte[] magic = Encoding.ASCII.GetBytes(LegacyMagic);
            byte[] id = Encoding.UTF8.GetBytes(credentials.AccessKeyId ?? string.Empty);
            byte[] secret = Encoding.UTF8.GetBytes(credentials.SecretAccessKey ?? string.Empty);

            byte[] buffer = new byte[magic.Length + 4 + id.Length + secret.Length];
            int offset = 0;

            Buffer.BlockCopy(magic, 0, buffer, offset, magic.Length);
            offset += magic.Length;

            buffer[offset++] = (byte)(id.Length & 0xFF);
            buffer[offset++] = (byte)((id.Length >> 8) & 0xFF);
            buffer[offset++] = (byte)((id.Length >> 16) & 0xFF);
            buffer[offset++] = (byte)((id.Length >> 24) & 0xFF);

            Buffer.BlockCopy(id, 0, buffer, offset, id.Length);
            offset += id.Length;
            Buffer.BlockCopy(secret, 0, buffer, offset, secret.Length);

            ZeroFill(id);
            ZeroFill(secret);
            return buffer;
        }

        private static R2Credentials DecodeLegacy(byte[] plaintext)
        {
            byte[] magic = Encoding.ASCII.GetBytes(LegacyMagic);
            if (plaintext == null || plaintext.Length < magic.Length + 4)
                throw new FormatException("The credential blob is too short.");

            for (int i = 0; i < magic.Length; i++)
            {
                if (plaintext[i] != magic[i]) throw new FormatException("Unrecognised credential blob header.");
            }

            int offset = magic.Length;
            int idLength = plaintext[offset]
                           | (plaintext[offset + 1] << 8)
                           | (plaintext[offset + 2] << 16)
                           | (plaintext[offset + 3] << 24);
            offset += 4;

            if (idLength < 0 || offset + idLength > plaintext.Length)
                throw new FormatException("The credential blob is malformed.");

            string accessKeyId = Encoding.UTF8.GetString(plaintext, offset, idLength);
            offset += idLength;

            string secret = Encoding.UTF8.GetString(plaintext, offset, plaintext.Length - offset);
            return new R2Credentials(accessKeyId, secret);
        }

        private static byte[] EncodeProfiles(IDictionary<string, R2Credentials> credentialsByProfile)
        {
            int completeCount = CountComplete(credentialsByProfile);
            if (completeCount > MaxProfileCount)
                throw new ArgumentException("Too many credential profiles.", "credentialsByProfile");

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Encoding.ASCII.GetBytes(ProfileMagic));
                writer.Write(completeCount);

                foreach (KeyValuePair<string, R2Credentials> entry in credentialsByProfile)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) ||
                        entry.Value == null ||
                        !entry.Value.IsComplete)
                        continue;

                    WriteField(writer, entry.Key.Trim());
                    WriteField(writer, entry.Value.AccessKeyId);
                    WriteField(writer, entry.Value.SecretAccessKey);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        private static Dictionary<string, R2Credentials> DecodeProfiles(byte[] plaintext)
        {
            if (!HasMagic(plaintext, ProfileMagic))
                throw new FormatException("Unrecognised credential blob header.");

            Dictionary<string, R2Credentials> result =
                new Dictionary<string, R2Credentials>(StringComparer.OrdinalIgnoreCase);

            using (MemoryStream stream = new MemoryStream(plaintext, false))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                reader.ReadBytes(ProfileMagic.Length);
                int count = reader.ReadInt32();
                if (count < 0 || count > MaxProfileCount)
                    throw new FormatException("The credential profile count is invalid.");

                for (int i = 0; i < count; i++)
                {
                    string profileId = ReadField(reader);
                    string accessKeyId = ReadField(reader);
                    string secret = ReadField(reader);
                    if (string.IsNullOrWhiteSpace(profileId) || result.ContainsKey(profileId))
                        throw new FormatException("The credential profile identifier is invalid.");

                    result.Add(profileId, new R2Credentials(accessKeyId, secret));
                }

                if (stream.Position != stream.Length)
                    throw new FormatException("The credential blob contains trailing data.");
            }

            return result;
        }

        private static void WriteField(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            try
            {
                if (bytes.Length > MaxFieldBytes)
                    throw new ArgumentException("A credential field is too large.");
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
            finally
            {
                ZeroFill(bytes);
            }
        }

        private static string ReadField(BinaryReader reader)
        {
            int length;
            try { length = reader.ReadInt32(); }
            catch (EndOfStreamException) { throw new FormatException("The credential blob is truncated."); }

            if (length < 0 || length > MaxFieldBytes)
                throw new FormatException("A credential field length is invalid.");

            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new FormatException("The credential blob is truncated.");

            try { return Encoding.UTF8.GetString(bytes); }
            finally { ZeroFill(bytes); }
        }

        private static int CountComplete(IDictionary<string, R2Credentials> credentialsByProfile)
        {
            int count = 0;
            foreach (KeyValuePair<string, R2Credentials> entry in credentialsByProfile)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) &&
                    entry.Value != null &&
                    entry.Value.IsComplete)
                    count++;
            }
            return count;
        }

        private static bool HasMagic(byte[] plaintext, string magicText)
        {
            byte[] magic = Encoding.ASCII.GetBytes(magicText);
            if (plaintext == null || plaintext.Length < magic.Length) return false;
            for (int i = 0; i < magic.Length; i++)
                if (plaintext[i] != magic[i]) return false;
            return true;
        }

        private static void ZeroFill(byte[] buffer)
        {
            if (buffer == null) return;
            Array.Clear(buffer, 0, buffer.Length);
        }
    }
}
