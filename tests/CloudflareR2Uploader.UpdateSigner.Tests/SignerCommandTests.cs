using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.UpdateSigner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class SignerCommandTests
    {
        [TestMethod]
        public void ExportPublicKey_RejectsMissingPasswordEnvironmentVariableWithoutLeakingSecrets()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out _);
            string variable = "UPDATE_SIGNER_MISSING_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, null);

            CommandResult result = Run(
                "export-public-key", "--pfx", pfx, "--password-env", variable);

            Assert.AreNotEqual(0, result.ExitCode);
            StringAssert.Contains(result.Error, "password environment variable");
            Assert.IsFalse(result.Output.Contains(password, StringComparison.Ordinal));
            Assert.IsFalse(result.Error.Contains(password, StringComparison.Ordinal));
        }

        [TestMethod]
        public void ExportPublicKey_RejectsWrongPasswordWithSafeDiagnostics()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string wrongPassword = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out _);
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, wrongPassword);

            CommandResult result = Run(
                "export-public-key", "--pfx", pfx, "--password-env", variable);

            Assert.AreNotEqual(0, result.ExitCode);
            Assert.IsFalse(result.Output.Contains(password, StringComparison.Ordinal));
            Assert.IsFalse(result.Error.Contains(password, StringComparison.Ordinal));
            Assert.IsFalse(result.Output.Contains(wrongPassword, StringComparison.Ordinal));
            Assert.IsFalse(result.Error.Contains(wrongPassword, StringComparison.Ordinal));
        }

        [TestMethod]
        public void ExportPublicKey_RejectsNonRsaCertificateAndCertificateWithoutPrivateKey()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, password);

            CommandResult nonRsa = Run(
                "export-public-key", "--pfx", CreateEcdsaPfx(temporary, password), "--password-env", variable);
            CommandResult publicOnly = Run(
                "export-public-key", "--pfx", CreatePublicOnlyRsaPfx(temporary, password), "--password-env", variable);

            Assert.AreNotEqual(0, nonRsa.ExitCode);
            Assert.AreNotEqual(0, publicOnly.ExitCode);
            StringAssert.Contains(nonRsa.Error, "RSA private key");
            StringAssert.Contains(publicOnly.Error, "RSA private key");
            Assert.IsFalse(nonRsa.Error.Contains(password, StringComparison.Ordinal));
            Assert.IsFalse(publicOnly.Error.Contains(password, StringComparison.Ordinal));
        }

        [TestMethod]
        public void ExportPublicKey_ReturnsBase64SubjectPublicKeyInfoOnly()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out string expectedPublicKey);
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, password);

            CommandResult result = Run(
                "export-public-key", "--pfx", pfx, "--password-env", variable);

            Assert.AreEqual(0, result.ExitCode, result.Error);
            Assert.AreEqual(expectedPublicKey, result.Output.Trim());
            Assert.AreEqual(string.Empty, result.Error);
            Assert.IsFalse(result.Output.Contains(password, StringComparison.Ordinal));
        }

        [TestMethod]
        public void ValidatePublicKey_UsesTheSharedCoreValidator()
        {
            using RSA valid = RSA.Create(2048);
            using RSA weak = RSA.Create(1024);
            using ECDsa ellipticCurve = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] validSpki = valid.ExportSubjectPublicKeyInfo();

            Assert.AreEqual(0, Run(
                "validate-public-key", "--public-key", Convert.ToBase64String(validSpki)).ExitCode);
            Assert.AreNotEqual(0, Run("validate-public-key", "--public-key", "not base64").ExitCode);
            Assert.AreNotEqual(0, Run(
                "validate-public-key", "--public-key", Convert.ToBase64String(new byte[] { 0x30, 0x00 })).ExitCode);
            Assert.AreNotEqual(0, Run(
                "validate-public-key", "--public-key", Convert.ToBase64String(ellipticCurve.ExportSubjectPublicKeyInfo())).ExitCode);
            Assert.AreNotEqual(0, Run(
                "validate-public-key", "--public-key", Convert.ToBase64String(weak.ExportSubjectPublicKeyInfo())).ExitCode);
            Assert.AreNotEqual(0, Run(
                "validate-public-key", "--public-key",
                Convert.ToBase64String(validSpki.Concat(new byte[] { 0 }).ToArray())).ExitCode);
        }

        [TestMethod]
        public void Sign_WritesNoBomSystemTextJsonManifestThatCoreVerifierAccepts()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out string publicKey);
            string installer = temporary.File("Setup.exe");
            string manifestPath = temporary.File("manifest.json");
            File.WriteAllBytes(installer, Encoding.UTF8.GetBytes("synthetic installer"));
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, password);

            CommandResult result = Run(
                "sign",
                "--pfx", pfx,
                "--password-env", variable,
                "--version", "2.0.0",
                "--installer", installer,
                "--installer-url", "https://updates.example.test/releases/2.0.0/Setup.exe",
                "--notes", "Zażółć — 安全 ✅",
                "--output", manifestPath);

            Assert.AreEqual(0, result.ExitCode, result.Error);
            byte[] json = File.ReadAllBytes(manifestPath);
            Assert.IsFalse(json.Length >= 3 && json[0] == 0xef && json[1] == 0xbb && json[2] == 0xbf);
            UpdateManifest manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
            Assert.IsNotNull(manifest);
            Assert.AreEqual("RSA-PSS-SHA256", manifest.SignatureAlgorithm);
            Assert.AreEqual("Zażółć — 安全 ✅", manifest.Notes);
            Assert.IsTrue(UpdateManifestSignature.Verify(manifest, publicKey));
            Assert.IsFalse(result.Output.Contains(password, StringComparison.Ordinal));
            Assert.IsFalse(result.Error.Contains(password, StringComparison.Ordinal));
        }

        [TestMethod]
        public void Sign_RejectsOutputThatIdentifiesInstallerOrCertificateWithoutChangingEither()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out _);
            string installer = temporary.File("Setup.exe");
            File.WriteAllBytes(installer, Encoding.UTF8.GetBytes("immutable installer"));
            byte[] originalInstaller = File.ReadAllBytes(installer);
            byte[] originalPfx = File.ReadAllBytes(pfx);
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, password);

            CommandResult installerOutput = Sign(pfx, variable, installer, installer);
            string relativeInstaller = Path.GetRelativePath(Environment.CurrentDirectory, installer);
            CommandResult relativeInstallerOutput = Sign(pfx, variable, installer, relativeInstaller);
            CommandResult certificateOutput = Sign(pfx, variable, installer, pfx);

            Assert.AreNotEqual(0, installerOutput.ExitCode);
            Assert.AreNotEqual(0, relativeInstallerOutput.ExitCode);
            Assert.AreNotEqual(0, certificateOutput.ExitCode);
            CollectionAssert.AreEqual(originalInstaller, File.ReadAllBytes(installer));
            CollectionAssert.AreEqual(originalPfx, File.ReadAllBytes(pfx));
        }

        [TestMethod]
        public void Sign_UsesOperatingSystemCaseSemanticsForOutputIdentity()
        {
            if (!OperatingSystem.IsWindows()) return;

            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out _);
            string installer = temporary.File("CaseSensitiveName.exe");
            File.WriteAllBytes(installer, Encoding.UTF8.GetBytes("immutable installer"));
            byte[] originalInstaller = File.ReadAllBytes(installer);
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, password);
            string caseVariant = installer.Replace("CaseSensitiveName.exe", "casesensitivename.EXE", StringComparison.Ordinal);

            CommandResult result = Sign(pfx, variable, installer, caseVariant);

            Assert.AreNotEqual(0, result.ExitCode);
            CollectionAssert.AreEqual(originalInstaller, File.ReadAllBytes(installer));
        }

        [TestMethod]
        public void Verify_AcceptsPublishedManifestAndRejectsTampering()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out string publicKey);
            string installer = temporary.File("Setup.exe");
            string manifestPath = temporary.File("manifest.json");
            File.WriteAllBytes(installer, Encoding.UTF8.GetBytes("synthetic installer"));
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, password);
            CommandResult signed = Run(
                "sign", "--pfx", pfx, "--password-env", variable,
                "--version", "2.0.0", "--installer", installer,
                "--installer-url", "https://updates.example.test/releases/2.0.0/Setup.exe",
                "--notes", string.Empty, "--output", manifestPath);
            Assert.AreEqual(0, signed.ExitCode, signed.Error);

            CommandResult verified = Run(
                "verify", "--manifest", manifestPath, "--public-key", publicKey);
            Assert.AreEqual(0, verified.ExitCode, verified.Error);

            UpdateManifest manifest = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllBytes(manifestPath));
            manifest.Notes = "tampered";
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest), new UTF8Encoding(false));
            CommandResult tampered = Run(
                "verify", "--manifest", manifestPath, "--public-key", publicKey);

            Assert.AreNotEqual(0, tampered.ExitCode);
            StringAssert.Contains(tampered.Error, "verification failed");
            Assert.IsFalse(tampered.Error.Contains(password, StringComparison.Ordinal));
        }

        [TestMethod]
        public void VerifyInstaller_RequiresSignedManifestSizeAndSha256ToMatchFile()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out string publicKey);
            string installer = temporary.File("Setup.exe");
            string downloaded = temporary.File("downloaded.exe");
            string manifestPath = temporary.File("manifest.json");
            File.WriteAllBytes(installer, Encoding.UTF8.GetBytes("synthetic installer"));
            File.Copy(installer, downloaded);
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, password);
            Assert.AreEqual(0, Sign(pfx, variable, installer, manifestPath).ExitCode);

            CommandResult accepted = Run(
                "verify-installer", "--manifest", manifestPath,
                "--public-key", publicKey, "--installer", downloaded);
            File.WriteAllBytes(downloaded, Encoding.UTF8.GetBytes("different installer"));
            CommandResult rejected = Run(
                "verify-installer", "--manifest", manifestPath,
                "--public-key", publicKey, "--installer", downloaded);

            Assert.AreEqual(0, accepted.ExitCode, accepted.Error);
            Assert.AreNotEqual(0, rejected.ExitCode);
        }

        [TestMethod]
        public void ValidateInstallerMetadata_RequiresARealExpectedProductPeAtTheRequestedNumericVersion()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string fixtureVersion = FixtureSemanticVersion();
            string wrongFixtureVersion = DifferentSemanticVersion(fixtureVersion);
            string genuineInstaller = temporary.File("CloudflareR2Uploader-v" + fixtureVersion + "-Setup.exe");
            File.Copy(typeof(SignerCommandTests).Assembly.Location, genuineInstaller);

            string minimalStub = temporary.File("stub.exe");
            byte[] stubBytes = new byte[512];
            stubBytes[0] = 0x4d;
            stubBytes[1] = 0x5a;
            BitConverter.GetBytes((uint)0x80).CopyTo(stubBytes, 0x3c);
            stubBytes[0x80] = 0x50;
            stubBytes[0x81] = 0x45;
            File.WriteAllBytes(minimalStub, stubBytes);

            string wrongProduct = temporary.File("wrong-product.exe");
            File.Copy(typeof(SignerCommand).Assembly.Location, wrongProduct);
            string malformed = temporary.File("malformed.exe");
            File.WriteAllText(malformed, "not a portable executable", Encoding.UTF8);

            FileVersionInfo fixtureMetadata = FileVersionInfo.GetVersionInfo(genuineInstaller);
            Assert.AreEqual(
                "Cloudflare R2 Uploader",
                fixtureMetadata.ProductName);
            Assert.AreEqual(
                "Cloudflare R2 Uploader Setup",
                fixtureMetadata.FileDescription);
            Assert.AreEqual(0, fixtureMetadata.FilePrivatePart);
            Assert.AreEqual(
                fixtureVersion + ".0",
                fixtureMetadata.FileVersion);

            CommandResult accepted = Run(
                "validate-installer-metadata", "--installer", genuineInstaller, "--version", fixtureVersion);
            CommandResult wrongVersion = Run(
                "validate-installer-metadata", "--installer", genuineInstaller, "--version", wrongFixtureVersion);
            CommandResult stub = Run(
                "validate-installer-metadata", "--installer", minimalStub, "--version", fixtureVersion);
            CommandResult product = Run(
                "validate-installer-metadata", "--installer", wrongProduct, "--version", fixtureVersion);
            CommandResult nonPe = Run(
                "validate-installer-metadata", "--installer", malformed, "--version", fixtureVersion);

            Assert.AreEqual(0, accepted.ExitCode, accepted.Error);
            Assert.AreNotEqual(0, wrongVersion.ExitCode);
            Assert.AreNotEqual(0, stub.ExitCode);
            Assert.AreNotEqual(0, product.ExitCode);
            Assert.AreNotEqual(0, nonPe.ExitCode);
        }

        [TestMethod]
        public void ValidateInstallerMetadata_ValidatesTheCompleteSemVerBeforeMappingFileVersion()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string installer = temporary.File("genuine-fixture.exe");
            File.Copy(typeof(SignerCommandTests).Assembly.Location, installer);
            string fixtureVersion = FixtureSemanticVersion();

            foreach (string valid in new[]
            {
                fixtureVersion,
                fixtureVersion + "-rc.1",
                fixtureVersion + "-rc.1+build.0007",
                fixtureVersion + "+build.0007"
            })
            {
                CommandResult result = Run(
                    "validate-installer-metadata", "--installer", installer, "--version", valid);
                Assert.AreEqual(0, result.ExitCode, valid + ": " + result.Error);
            }

            foreach (string invalid in new[]
            {
                fixtureVersion + "-",
                fixtureVersion + "-01",
                fixtureVersion + "-alpha..1",
                fixtureVersion + "-alpha_1",
                fixtureVersion + "+",
                fixtureVersion + "+build..1",
                fixtureVersion + "+build_1",
                " " + fixtureVersion,
                fixtureVersion + " ",
                fixtureVersion + "\n",
                fixtureVersion + "\r\n"
            })
            {
                CommandResult result = Run(
                    "validate-installer-metadata", "--installer", installer, "--version", invalid);
                Assert.AreNotEqual(0, result.ExitCode, invalid);
                StringAssert.Contains(result.Error, "not a valid Semantic Version", invalid);
            }

            CommandResult rangeOverflow = Run(
                "validate-installer-metadata", "--installer", installer, "--version", "65536.0.0");
            Assert.AreNotEqual(0, rangeOverflow.ExitCode);
            StringAssert.Contains(
                rangeOverflow.Error,
                "cannot be represented by Windows file-version metadata");

            CommandResult maximumRange = Run(
                "validate-installer-metadata", "--installer", installer, "--version", "65535.65535.65535");
            Assert.AreNotEqual(0, maximumRange.ExitCode);
            StringAssert.Contains(
                maximumRange.Error,
                "does not contain the expected product and version metadata");
        }

        [TestMethod]
        public void SigningAndInstallerMetadataShareThe128CharacterManifestVersionLimit()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string fixtureVersion = FixtureSemanticVersion();
            string maximumVersion = VersionWithLength(fixtureVersion, 128);
            string oversizedVersion = VersionWithLength(fixtureVersion, 129);
            string installer = temporary.File("genuine-fixture.exe");
            File.Copy(typeof(SignerCommandTests).Assembly.Location, installer);
            string password = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out string publicKey);
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, password);
            string acceptedManifestPath = temporary.File("accepted-manifest.json");
            string rejectedManifestPath = temporary.File("rejected-manifest.json");

            CommandResult acceptedMetadata = Run(
                "validate-installer-metadata", "--installer", installer, "--version", maximumVersion);
            CommandResult rejectedMetadata = Run(
                "validate-installer-metadata", "--installer", installer, "--version", oversizedVersion);
            CommandResult acceptedSign = Run(
                "sign", "--pfx", pfx, "--password-env", variable,
                "--version", maximumVersion, "--installer", installer,
                "--installer-url", "https://updates.example.test/releases/maximum/Setup.exe",
                "--notes", string.Empty, "--output", acceptedManifestPath);
            CommandResult rejectedSign = Run(
                "sign", "--pfx", pfx, "--password-env", variable,
                "--version", oversizedVersion, "--installer", installer,
                "--installer-url", "https://updates.example.test/releases/oversized/Setup.exe",
                "--notes", string.Empty, "--output", rejectedManifestPath);

            Assert.AreEqual(0, acceptedMetadata.ExitCode, acceptedMetadata.Error);
            Assert.AreNotEqual(0, rejectedMetadata.ExitCode);
            StringAssert.Contains(rejectedMetadata.Error, "not a valid Semantic Version");
            Assert.AreEqual(0, acceptedSign.ExitCode, acceptedSign.Error);
            UpdateManifest acceptedManifest = UpdateManifestJson.DeserializeManifest(
                File.ReadAllBytes(acceptedManifestPath));
            Assert.AreEqual(maximumVersion, acceptedManifest.Version);
            Assert.IsTrue(UpdateManifestSignature.Verify(acceptedManifest, publicKey));
            Assert.AreNotEqual(0, rejectedSign.ExitCode);
            Assert.IsFalse(File.Exists(rejectedManifestPath));
        }

        [TestMethod]
        public void Sign_EnforcesTheEightThousandUnicodeScalarReleaseNotesLimit()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out string publicKey);
            string installer = temporary.File("Setup.exe");
            File.WriteAllBytes(installer, Encoding.UTF8.GetBytes("synthetic installer"));
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, password);
            string astral = char.ConvertFromUtf32(0x1f680);
            string maximum = string.Concat(Enumerable.Repeat(astral, 8000));
            string acceptedPath = temporary.File("accepted.json");
            string oversizedPath = temporary.File("oversized.json");
            string malformedPath = temporary.File("malformed.json");

            CommandResult accepted = Run(
                "sign", "--pfx", pfx, "--password-env", variable,
                "--version", "2.0.0", "--installer", installer,
                "--installer-url", "https://updates.example.test/releases/2.0.0/Setup.exe",
                "--notes", maximum, "--output", acceptedPath);
            CommandResult oversized = Run(
                "sign", "--pfx", pfx, "--password-env", variable,
                "--version", "2.0.0", "--installer", installer,
                "--installer-url", "https://updates.example.test/releases/2.0.0/Setup.exe",
                "--notes", maximum + astral, "--output", oversizedPath);
            CommandResult malformed = Run(
                "sign", "--pfx", pfx, "--password-env", variable,
                "--version", "2.0.0", "--installer", installer,
                "--installer-url", "https://updates.example.test/releases/2.0.0/Setup.exe",
                "--notes", "release\ud800notes", "--output", malformedPath);

            Assert.AreEqual(0, accepted.ExitCode, accepted.Error);
            UpdateManifest manifest = UpdateManifestJson.DeserializeManifest(File.ReadAllBytes(acceptedPath));
            Assert.AreEqual(16000, manifest.Notes.Length);
            Assert.IsTrue(UpdateManifestSignature.Verify(manifest, publicKey));
            Assert.AreNotEqual(0, oversized.ExitCode);
            Assert.AreNotEqual(0, malformed.ExitCode);
            Assert.IsFalse(File.Exists(oversizedPath));
            Assert.IsFalse(File.Exists(malformedPath));
        }

        [TestMethod]
        public void Signer_RequiresExplicitHeldLockModeToReadAPublisherOwnedSnapshot()
        {
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string password = NewPassword();
            string pfx = CreateRsaPfx(temporary, password, out string publicKey);
            string installer = temporary.File("held-installer.exe");
            string manifest = temporary.File("held-manifest.json");
            File.WriteAllBytes(installer, Encoding.UTF8.GetBytes("publisher-held installer"));
            byte[] original = File.ReadAllBytes(installer);
            string variable = "UPDATE_SIGNER_PASSWORD_" + Guid.NewGuid().ToString("N");
            using EnvironmentVariableScope environment = new EnvironmentVariableScope(variable, password);
            using FileStream owner = new FileStream(
                installer, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

            CommandResult ordinary = Sign(pfx, variable, installer, manifest);
            CommandResult held = Run(
                "sign", "--pfx", pfx, "--password-env", variable,
                "--version", "2.0.0", "--installer", installer,
                "--installer-url", "https://updates.example.test/releases/2.0.0/held-installer.exe",
                "--notes", string.Empty, "--output", manifest,
                "--held-read-lock", "true");
            CommandResult verified = Run(
                "verify-installer", "--manifest", manifest, "--public-key", publicKey,
                "--installer", installer, "--held-read-lock", "true");

            if (OperatingSystem.IsWindows()) Assert.AreNotEqual(0, ordinary.ExitCode);
            Assert.AreEqual(0, held.ExitCode, held.Error);
            Assert.AreEqual(0, verified.ExitCode, verified.Error);
            byte[] after = new byte[checked((int)owner.Length)];
            owner.Position = 0;
            owner.ReadExactly(after);
            CollectionAssert.AreEqual(original, after);
        }

        private static CommandResult Run(params string[] arguments)
        {
            using StringWriter output = new StringWriter();
            using StringWriter error = new StringWriter();
            int exitCode = SignerCommand.Run(arguments, output, error);
            return new CommandResult(exitCode, output.ToString(), error.ToString());
        }

        private static CommandResult Sign(
            string pfx,
            string passwordVariable,
            string installer,
            string output)
        {
            return Run(
                "sign",
                "--pfx", pfx,
                "--password-env", passwordVariable,
                "--version", "2.0.0",
                "--installer", installer,
                "--installer-url", "https://updates.example.test/releases/2.0.0/Setup.exe",
                "--notes", string.Empty,
                "--output", output);
        }

        private static string NewPassword()
        {
            return "ephemeral-" + Guid.NewGuid().ToString("N");
        }

        private static string FixtureSemanticVersion()
        {
            FileVersionInfo metadata = FileVersionInfo.GetVersionInfo(typeof(SignerCommandTests).Assembly.Location);
            Assert.AreEqual(0, metadata.FilePrivatePart, "The genuine PE fixture must use a three-part file version.");
            Assert.IsTrue(metadata.FileMajorPart >= 0 && metadata.FileMajorPart <= ushort.MaxValue);
            Assert.IsTrue(metadata.FileMinorPart >= 0 && metadata.FileMinorPart <= ushort.MaxValue);
            Assert.IsTrue(metadata.FileBuildPart >= 0 && metadata.FileBuildPart <= ushort.MaxValue);
            return string.Join(
                ".",
                metadata.FileMajorPart,
                metadata.FileMinorPart,
                metadata.FileBuildPart);
        }

        private static string DifferentSemanticVersion(string fixtureVersion)
        {
            string[] components = fixtureVersion.Split('.');
            int patch = int.Parse(components[2], System.Globalization.CultureInfo.InvariantCulture);
            if (patch < ushort.MaxValue)
                return components[0] + "." + components[1] + "." + (patch + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

            int minor = int.Parse(components[1], System.Globalization.CultureInfo.InvariantCulture);
            if (minor < ushort.MaxValue)
                return components[0] + "." + (minor + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + ".0";

            int major = int.Parse(components[0], System.Globalization.CultureInfo.InvariantCulture);
            return major > 0 ? "0.0.0" : "1.0.0";
        }

        private static string VersionWithLength(string numericVersion, int length)
        {
            int prereleaseCharacters = length - numericVersion.Length - 1;
            Assert.IsTrue(prereleaseCharacters > 0);
            string version = numericVersion + "-" + new string('a', prereleaseCharacters);
            Assert.AreEqual(length, version.Length);
            return version;
        }

        private static string CreateRsaPfx(
            TemporaryDirectory temporary,
            string password,
            out string publicKey)
        {
            using RSA rsa = RSA.Create(2048);
            CertificateRequest request = new CertificateRequest(
                "CN=CloudflareR2Uploader Update Signer Test",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(1));
            publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            string path = temporary.File("rsa-" + Guid.NewGuid().ToString("N") + ".pfx");
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
            return path;
        }

        private static string CreateEcdsaPfx(TemporaryDirectory temporary, string password)
        {
            using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            CertificateRequest request = new CertificateRequest(
                "CN=CloudflareR2Uploader ECDSA Test",
                ecdsa,
                HashAlgorithmName.SHA256);
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(1));
            string path = temporary.File("ecdsa.pfx");
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
            return path;
        }

        private static string CreatePublicOnlyRsaPfx(TemporaryDirectory temporary, string password)
        {
            string privatePfx = CreateRsaPfx(temporary, password, out _);
            using X509Certificate2 withPrivateKey = X509CertificateLoader.LoadPkcs12FromFile(
                privatePfx,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
            using X509Certificate2 publicOnly = X509CertificateLoader.LoadCertificate(
                withPrivateKey.Export(X509ContentType.Cert));
            string path = temporary.File("public-only.pfx");
            File.WriteAllBytes(path, publicOnly.Export(X509ContentType.Pkcs12, password));
            return path;
        }

        private sealed class EnvironmentVariableScope : IDisposable
        {
            private readonly string _name;
            private readonly string _original;

            public EnvironmentVariableScope(string name, string value)
            {
                _name = name;
                _original = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable(_name, _original);
            }
        }

        private sealed record CommandResult(int ExitCode, string Output, string Error);
    }
}
