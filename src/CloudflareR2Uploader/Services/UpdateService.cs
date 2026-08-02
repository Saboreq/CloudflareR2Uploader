using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;
using Newtonsoft.Json;

namespace CloudflareR2Uploader.Services
{
    public sealed class UpdateService : IDisposable
    {
        private const int MaximumManifestBytes = 64 * 1024;
        private const long MaximumInstallerBytes = 256L * 1024L * 1024L;
        private readonly HttpClient _client;
        private readonly bool _ownsClient;

        public UpdateService(HttpClient client = null)
        {
            _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _ownsClient = client == null;
            _client.DefaultRequestHeaders.UserAgent.ParseAdd("CloudflareR2Uploader/" + Program.GetVersion());
        }

        public static string LoadManifestUrl(string applicationDirectory)
        {
            try
            {
                string path = Path.Combine(applicationDirectory ?? string.Empty, "update-source.json");
                if (!File.Exists(path)) return null;
                FileInfo info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaximumManifestBytes) return null;
                UpdateSourceConfiguration source = JsonConvert.DeserializeObject<UpdateSourceConfiguration>(File.ReadAllText(path, Encoding.UTF8));
                Uri uri;
                if (source == null || !TryHttpsUri(source.ManifestUrl, out uri)) return null;
                return uri.AbsoluteUri;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (JsonException) { return null; }
        }

        public async Task<UpdateCheckResult> CheckAsync(string manifestUrl, string currentVersion, CancellationToken cancellationToken)
        {
            Uri manifestUri;
            if (!TryHttpsUri(manifestUrl, out manifestUri)) throw new ArgumentException("The update manifest must use an absolute HTTPS URL.", "manifestUrl");
            SemanticVersion installed;
            if (!SemanticVersion.TryParse(currentVersion, out installed)) throw new ArgumentException("The installed application version is invalid.", "currentVersion");

            using (HttpResponseMessage response = await _client.GetAsync(AddCacheBuster(manifestUri), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                EnsureHttpsResponse(response, "update manifest");
                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaximumManifestBytes)
                    throw new InvalidDataException("The update manifest is too large.");
                string json;
                using (Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (MemoryStream buffer = new MemoryStream())
                {
                    await CopyBoundedAsync(stream, buffer, MaximumManifestBytes, cancellationToken, null, 0).ConfigureAwait(false);
                    json = Encoding.UTF8.GetString(buffer.ToArray());
                }
                UpdateManifest manifest;
                try { manifest = JsonConvert.DeserializeObject<UpdateManifest>(json); }
                catch (JsonException ex) { throw new InvalidDataException("The update manifest is not valid JSON.", ex); }
                ValidateManifest(manifest);
                SemanticVersion available;
                SemanticVersion.TryParse(manifest.Version, out available);
                return new UpdateCheckResult { Manifest = manifest, IsUpdateAvailable = available.CompareTo(installed) > 0 };
            }
        }

        public async Task<string> DownloadInstallerAsync(UpdateManifest manifest, IProgress<UpdateDownloadProgress> progress, CancellationToken cancellationToken)
        {
            ValidateManifest(manifest);
            AppPaths.EnsureDirectory(AppPaths.UpdateDirectory);
            CleanupOldDownloads();
            string safeVersion = manifest.Version.Replace('+', '-');
            string finalPath = Path.Combine(AppPaths.UpdateDirectory, "CloudflareR2Uploader-v" + safeVersion + "-Setup.exe");
            string partialPath = finalPath + ".download";
            TryDelete(partialPath);
            try
            {
                using (HttpResponseMessage response = await _client.GetAsync(manifest.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    EnsureHttpsResponse(response, "installer");
                    if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value != manifest.SizeBytes)
                        throw new InvalidDataException("The installer length does not match the update manifest.");
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        await CopyBoundedAsync(input, output, MaximumInstallerBytes, cancellationToken, progress, manifest.SizeBytes).ConfigureAwait(false);
                }
                FileInfo downloaded = new FileInfo(partialPath);
                if (downloaded.Length != manifest.SizeBytes) throw new InvalidDataException("The downloaded installer length does not match the update manifest.");
                string actualHash;
                using (FileStream input = new FileStream(partialPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (SHA256 sha = SHA256.Create()) actualHash = ToHex(sha.ComputeHash(input));
                if (!FixedTimeEquals(actualHash, manifest.Sha256)) throw new InvalidDataException("The downloaded installer failed SHA-256 verification.");
                TryDelete(finalPath);
                File.Move(partialPath, finalPath);
                return finalPath;
            }
            catch
            {
                TryDelete(partialPath);
                throw;
            }
        }

        internal static void ValidateManifest(UpdateManifest manifest)
        {
            if (manifest == null || manifest.SchemaVersion != 1) throw new InvalidDataException("The update manifest schema is not supported.");
            SemanticVersion version;
            if (!SemanticVersion.TryParse(manifest.Version, out version)) throw new InvalidDataException("The update manifest version is invalid.");
            Uri installerUri;
            if (!TryHttpsUri(manifest.InstallerUrl, out installerUri)) throw new InvalidDataException("The installer must use an absolute HTTPS URL.");
            if (manifest.SizeBytes <= 0 || manifest.SizeBytes > MaximumInstallerBytes) throw new InvalidDataException("The installer size is outside the supported range.");
            if (string.IsNullOrWhiteSpace(manifest.Sha256) || manifest.Sha256.Length != 64) throw new InvalidDataException("The update manifest SHA-256 is invalid.");
            for (int i = 0; i < manifest.Sha256.Length; i++)
                if (!Uri.IsHexDigit(manifest.Sha256[i])) throw new InvalidDataException("The update manifest SHA-256 is invalid.");
            manifest.Sha256 = manifest.Sha256.ToLowerInvariant();
            manifest.Notes = (manifest.Notes ?? string.Empty).Trim();
            if (manifest.Notes.Length > 8000) manifest.Notes = manifest.Notes.Substring(0, 8000);
        }

        private static async Task CopyBoundedAsync(Stream input, Stream output, long maximumBytes, CancellationToken cancellationToken, IProgress<UpdateDownloadProgress> progress, long totalBytes)
        {
            byte[] buffer = new byte[81920];
            long copied = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                copied += read;
                if (copied > maximumBytes) throw new InvalidDataException("The downloaded file is larger than allowed.");
                await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                if (progress != null) progress.Report(new UpdateDownloadProgress { BytesReceived = copied, TotalBytes = totalBytes, Percentage = totalBytes <= 0 ? 0 : (int)Math.Min(100, copied * 100 / totalBytes) });
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void EnsureHttpsResponse(HttpResponseMessage response, string description)
        {
            if (response.RequestMessage == null || response.RequestMessage.RequestUri == null ||
                !string.Equals(response.RequestMessage.RequestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The " + description + " redirected to a non-HTTPS address.");
        }

        private static Uri AddCacheBuster(Uri uri)
        {
            UriBuilder builder = new UriBuilder(uri);
            string value = "check=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            builder.Query = string.IsNullOrEmpty(builder.Query) ? value : builder.Query.TrimStart('?') + "&" + value;
            return builder.Uri;
        }

        private static bool TryHttpsUri(string value, out Uri uri)
        {
            return Uri.TryCreate((value ?? string.Empty).Trim(), UriKind.Absolute, out uri) &&
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(uri.UserInfo);
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }

        private static void CleanupOldDownloads()
        {
            try
            {
                foreach (string file in Directory.GetFiles(AppPaths.UpdateDirectory, "CloudflareR2Uploader-*-Setup.exe*"))
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) TryDelete(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose() { if (_ownsClient) _client.Dispose(); }
    }
}
