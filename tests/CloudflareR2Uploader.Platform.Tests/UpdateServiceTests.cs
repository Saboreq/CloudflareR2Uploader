using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudflareR2Uploader.Tests
{
    [TestClass]
    public sealed class UpdateServiceTests
    {
        [TestMethod]
        public void SemanticVersion_OrdersPrereleasesAndReleases()
        {
            SemanticVersion beta;
            SemanticVersion release;
            SemanticVersion patch;
            Assert.IsTrue(SemanticVersion.TryParse("1.2.3-beta.2", out beta));
            Assert.IsTrue(SemanticVersion.TryParse("1.2.3", out release));
            Assert.IsTrue(SemanticVersion.TryParse("1.2.4", out patch));
            Assert.IsTrue(beta.CompareTo(release) < 0);
            Assert.IsTrue(release.CompareTo(patch) < 0);
            Assert.IsFalse(SemanticVersion.TryParse("01.2.3", out beta));
        }

        [TestMethod]
        public async Task CheckAsync_ReportsNewerSignedManifestAndUsesCacheBuster()
        {
            using RSA trusted = RSA.Create(2048);
            Uri requested = null;
            UpdateManifest manifest = SignedManifest("1.1.0", new byte[] { 1, 2, 3 }, trusted);
            UpdateSourceConfiguration source = Source(trusted);
            using HttpClient client = new HttpClient(new FakeHandler(request =>
            {
                requested = request.RequestUri;
                return JsonResponse(manifest);
            }));
            using UpdateService service = new UpdateService(client);

            UpdateCheckResult result = await service.CheckAsync(source, "1.0.0", CancellationToken.None);

            Assert.IsTrue(result.IsUpdateAvailable);
            Assert.AreEqual("1.1.0", result.Manifest.Version);
            StringAssert.Contains(requested.Query, "check=");
        }

        [TestMethod]
        public async Task CheckAsync_DoesNotOfferSameSignedVersion()
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest manifest = SignedManifest("1.0.0", new byte[] { 4 }, trusted);
            using HttpClient client = new HttpClient(new FakeHandler(_ => JsonResponse(manifest)));
            using UpdateService service = new UpdateService(client);

            UpdateCheckResult result = await service.CheckAsync(Source(trusted), "1.0.0", CancellationToken.None);

            Assert.IsFalse(result.IsUpdateAvailable);
        }

        [TestMethod]
        public async Task CheckAsync_RejectsInvalidSourceBeforeAnyNetworkRequest()
        {
            int requests = 0;
            using HttpClient client = new HttpClient(new FakeHandler(_ =>
            {
                requests++;
                throw new AssertFailedException("Invalid source reached the network boundary.");
            }));
            using UpdateService service = new UpdateService(client);

            await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.CheckAsync(
                new UpdateSourceConfiguration
                {
                    ManifestUrl = "http://updates.example.test/manifest.json",
                    ManifestPublicKey = "not-base64"
                },
                "1.0.0",
                CancellationToken.None));

            Assert.AreEqual(0, requests);
        }

        [TestMethod]
        public async Task CheckAsync_RejectsUnsignedManifestImmediatelyAfterDeserialization()
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest unsigned = Manifest("1.1.0", new byte[] { 1, 2, 3 });
            using HttpClient client = new HttpClient(new FakeHandler(_ => JsonResponse(unsigned)));
            using UpdateService service = new UpdateService(client);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                service.CheckAsync(Source(trusted), "1.0.0", CancellationToken.None));
        }

        [TestMethod]
        public async Task CheckAsync_RejectsManifestAlteredAfterSigning()
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest altered = SignedManifest("1.1.0", new byte[] { 1, 2, 3 }, trusted);
            altered.Notes += " altered";
            using HttpClient client = new HttpClient(new FakeHandler(_ => JsonResponse(altered)));
            using UpdateService service = new UpdateService(client);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                service.CheckAsync(Source(trusted), "1.0.0", CancellationToken.None));
        }

        [TestMethod]
        public async Task CheckAsync_RejectsManifestSignedByWrongKey()
        {
            using RSA trusted = RSA.Create(2048);
            using RSA attacker = RSA.Create(2048);
            UpdateManifest attackerManifest = SignedManifest("9.0.0", new byte[] { 1, 2, 3 }, attacker);
            using HttpClient client = new HttpClient(new FakeHandler(_ => JsonResponse(attackerManifest)));
            using UpdateService service = new UpdateService(client);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                service.CheckAsync(Source(trusted), "1.0.0", CancellationToken.None));
        }

        [TestMethod]
        public async Task CheckAsync_RejectsTrailingJsonInsteadOfAcceptingTheFirstObject()
        {
            using RSA trusted = RSA.Create(2048);
            UpdateManifest manifest = SignedManifest("1.1.0", new byte[] { 1, 2, 3 }, trusted);
            string json = JsonSerializer.Serialize(manifest) + "{}";
            using HttpClient client = new HttpClient(new FakeHandler(_ => TextResponse(json)));
            using UpdateService service = new UpdateService(client);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
                service.CheckAsync(Source(trusted), "1.0.0", CancellationToken.None));
        }

        [TestMethod]
        public async Task DownloadInstallerAsync_ReverifiesBeforeNetworkOrFileAction()
        {
            using RSA trusted = RSA.Create(2048);
            byte[] payload = Encoding.UTF8.GetBytes("synthetic setup executable");
            UpdateManifest manifest = SignedManifest("2.0.0", payload, trusted);
            manifest.InstallerUrl = "https://attacker.example.test/Setup.exe";
            int requests = 0;
            using TemporaryDirectory temporary = new TemporaryDirectory();
            AppPaths.OverrideRootForTesting(temporary.Path);
            try
            {
                using HttpClient client = new HttpClient(new FakeHandler(_ =>
                {
                    requests++;
                    throw new AssertFailedException("Tampered manifest reached the installer network boundary.");
                }));
                using UpdateService service = new UpdateService(client);

                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => service.DownloadInstallerAsync(
                    Source(trusted), manifest, null, CancellationToken.None));

                Assert.AreEqual(0, requests);
                Assert.IsFalse(Directory.Exists(AppPaths.UpdateDirectory));
            }
            finally
            {
                AppPaths.OverrideRootForTesting(null);
            }
        }

        [TestMethod]
        public async Task DownloadInstallerAsync_RejectsSourceSubstitutionBeforeNetworkOrFileAction()
        {
            using RSA trusted = RSA.Create(2048);
            using RSA attacker = RSA.Create(2048);
            UpdateManifest manifest = SignedManifest("2.0.0", Encoding.UTF8.GetBytes("payload"), trusted);
            int requests = 0;
            using TemporaryDirectory temporary = new TemporaryDirectory();
            AppPaths.OverrideRootForTesting(temporary.Path);
            try
            {
                using HttpClient client = new HttpClient(new FakeHandler(_ =>
                {
                    requests++;
                    throw new AssertFailedException("Substituted source reached the installer network boundary.");
                }));
                using UpdateService service = new UpdateService(client);

                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => service.DownloadInstallerAsync(
                    Source(attacker), manifest, null, CancellationToken.None));

                Assert.AreEqual(0, requests);
                Assert.IsFalse(Directory.Exists(AppPaths.UpdateDirectory));
            }
            finally
            {
                AppPaths.OverrideRootForTesting(null);
            }
        }

        [TestMethod]
        public async Task DownloadInstallerAsync_VerifiesSizeAndHashBeforeFinalRename()
        {
            using RSA trusted = RSA.Create(2048);
            byte[] payload = Encoding.UTF8.GetBytes("synthetic setup executable");
            UpdateManifest manifest = SignedManifest("2.0.0", payload, trusted);
            using TemporaryDirectory temporary = new TemporaryDirectory();
            AppPaths.OverrideRootForTesting(temporary.Path);
            try
            {
                using HttpClient client = new HttpClient(new FakeHandler(_ => BinaryResponse(payload)));
                using UpdateService service = new UpdateService(client);

                string path = await service.DownloadInstallerAsync(
                    Source(trusted), manifest, null, CancellationToken.None);

                Assert.IsTrue(File.Exists(path));
                CollectionAssert.AreEqual(payload, File.ReadAllBytes(path));
                Assert.AreEqual(0, Directory.GetFiles(AppPaths.UpdateDirectory, "*.download").Length);
            }
            finally
            {
                AppPaths.OverrideRootForTesting(null);
            }
        }

        [TestMethod]
        public async Task DownloadInstallerAsync_RemovesPartialWhenHashFails()
        {
            using RSA trusted = RSA.Create(2048);
            byte[] expected = Encoding.UTF8.GetBytes("expected");
            byte[] downloaded = Encoding.UTF8.GetBytes("tampered");
            UpdateManifest manifest = SignedManifest("2.0.1", expected, trusted);
            Assert.AreEqual(expected.Length, downloaded.Length);
            using TemporaryDirectory temporary = new TemporaryDirectory();
            AppPaths.OverrideRootForTesting(temporary.Path);
            try
            {
                using HttpClient client = new HttpClient(new FakeHandler(_ => BinaryResponse(downloaded)));
                using UpdateService service = new UpdateService(client);

                await Assert.ThrowsExceptionAsync<InvalidDataException>(() => service.DownloadInstallerAsync(
                    Source(trusted), manifest, null, CancellationToken.None));

                Assert.AreEqual(0, Directory.GetFiles(AppPaths.UpdateDirectory).Length);
            }
            finally
            {
                AppPaths.OverrideRootForTesting(null);
            }
        }

        [TestMethod]
        public void OpenVerifiedInstallerForLaunch_RevalidatesTheSignedBytesAndHoldsTheExactFile()
        {
            using RSA trusted = RSA.Create(2048);
            byte[] payload = Encoding.UTF8.GetBytes("verified launch payload");
            UpdateManifest manifest = SignedManifest("2.0.0", payload, trusted);
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string installer = temporary.File("Setup.exe");
            File.WriteAllBytes(installer, payload);

            using (VerifiedInstallerLaunch launch = UpdateService.OpenVerifiedInstallerForLaunch(
                Source(trusted), manifest, installer))
            {
                Assert.AreEqual(Path.GetFullPath(installer), launch.InstallerPath);
                if (OperatingSystem.IsWindows())
                {
                    Assert.ThrowsException<IOException>(() =>
                    {
                        using FileStream writer = new(
                            installer, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                    });
                    Assert.ThrowsException<IOException>(() => File.Delete(installer));
                }
            }

            File.Delete(installer);
            Assert.IsFalse(File.Exists(installer));
        }

        [TestMethod]
        public void OpenVerifiedInstallerForLaunch_RejectsSameLengthSubstitutionAndWrongTrustRoot()
        {
            using RSA trusted = RSA.Create(2048);
            using RSA attacker = RSA.Create(2048);
            byte[] expected = Encoding.UTF8.GetBytes("expected installer");
            byte[] replacement = Encoding.UTF8.GetBytes("attacker installer");
            Assert.AreEqual(expected.Length, replacement.Length);
            UpdateManifest manifest = SignedManifest("2.0.0", expected, trusted);
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string installer = temporary.File("Setup.exe");
            File.WriteAllBytes(installer, replacement);

            Assert.ThrowsException<InvalidDataException>(() =>
                UpdateService.OpenVerifiedInstallerForLaunch(Source(trusted), manifest, installer));
            Assert.ThrowsException<InvalidDataException>(() =>
                UpdateService.OpenVerifiedInstallerForLaunch(Source(attacker), manifest, installer));
        }

        [TestMethod]
        public void LoadUpdateSource_AcceptsOnlyCompleteHttpsConfigurationWithValidSpki()
        {
            using RSA trusted = RSA.Create(2048);
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string path = temporary.File("update-source.json");
            UpdateSourceConfiguration expected = Source(trusted);
            File.WriteAllText(path, JsonSerializer.Serialize(expected), new UTF8Encoding(false));

            UpdateSourceConfiguration loaded = UpdateService.LoadUpdateSource(temporary.Path);

            Assert.IsNotNull(loaded);
            Assert.AreEqual(expected.ManifestUrl, loaded.ManifestUrl);
            Assert.AreEqual(expected.ManifestPublicKey, loaded.ManifestPublicKey);

            File.WriteAllText(path, "{\"manifestUrl\":\"http://updates.example.test/manifest.json\",\"manifestPublicKey\":\"" + expected.ManifestPublicKey + "\"}");
            Assert.IsNull(UpdateService.LoadUpdateSource(temporary.Path));
            File.WriteAllText(path, "{\"manifestUrl\":\"https://updates.example.test/manifest.json\",\"manifestPublicKey\":\"not-base64\"}");
            Assert.IsNull(UpdateService.LoadUpdateSource(temporary.Path));
            File.WriteAllText(path, "{\"manifestUrl\":\"https://updates.example.test/manifest.json\"}");
            Assert.IsNull(UpdateService.LoadUpdateSource(temporary.Path));
        }

        [TestMethod]
        public void LoadUpdateSource_RejectsTrailingAndOversizedInput()
        {
            using RSA trusted = RSA.Create(2048);
            using TemporaryDirectory temporary = new TemporaryDirectory();
            string path = temporary.File("update-source.json");
            string json = JsonSerializer.Serialize(Source(trusted));

            File.WriteAllText(path, json + "{}", new UTF8Encoding(false));
            Assert.IsNull(UpdateService.LoadUpdateSource(temporary.Path));

            File.WriteAllBytes(path, new byte[64 * 1024 + 1]);
            Assert.IsNull(UpdateService.LoadUpdateSource(temporary.Path));
        }

        private static UpdateManifest SignedManifest(string version, byte[] bytes, RSA key)
        {
            UpdateManifest manifest = Manifest(version, bytes);
            UpdateManifestSignature.Sign(manifest, key);
            return manifest;
        }

        private static UpdateManifest Manifest(string version, byte[] bytes)
        {
            return new UpdateManifest
            {
                SchemaVersion = 1,
                Version = version,
                InstallerUrl = "https://updates.example.test/releases/" + version + "/Setup.exe",
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                SizeBytes = bytes.Length,
                PublishedUtc = new DateTimeOffset(2026, 8, 17, 8, 11, 12, TimeSpan.Zero),
                Notes = "Synthetic release notes"
            };
        }

        private static UpdateSourceConfiguration Source(RSA key)
        {
            return new UpdateSourceConfiguration
            {
                ManifestUrl = "https://updates.example.test/channel/manifest.json",
                ManifestPublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
            };
        }

        private static HttpResponseMessage JsonResponse(UpdateManifest manifest)
        {
            return TextResponse(JsonSerializer.Serialize(manifest));
        }

        private static HttpResponseMessage TextResponse(string text)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(text, Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage BinaryResponse(byte[] payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
            {
                _response = response;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                HttpResponseMessage response = _response(request);
                response.RequestMessage = request;
                return Task.FromResult(response);
            }
        }
    }
}
