using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Services;
using CloudflareR2Uploader.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

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
        public async Task CheckAsync_ReportsNewerManifestAndUsesCacheBuster()
        {
            Uri requested = null;
            UpdateManifest manifest = Manifest("1.1.0", new byte[] { 1, 2, 3 });
            using (HttpClient client = new HttpClient(new FakeHandler(request =>
            {
                requested = request.RequestUri;
                return JsonResponse(manifest);
            })))
            using (UpdateService service = new UpdateService(client))
            {
                UpdateCheckResult result = await service.CheckAsync("https://updates.example.test/channel/manifest.json", "1.0.0", CancellationToken.None);
                Assert.IsTrue(result.IsUpdateAvailable);
                Assert.AreEqual("1.1.0", result.Manifest.Version);
                StringAssert.Contains(requested.Query, "check=");
            }
        }

        [TestMethod]
        public async Task CheckAsync_DoesNotOfferSameOrOlderVersion()
        {
            UpdateManifest manifest = Manifest("1.0.0", new byte[] { 4 });
            using (HttpClient client = new HttpClient(new FakeHandler(request => JsonResponse(manifest))))
            using (UpdateService service = new UpdateService(client))
            {
                UpdateCheckResult result = await service.CheckAsync("https://updates.example.test/manifest.json", "1.0.0", CancellationToken.None);
                Assert.IsFalse(result.IsUpdateAvailable);
            }
        }

        [TestMethod]
        public async Task CheckAsync_RejectsNonHttpsSource()
        {
            using (UpdateService service = new UpdateService(new HttpClient(new FakeHandler(request => new HttpResponseMessage(HttpStatusCode.OK)))))
            {
                await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.CheckAsync("http://updates.example.test/manifest.json", "1.0.0", CancellationToken.None));
            }
        }

        [TestMethod]
        public async Task DownloadInstallerAsync_VerifiesSizeAndHashBeforeFinalRename()
        {
            byte[] payload = Encoding.UTF8.GetBytes("synthetic setup executable");
            UpdateManifest manifest = Manifest("2.0.0", payload);
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                AppPaths.OverrideRootForTesting(temporary.Path);
                try
                {
                    using (HttpClient client = new HttpClient(new FakeHandler(request => BinaryResponse(payload))))
                    using (UpdateService service = new UpdateService(client))
                    {
                        string path = await service.DownloadInstallerAsync(manifest, null, CancellationToken.None);
                        Assert.IsTrue(File.Exists(path));
                        CollectionAssert.AreEqual(payload, File.ReadAllBytes(path));
                        Assert.AreEqual(0, Directory.GetFiles(AppPaths.UpdateDirectory, "*.download").Length);
                    }
                }
                finally { AppPaths.OverrideRootForTesting(null); }
            }
        }

        [TestMethod]
        public async Task DownloadInstallerAsync_RemovesPartialWhenHashFails()
        {
            byte[] payload = Encoding.UTF8.GetBytes("payload");
            UpdateManifest manifest = Manifest("2.0.1", payload);
            manifest.Sha256 = new string('0', 64);
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                AppPaths.OverrideRootForTesting(temporary.Path);
                try
                {
                    using (HttpClient client = new HttpClient(new FakeHandler(request => BinaryResponse(payload))))
                    using (UpdateService service = new UpdateService(client))
                    {
                        await Assert.ThrowsExceptionAsync<InvalidDataException>(() => service.DownloadInstallerAsync(manifest, null, CancellationToken.None));
                        Assert.AreEqual(0, Directory.GetFiles(AppPaths.UpdateDirectory).Length);
                    }
                }
                finally { AppPaths.OverrideRootForTesting(null); }
            }
        }

        [TestMethod]
        public void LoadManifestUrl_AcceptsOnlyHttpsConfiguration()
        {
            using (TemporaryDirectory temporary = new TemporaryDirectory())
            {
                File.WriteAllText(temporary.File("update-source.json"), "{\"manifestUrl\":\"https://updates.example.test/manifest.json\"}");
                Assert.AreEqual("https://updates.example.test/manifest.json", UpdateService.LoadManifestUrl(temporary.Path));
                File.WriteAllText(temporary.File("update-source.json"), "{\"manifestUrl\":\"http://updates.example.test/manifest.json\"}");
                Assert.IsNull(UpdateService.LoadManifestUrl(temporary.Path));
            }
        }

        private static UpdateManifest Manifest(string version, byte[] bytes)
        {
            return new UpdateManifest
            {
                SchemaVersion = 1,
                Version = version,
                InstallerUrl = "https://updates.example.test/releases/" + version + "/Setup.exe",
                Sha256 = Hash(bytes),
                SizeBytes = bytes.Length,
                Notes = "Synthetic release notes"
            };
        }

        private static HttpResponseMessage JsonResponse(UpdateManifest manifest)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(manifest), Encoding.UTF8, "application/json") };
        }

        private static HttpResponseMessage BinaryResponse(byte[] payload)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;
            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> response) { _response = response; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = _response(request);
                response.RequestMessage = request;
                return Task.FromResult(response);
            }
        }
    }
}
