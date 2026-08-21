using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class UpdateManifestSignatureTests
    {
        private const string Magic = "CloudflareR2Uploader.UpdateManifest.v1";
        private static readonly string[] ManifestPropertyNames =
        {
            "schemaVersion", "version", "installerUrl", "sha256", "sizeBytes", "publishedUtc",
            "notes", "signatureAlgorithm", "signature"
        };
        private static readonly string[] SourcePropertyNames = { "manifestUrl", "manifestPublicKey" };

        [TestMethod]
        public void Sign_ProducesSchema1RsaPssSignatureThatTrustedKeyVerifies()
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest manifest = Manifest("2.0.0");

            UpdateManifestSignature.Sign(manifest, trusted);

            Assert.AreEqual("RSA-PSS-SHA256", manifest.SignatureAlgorithm);
            Assert.IsFalse(string.IsNullOrWhiteSpace(manifest.Signature));
            Assert.IsTrue(UpdateManifestSignature.Verify(manifest, PublicKey(trusted)));
        }

        [TestMethod]
        public void IsValidPublicKey_UsesTheExactRuntimeTrustContract()
        {
            using RSA valid = RSA.Create(2048);
            using RSA weak = RSA.Create(1024);
            using ECDsa ellipticCurve = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] validSpki = valid.ExportSubjectPublicKeyInfo();

            Assert.IsTrue(UpdateManifestSignature.IsValidPublicKey(Convert.ToBase64String(validSpki)));
            Assert.IsFalse(UpdateManifestSignature.IsValidPublicKey(string.Empty));
            Assert.IsFalse(UpdateManifestSignature.IsValidPublicKey("not base64"));
            Assert.IsFalse(UpdateManifestSignature.IsValidPublicKey(Convert.ToBase64String(new byte[] { 0x30, 0x00 })));
            Assert.IsFalse(UpdateManifestSignature.IsValidPublicKey(
                Convert.ToBase64String(ellipticCurve.ExportSubjectPublicKeyInfo())));
            Assert.IsFalse(UpdateManifestSignature.IsValidPublicKey(
                Convert.ToBase64String(weak.ExportSubjectPublicKeyInfo())));
            Assert.IsFalse(UpdateManifestSignature.IsValidPublicKey(
                Convert.ToBase64String(validSpki.Concat(new byte[] { 0 }).ToArray())));
        }

        [TestMethod]
        public void CreatePayload_UsesTheStableSchema1ByteContract()
        {
            UpdateManifest manifest = Manifest("2.0.0");
            manifest.InstallerUrl = "https://updates.example.test/releases/2.0.0/Setup.exe?arch=x64";
            manifest.Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            manifest.SizeBytes = 123456789;
            manifest.PublishedUtc = new DateTimeOffset(2026, 8, 17, 10, 11, 12, TimeSpan.FromHours(2));
            manifest.Notes = "Zażółć gęślą jaźń — 安全 ✅";

            byte[] payload = UpdateManifestSignature.CreatePayload(manifest);

            CollectionAssert.AreEqual(ExpectedPayload(manifest), payload);
        }

        [TestMethod]
        public void CreatePayload_NormalizesEquivalentTimestampOffsetsToUtcTicks()
        {
            UpdateManifest utc = Manifest("2.0.0");
            utc.PublishedUtc = new DateTimeOffset(2026, 8, 17, 8, 11, 12, TimeSpan.Zero);
            UpdateManifest offset = Clone(utc);
            offset.PublishedUtc = new DateTimeOffset(2026, 8, 17, 10, 11, 12, TimeSpan.FromHours(2));

            CollectionAssert.AreEqual(
                UpdateManifestSignature.CreatePayload(utc),
                UpdateManifestSignature.CreatePayload(offset));
        }

        [TestMethod]
        public void Verify_PreservesUnicodeAndUtcMeaningAcrossSystemTextJsonRoundTrip()
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest manifest = Manifest("2.0.0");
            manifest.PublishedUtc = new DateTimeOffset(2026, 8, 17, 10, 11, 12, TimeSpan.FromHours(2));
            manifest.Notes = "Zażółć gęślą jaźń — 安全 ✅";
            UpdateManifestSignature.Sign(manifest, trusted);

            string json = JsonSerializer.Serialize(manifest);
            UpdateManifest roundTripped = JsonSerializer.Deserialize<UpdateManifest>(json);

            Assert.IsNotNull(roundTripped);
            Assert.AreEqual(manifest.Notes, roundTripped.Notes);
            Assert.IsTrue(UpdateManifestSignature.Verify(roundTripped, PublicKey(trusted)));
        }

        [DataTestMethod]
        [DataRow("schema")]
        [DataRow("version")]
        [DataRow("installerUrl")]
        [DataRow("sha256")]
        [DataRow("sizeBytes")]
        [DataRow("publishedUtc")]
        [DataRow("notes")]
        public void Verify_RejectsMutationOfEverySignedField(string field)
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest manifest = Manifest("2.0.0");
            UpdateManifestSignature.Sign(manifest, trusted);

            switch (field)
            {
                case "schema": manifest.SchemaVersion = 2; break;
                case "version": manifest.Version = "2.0.1"; break;
                case "installerUrl": manifest.InstallerUrl = "https://updates.example.test/releases/2.0.0/Other.exe"; break;
                case "sha256": manifest.Sha256 = new string('1', 64); break;
                case "sizeBytes": manifest.SizeBytes++; break;
                case "publishedUtc": manifest.PublishedUtc = manifest.PublishedUtc.Value.AddTicks(1); break;
                case "notes": manifest.Notes += " altered"; break;
                default: Assert.Fail("Unknown signed field mutation."); break;
            }

            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, PublicKey(trusted)), field);
        }

        [TestMethod]
        public void Verify_RejectsMissingSignatureAndUnsupportedAlgorithmWithoutDowngrade()
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest manifest = Manifest("2.0.0");

            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, PublicKey(trusted)));

            UpdateManifestSignature.Sign(manifest, trusted);
            manifest.SignatureAlgorithm = "rsa-pss-sha256";
            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, PublicKey(trusted)));

            manifest.SignatureAlgorithm = "RSA-SHA256";
            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, PublicKey(trusted)));
        }

        [TestMethod]
        public void Verify_RejectsMalformedAndNonCanonicalBase64()
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest manifest = Manifest("2.0.0");
            UpdateManifestSignature.Sign(manifest, trusted);
            string publicKey = PublicKey(trusted);

            manifest.Signature = "not-base64!";
            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, publicKey));

            UpdateManifestSignature.Sign(manifest, trusted);
            manifest.Signature = " " + manifest.Signature;
            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, publicKey));

            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, "not-base64!"));
            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, " " + publicKey));
        }

        [TestMethod]
        public void Verify_RejectsWrongKeyAndTrailingSubjectPublicKeyInfoData()
        {
            using RSA trusted = RSA.Create(2048);
            using RSA attacker = RSA.Create(2048);
            UpdateManifest manifest = Manifest("2.0.0");
            UpdateManifestSignature.Sign(manifest, trusted);

            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, PublicKey(attacker)));
            Assert.IsFalse(UpdateManifestSignature.Verify(
                manifest,
                Convert.ToBase64String(trusted.ExportSubjectPublicKeyInfo().Concat(new byte[] { 0 }).ToArray())));
        }

        [TestMethod]
        public void Verify_RejectsOversizedKeyAndSignatureInputs()
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest manifest = Manifest("2.0.0");
            UpdateManifestSignature.Sign(manifest, trusted);

            manifest.Signature = Convert.ToBase64String(new byte[16 * 1024]);
            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, PublicKey(trusted)));
            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, Convert.ToBase64String(new byte[16 * 1024])));
        }

        [TestMethod]
        public void Sign_RejectsInvalidSchemaUrlHashSizeTimestampAndNotes()
        {
            using RSA trusted = RSA.Create(2048);

            AssertInvalid(manifest => manifest.SchemaVersion = 2, trusted);
            AssertInvalid(manifest => manifest.Version = " 2.0.0", trusted);
            AssertInvalid(manifest => manifest.Version = "2.0.0+unsigned-metadata", trusted);
            AssertInvalid(manifest => manifest.InstallerUrl = "http://updates.example.test/Setup.exe", trusted);
            AssertInvalid(manifest => manifest.InstallerUrl = "https://user:password@updates.example.test/Setup.exe", trusted);
            AssertInvalid(manifest => manifest.Sha256 = new string('A', 64), trusted);
            AssertInvalid(manifest => manifest.SizeBytes = 0, trusted);
            AssertInvalid(manifest => manifest.SizeBytes = 256L * 1024L * 1024L + 1L, trusted);
            AssertInvalid(manifest => manifest.PublishedUtc = null, trusted);
            AssertInvalid(manifest => manifest.PublishedUtc = DateTimeOffset.MinValue, trusted);
            AssertInvalid(manifest => manifest.Notes = null, trusted);
            AssertInvalid(manifest => manifest.Notes = new string('x', 8001), trusted);
        }

        [TestMethod]
        public void Sign_AllowsEmptyNotesButVerifyRejectsExplicitNullNotes()
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest manifest = Manifest("2.0.0");
            manifest.Notes = string.Empty;
            UpdateManifestSignature.Sign(manifest, trusted);

            Assert.IsTrue(UpdateManifestSignature.Verify(manifest, PublicKey(trusted)));
            manifest.Notes = null;
            Assert.IsFalse(UpdateManifestSignature.Verify(manifest, PublicKey(trusted)));
        }

        [TestMethod]
        public void Sign_CountsReleaseNotesAsUnicodeScalarsAndRejectsMalformedSurrogates()
        {
            using RSA trusted = RSA.Create(2048);
            string astral = char.ConvertFromUtf32(0x1f680);
            UpdateManifest accepted = Manifest("2.0.0");
            accepted.Notes = string.Concat(Enumerable.Repeat(astral, 8000));
            UpdateManifest rejected = Manifest("2.0.0");
            rejected.Notes = accepted.Notes + astral;
            UpdateManifest malformedHigh = Manifest("2.0.0");
            malformedHigh.Notes = "release\ud800notes";
            UpdateManifest malformedLow = Manifest("2.0.0");
            malformedLow.Notes = "release\udc00notes";

            UpdateManifestSignature.Sign(accepted, trusted);

            Assert.IsTrue(UpdateManifestSignature.Verify(accepted, PublicKey(trusted)));
            Assert.ThrowsException<InvalidDataException>(() => UpdateManifestSignature.Sign(rejected, trusted));
            Assert.ThrowsException<InvalidDataException>(() => UpdateManifestSignature.Sign(malformedHigh, trusted));
            Assert.ThrowsException<InvalidDataException>(() => UpdateManifestSignature.Sign(malformedLow, trusted));
        }

        [TestMethod]
        public void Sign_AcceptsA128CharacterVersionAndRejectsA129CharacterVersion()
        {
            using RSA trusted = RSA.Create(2048);
            string maximumVersion = "1.2.3-" + new string('a', 122);
            string oversizedVersion = maximumVersion + "a";
            Assert.AreEqual(128, maximumVersion.Length);
            Assert.AreEqual(129, oversizedVersion.Length);

            UpdateManifest accepted = Manifest(maximumVersion);
            UpdateManifestSignature.Sign(accepted, trusted);

            Assert.IsTrue(UpdateManifestSignature.Verify(accepted, PublicKey(trusted)));
            Assert.ThrowsException<InvalidDataException>(() =>
                UpdateManifestSignature.Sign(Manifest(oversizedVersion), trusted));
        }

        [TestMethod]
        public void SystemTextJson_UsesTheExactManifestAndSourcePropertyNames()
        {
            UpdateManifest manifest = Manifest("2.0.0");
            manifest.SignatureAlgorithm = "RSA-PSS-SHA256";
            manifest.Signature = "signature";
            UpdateSourceConfiguration source = new UpdateSourceConfiguration
            {
                ManifestUrl = "https://updates.example.test/manifest.json",
                ManifestPublicKey = "public-key"
            };

            using JsonDocument manifestJson = JsonDocument.Parse(JsonSerializer.Serialize(manifest));
            CollectionAssert.AreEquivalent(
                ManifestPropertyNames,
                manifestJson.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
            using JsonDocument sourceJson = JsonDocument.Parse(JsonSerializer.Serialize(source));
            CollectionAssert.AreEquivalent(
                SourcePropertyNames,
                sourceJson.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        }

        [TestMethod]
        public void UpdateManifestJson_RejectsDuplicateUnknownMissingAndTrailingProperties()
        {
            UpdateManifest manifest = Manifest("2.0.0");
            manifest.SignatureAlgorithm = "RSA-PSS-SHA256";
            manifest.Signature = "signature";
            string json = JsonSerializer.Serialize(manifest);
            string duplicate = json.Replace(
                "\"version\":\"2.0.0\"",
                "\"version\":\"9.0.0\",\"version\":\"2.0.0\"",
                StringComparison.Ordinal);
            string unknown = json.Insert(json.Length - 1, ",\"unsignedField\":true");
            string missing = json.Replace(
                ",\"notes\":\"Synthetic release notes\"",
                string.Empty,
                StringComparison.Ordinal);
            string malformedSurrogate = json.Replace(
                "Synthetic release notes",
                "release\\ud800notes",
                StringComparison.Ordinal);

            AssertJsonRejected(() => UpdateManifestJson.DeserializeManifest(Encoding.UTF8.GetBytes(duplicate)));
            AssertJsonRejected(() => UpdateManifestJson.DeserializeManifest(Encoding.UTF8.GetBytes(unknown)));
            AssertJsonRejected(() => UpdateManifestJson.DeserializeManifest(Encoding.UTF8.GetBytes(missing)));
            AssertJsonRejected(() => UpdateManifestJson.DeserializeManifest(Encoding.UTF8.GetBytes(json + "{}")));
            AssertJsonRejected(() => UpdateManifestJson.DeserializeManifest(Encoding.UTF8.GetBytes(malformedSurrogate)));
        }

        [TestMethod]
        public void UpdateManifestJson_RejectsDuplicateAndIncompleteSourceProperties()
        {
            string duplicate = "{\"manifestUrl\":\"https://attacker.example.test/manifest.json\",\"manifestUrl\":\"https://updates.example.test/manifest.json\",\"manifestPublicKey\":\"key\"}";
            string missing = "{\"manifestUrl\":\"https://updates.example.test/manifest.json\"}";

            AssertJsonRejected(() => UpdateManifestJson.DeserializeSource(Encoding.UTF8.GetBytes(duplicate)));
            AssertJsonRejected(() => UpdateManifestJson.DeserializeSource(Encoding.UTF8.GetBytes(missing)));
        }

        private static void AssertJsonRejected(Action action)
        {
            try
            {
                action();
                Assert.Fail("Malformed update JSON was accepted.");
            }
            catch (JsonException)
            {
            }
        }

        private static void AssertInvalid(Action<UpdateManifest> mutate, RSA trusted)
        {
            UpdateManifest manifest = Manifest("2.0.0");
            mutate(manifest);
            Assert.ThrowsException<InvalidDataException>(() => UpdateManifestSignature.Sign(manifest, trusted));
        }

        private static UpdateManifest Manifest(string version)
        {
            return new UpdateManifest
            {
                SchemaVersion = 1,
                Version = version,
                InstallerUrl = "https://updates.example.test/releases/" + version + "/Setup.exe",
                Sha256 = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
                SizeBytes = 42,
                PublishedUtc = new DateTimeOffset(2026, 8, 17, 8, 11, 12, TimeSpan.Zero),
                Notes = "Synthetic release notes"
            };
        }

        private static UpdateManifest Clone(UpdateManifest manifest)
        {
            return JsonSerializer.Deserialize<UpdateManifest>(JsonSerializer.Serialize(manifest));
        }

        private static string PublicKey(RSA key)
        {
            return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
        }

        private static byte[] ExpectedPayload(UpdateManifest manifest)
        {
            using MemoryStream buffer = new MemoryStream();
            using (BinaryWriter writer = new BinaryWriter(buffer, new UTF8Encoding(false, true), true))
            {
                writer.Write(Magic);
                writer.Write(1);
                writer.Write("2.0.0");
                writer.Write("https://updates.example.test/releases/2.0.0/Setup.exe?arch=x64");
                writer.Write("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
                writer.Write(123456789L);
                writer.Write(new DateTimeOffset(2026, 8, 17, 8, 11, 12, TimeSpan.Zero).UtcDateTime.Ticks);
                writer.Write("Zażółć gęślą jaźń — 安全 ✅");
            }

            return buffer.ToArray();
        }
    }
}
