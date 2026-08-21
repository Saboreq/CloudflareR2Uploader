using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;

namespace CloudflareR2Uploader.UpdateSigner
{
    internal static class Program
    {
        public static int Main(string[] args)
        {
            return SignerCommand.Run(args, Console.Out, Console.Error);
        }
    }

    internal static class SignerCommand
    {
        private const long MaximumInstallerBytes = 256L * 1024L * 1024L;
        private const int MaximumManifestBytes = 64 * 1024;
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false, true);
        private static readonly string[] SignOptionalValueOptions = ["--notes"];
        private static readonly JsonSerializerOptions WriteJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        internal static int Run(string[] args, TextWriter output, TextWriter error)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);

            try
            {
                if (args.Length == 0) throw new CommandException(Usage);

                switch (args[0])
                {
                    case "export-public-key":
                        ExportPublicKey(ParseOptions(
                            args,
                            "--pfx",
                            "--password-env"), output);
                        break;
                    case "sign":
                        Sign(ParseOptionsWithOptional(
                            args,
                            "--held-read-lock",
                            SignOptionalValueOptions,
                            "--pfx",
                            "--password-env",
                            "--version",
                            "--installer",
                            "--installer-url",
                            "--output"), output);
                        break;
                    case "validate-public-key":
                        ValidatePublicKey(ParseOptions(
                            args,
                            "--public-key"), output);
                        break;
                    case "validate-installer-metadata":
                        ValidateInstallerMetadata(ParseOptions(
                            args,
                            "--installer",
                            "--version"), output);
                        break;
                    case "verify":
                        Verify(ParseOptions(
                            args,
                            "--manifest",
                            "--public-key"), output);
                        break;
                    case "verify-installer":
                        VerifyInstaller(ParseOptionsWithOptional(
                            args,
                            "--held-read-lock",
                            Array.Empty<string>(),
                            "--manifest",
                            "--public-key",
                            "--installer"), output);
                        break;
                    default:
                        throw new CommandException(Usage);
                }

                return 0;
            }
            catch (CommandException ex)
            {
                error.WriteLine(ex.Message);
                return 1;
            }
            catch (IOException)
            {
                error.WriteLine("The requested input or output file could not be accessed.");
                return 1;
            }
            catch (UnauthorizedAccessException)
            {
                error.WriteLine("The requested input or output file could not be accessed.");
                return 1;
            }
            catch (JsonException)
            {
                error.WriteLine("The update manifest is not valid JSON.");
                return 1;
            }
            catch (CryptographicException)
            {
                error.WriteLine("The signing operation could not be completed.");
                return 1;
            }
            catch (Exception)
            {
                error.WriteLine("The signing command failed.");
                return 1;
            }
        }

        private static void ExportPublicKey(Dictionary<string, string> options, TextWriter output)
        {
            using X509Certificate2 certificate = LoadSigningCertificate(options);
            using RSA privateKey = GetRsaPrivateKey(certificate);
            output.WriteLine(Convert.ToBase64String(privateKey.ExportSubjectPublicKeyInfo()));
        }

        private static void Sign(Dictionary<string, string> options, TextWriter output)
        {
            string version = RequireValue(options, "--version");
            if (!UpdateManifestSignature.IsValidManifestVersion(version))
                throw new CommandException("The update manifest fields are invalid.");

            string installerPath = Path.GetFullPath(RequireValue(options, "--installer"));
            string certificatePath = Path.GetFullPath(RequireValue(options, "--pfx"));
            string outputPath = Path.GetFullPath(RequireValue(options, "--output"));
            if (PathsIdentifySameFile(outputPath, installerPath) ||
                PathsIdentifySameFile(outputPath, certificatePath))
                throw new CommandException("The manifest output must be different from the installer and signing certificate.");

            options["--installer"] = installerPath;
            options["--pfx"] = certificatePath;
            options["--output"] = outputPath;
            FileInfo installer = new FileInfo(installerPath);
            if (!installer.Exists)
                throw new CommandException("The installer file could not be found.");
            if (installer.Length <= 0 || installer.Length > MaximumInstallerBytes)
                throw new CommandException("The installer size is outside the supported range.");

            string hash;
            FileShare installerShare = UsesPublisherHeldReadLock(options)
                ? FileShare.ReadWrite
                : FileShare.Read;
            using (FileStream input = new FileStream(
                installer.FullName, FileMode.Open, FileAccess.Read, installerShare))
                hash = Convert.ToHexStringLower(SHA256.HashData(input));
            string notes = options.TryGetValue("--notes", out string parsedNotes)
                ? parsedNotes
                : string.Empty;

            UpdateManifest manifest = new UpdateManifest
            {
                SchemaVersion = 1,
                Version = version,
                InstallerUrl = RequireValue(options, "--installer-url"),
                Sha256 = hash,
                SizeBytes = installer.Length,
                PublishedUtc = DateTimeOffset.UtcNow,
                Notes = notes
            };

            using X509Certificate2 certificate = LoadSigningCertificate(options);
            using RSA privateKey = GetRsaPrivateKey(certificate);
            try
            {
                UpdateManifestSignature.Sign(manifest, privateKey);
            }
            catch (InvalidDataException)
            {
                throw new CommandException("The update manifest fields are invalid.");
            }

            WriteManifest(outputPath, manifest);
            output.WriteLine("Signed update manifest.");
        }

        private static void ValidatePublicKey(Dictionary<string, string> options, TextWriter output)
        {
            if (!UpdateManifestSignature.IsValidPublicKey(RequireValue(options, "--public-key")))
                throw new CommandException("The update public key is not a canonical RSA SubjectPublicKeyInfo of at least 2048 bits.");
            output.WriteLine("Update public key verified.");
        }

        private static void ValidateInstallerMetadata(
            Dictionary<string, string> options,
            TextWriter output)
        {
            string installerPath = Path.GetFullPath(RequireValue(options, "--installer"));
            FileInfo installer = new FileInfo(installerPath);
            if (!installer.Exists ||
                !string.Equals(installer.Extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
                installer.Length <= 0 ||
                installer.Length > MaximumInstallerBytes)
                throw new CommandException("The release installer is not a supported EXE file.");

            string version = RequireValue(options, "--version");
            if (version.Length > UpdateManifestSignature.MaximumVersionCharacters ||
                !SemanticVersion.TryParse(version, out SemanticVersion semanticVersion))
                throw new CommandException("The requested installer version is not a valid Semantic Version.");
            if (!semanticVersion.TryGetFileVersion(out int major, out int minor, out int patch))
                throw new CommandException("The requested installer version cannot be represented by Windows file-version metadata.");

            FileVersionInfo metadata = FileVersionInfo.GetVersionInfo(installerPath);
            if (!string.Equals(metadata.ProductName, "Cloudflare R2 Uploader", StringComparison.Ordinal) ||
                !string.Equals(metadata.FileDescription, "Cloudflare R2 Uploader Setup", StringComparison.Ordinal) ||
                metadata.FileMajorPart != major ||
                metadata.FileMinorPart != minor ||
                metadata.FileBuildPart != patch ||
                metadata.FilePrivatePart != 0)
                throw new CommandException("The release installer does not contain the expected product and version metadata.");

            output.WriteLine("Release installer metadata verified.");
        }

        private static void Verify(Dictionary<string, string> options, TextWriter output)
        {
            string manifestPath = RequireValue(options, "--manifest");
            byte[] json = ReadBoundedFile(manifestPath, MaximumManifestBytes);
            UpdateManifest manifest = UpdateManifestJson.DeserializeManifest(json);
            if (!UpdateManifestSignature.Verify(manifest, RequireValue(options, "--public-key")))
                throw new CommandException("The update manifest signature verification failed.");
            output.WriteLine("Update manifest signature verified.");
        }

        private static void VerifyInstaller(Dictionary<string, string> options, TextWriter output)
        {
            byte[] json = ReadBoundedFile(RequireValue(options, "--manifest"), MaximumManifestBytes);
            UpdateManifest manifest = UpdateManifestJson.DeserializeManifest(json);
            if (!UpdateManifestSignature.Verify(manifest, RequireValue(options, "--public-key")))
                throw new CommandException("The update manifest signature verification failed.");

            string installerPath = RequireValue(options, "--installer");
            FileInfo installer = new FileInfo(installerPath);
            if (!installer.Exists || installer.Length != manifest.SizeBytes)
                throw new CommandException("The published installer does not match the signed manifest.");

            byte[] actualHash;
            FileShare installerShare = UsesPublisherHeldReadLock(options)
                ? FileShare.ReadWrite
                : FileShare.Read;
            using (FileStream input = new FileStream(
                installer.FullName, FileMode.Open, FileAccess.Read, installerShare))
                actualHash = SHA256.HashData(input);
            byte[] expectedHash = Convert.FromHexString(manifest.Sha256);
            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                throw new CommandException("The published installer does not match the signed manifest.");
            output.WriteLine("Published installer verified against the signed manifest.");
        }

        private static X509Certificate2 LoadSigningCertificate(Dictionary<string, string> options)
        {
            string environmentName = RequireValue(options, "--password-env");
            string password = Environment.GetEnvironmentVariable(environmentName);
            if (string.IsNullOrEmpty(password))
                throw new CommandException("The named password environment variable is missing or empty.");

            try
            {
                return X509CertificateLoader.LoadPkcs12FromFile(
                    RequireValue(options, "--pfx"),
                    password,
                    X509KeyStorageFlags.EphemeralKeySet);
            }
            catch (CryptographicException)
            {
                throw new CommandException("The password-protected signing certificate could not be loaded.");
            }
        }

        private static RSA GetRsaPrivateKey(X509Certificate2 certificate)
        {
            RSA privateKey = certificate.GetRSAPrivateKey();
            if (privateKey == null || !certificate.HasPrivateKey)
            {
                privateKey?.Dispose();
                throw new CommandException("The signing certificate must contain an RSA private key.");
            }
            if (privateKey.KeySize < 2048)
            {
                privateKey.Dispose();
                throw new CommandException("The signing certificate RSA private key must be at least 2048 bits.");
            }
            return privateKey;
        }

        private static Dictionary<string, string> ParseOptions(
            string[] args,
            params string[] expectedOptions)
        {
            HashSet<string> expected = new HashSet<string>(expectedOptions, StringComparer.Ordinal);
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !expected.Contains(args[index]) || values.ContainsKey(args[index]))
                    throw new CommandException(Usage);
                values.Add(args[index], args[index + 1]);
            }

            if (values.Count != expected.Count)
                throw new CommandException(Usage);
            return values;
        }

        private static Dictionary<string, string> ParseOptionsWithOptional(
            string[] args,
            string optionalOption,
            string[] optionalValueOptions,
            params string[] requiredOptions)
        {
            HashSet<string> allowed = new HashSet<string>(requiredOptions, StringComparer.Ordinal)
            {
                optionalOption
            };
            allowed.UnionWith(optionalValueOptions);
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !allowed.Contains(args[index]) || values.ContainsKey(args[index]))
                    throw new CommandException(Usage);
                values.Add(args[index], args[index + 1]);
            }

            foreach (string required in requiredOptions)
            {
                if (!values.ContainsKey(required)) throw new CommandException(Usage);
            }
            if (values.TryGetValue(optionalOption, out string optionalValue) &&
                !string.Equals(optionalValue, "true", StringComparison.Ordinal))
                throw new CommandException(Usage);
            return values;
        }

        private static bool UsesPublisherHeldReadLock(Dictionary<string, string> options)
        {
            return options.TryGetValue("--held-read-lock", out string value) &&
                string.Equals(value, "true", StringComparison.Ordinal);
        }

        private static string RequireValue(Dictionary<string, string> options, string name)
        {
            string value = options[name];
            if (string.IsNullOrWhiteSpace(value))
                throw new CommandException("A required command value is missing.");
            return value;
        }

        private static byte[] ReadBoundedFile(string path, int maximumBytes)
        {
            using FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length <= 0 || input.Length > maximumBytes)
                throw new CommandException("The update manifest size is outside the supported range.");

            using MemoryStream output = new MemoryStream((int)input.Length);
            byte[] buffer = new byte[4096];
            int total = 0;
            while (true)
            {
                int read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total += read;
                if (total > maximumBytes)
                    throw new CommandException("The update manifest is too large.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }

        private static void WriteManifest(string path, UpdateManifest manifest)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(manifest, WriteJsonOptions), Utf8WithoutBom);
                File.Move(temporaryPath, fullPath, true);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private static bool PathsIdentifySameFile(string first, string second)
        {
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
                comparison);
        }

        private const string Usage =
            "Usage:\n" +
            "  export-public-key --pfx PATH --password-env NAME\n" +
            "  sign --pfx PATH --password-env NAME --version VERSION --installer PATH " +
            "--installer-url HTTPS_URL [--notes TEXT] --output PATH [--held-read-lock true]\n" +
            "  validate-public-key --public-key BASE64_SUBJECT_PUBLIC_KEY_INFO\n" +
            "  validate-installer-metadata --installer PATH --version VERSION\n" +
            "  verify --manifest PATH --public-key BASE64_SUBJECT_PUBLIC_KEY_INFO\n" +
            "  verify-installer --manifest PATH --public-key BASE64_SUBJECT_PUBLIC_KEY_INFO --installer PATH " +
            "[--held-read-lock true]";

        private sealed class CommandException : Exception
        {
            public CommandException(string message)
                : base(message)
            {
            }
        }
    }
}
