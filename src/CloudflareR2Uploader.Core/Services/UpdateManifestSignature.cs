using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    public static class UpdateManifestSignature
    {
        public const string Algorithm = "RSA-PSS-SHA256";

        private const string PayloadMagic = "CloudflareR2Uploader.UpdateManifest.v1";
        private const long MaximumInstallerBytes = 256L * 1024L * 1024L;
        private const int MaximumNotesCharacters = 8000;
        internal const int MaximumVersionCharacters = 128;
        private const int MaximumUrlCharacters = 4096;
        private const int MaximumEncodedKeyOrSignatureCharacters = 16 * 1024;

        public static void Sign(UpdateManifest manifest, RSA privateKey)
        {
            ValidateShape(manifest);
            ArgumentNullException.ThrowIfNull(privateKey);
            if (privateKey.KeySize < 2048) throw new CryptographicException("The update signing key must be at least 2048 bits.");

            manifest.SignatureAlgorithm = Algorithm;
            manifest.Signature = Convert.ToBase64String(privateKey.SignData(
                CreatePayloadCore(manifest),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss));
        }

        public static bool Verify(UpdateManifest manifest, string subjectPublicKeyInfo)
        {
            try
            {
                ValidateShape(manifest);
                if (!string.Equals(manifest.SignatureAlgorithm, Algorithm, StringComparison.Ordinal)) return false;
                if (!TryDecodeCanonicalBase64(manifest.Signature, out byte[] signature)) return false;
                if (!TryDecodeCanonicalBase64(subjectPublicKeyInfo, out byte[] publicKey)) return false;

                using RSA rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
                if (bytesRead != publicKey.Length || rsa.KeySize < 2048) return false;
                if (signature.Length != (rsa.KeySize + 7) / 8) return false;

                return rsa.VerifyData(
                    CreatePayloadCore(manifest),
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        internal static byte[] CreatePayload(UpdateManifest manifest)
        {
            ValidateShape(manifest);
            return CreatePayloadCore(manifest);
        }

        internal static void ValidateShape(UpdateManifest manifest)
        {
            if (manifest == null || manifest.SchemaVersion != 1)
                throw new InvalidDataException("The update manifest schema is not supported.");

            if (!IsValidManifestVersion(manifest.Version))
                throw new InvalidDataException("The update manifest version is invalid.");

            if (!TryHttpsUri(manifest.InstallerUrl, out _))
                throw new InvalidDataException("The installer must use an absolute HTTPS URL.");

            if (manifest.SizeBytes <= 0 || manifest.SizeBytes > MaximumInstallerBytes)
                throw new InvalidDataException("The installer size is outside the supported range.");

            if (!IsLowercaseSha256(manifest.Sha256))
                throw new InvalidDataException("The update manifest SHA-256 is invalid.");

            if (!manifest.PublishedUtc.HasValue || manifest.PublishedUtc.Value < DateTimeOffset.UnixEpoch)
                throw new InvalidDataException("The update manifest publication timestamp is invalid.");

            if (!HasValidUnicodeScalarLength(manifest.Notes, MaximumNotesCharacters))
                throw new InvalidDataException("The update manifest release notes are invalid.");
        }

        private static bool HasValidUnicodeScalarLength(string value, int maximumScalars)
        {
            if (value == null) return false;

            int scalars = 0;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (char.IsHighSurrogate(current))
                {
                    if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                        return false;
                    index++;
                }
                else if (char.IsLowSurrogate(current))
                {
                    return false;
                }

                scalars++;
                if (scalars > maximumScalars) return false;
            }

            return true;
        }

        public static bool IsValidPublicKey(string subjectPublicKeyInfo)
        {
            try
            {
                if (!TryDecodeCanonicalBase64(subjectPublicKeyInfo, out byte[] publicKey)) return false;
                using RSA rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
                return bytesRead == publicKey.Length && rsa.KeySize >= 2048;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        internal static bool TryImportPublicKey(string subjectPublicKeyInfo)
        {
            return IsValidPublicKey(subjectPublicKeyInfo);
        }

        internal static bool IsValidManifestVersion(string version)
        {
            return !string.IsNullOrEmpty(version) &&
                version.Length <= MaximumVersionCharacters &&
                string.Equals(version, version.Trim(), StringComparison.Ordinal) &&
                !version.Contains('+') &&
                SemanticVersion.TryParse(version, out _);
        }

        private static byte[] CreatePayloadCore(UpdateManifest manifest)
        {
            using MemoryStream buffer = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(buffer, new UTF8Encoding(false, true), true))
            {
                writer.Write(PayloadMagic);
                writer.Write(manifest.SchemaVersion);
                writer.Write(manifest.Version);
                writer.Write(manifest.InstallerUrl);
                writer.Write(manifest.Sha256.ToLowerInvariant());
                writer.Write(manifest.SizeBytes);
                writer.Write(manifest.PublishedUtc.Value.UtcDateTime.Ticks);
                writer.Write(manifest.Notes);
            }

            return buffer.ToArray();
        }

        private static bool TryDecodeCanonicalBase64(string value, out byte[] decoded)
        {
            decoded = Array.Empty<byte>();
            if (string.IsNullOrEmpty(value) || value.Length > MaximumEncodedKeyOrSignatureCharacters)
                return false;

            try
            {
                decoded = Convert.FromBase64String(value);
                return string.Equals(Convert.ToBase64String(decoded), value, StringComparison.Ordinal);
            }
            catch (FormatException)
            {
                decoded = Array.Empty<byte>();
                return false;
            }
        }

        private static bool IsLowercaseSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    return false;
            }

            return true;
        }

        private static bool TryHttpsUri(string value, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrEmpty(value) || value.Length > MaximumUrlCharacters ||
                !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
                !Uri.TryCreate(value, UriKind.Absolute, out Uri parsed) ||
                !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(parsed.Host) ||
                !string.IsNullOrEmpty(parsed.UserInfo) ||
                !string.IsNullOrEmpty(parsed.Fragment))
                return false;

            uri = parsed;
            return true;
        }
    }
}
