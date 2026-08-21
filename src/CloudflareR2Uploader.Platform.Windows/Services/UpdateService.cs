using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    public sealed class VerifiedInstallerLaunch : IDisposable
    {
        private FileStream _lock;

        internal VerifiedInstallerLaunch(string installerPath, FileStream installerLock)
        {
            InstallerPath = installerPath;
            _lock = installerLock;
        }

        public string InstallerPath { get; }

        public void Dispose() => Interlocked.Exchange(ref _lock, null)?.Dispose();
    }

    public sealed class UpdateService : IDisposable
    {
        private const int MaximumManifestBytes = 64 * 1024;
        private const long MaximumInstallerBytes = 256L * 1024L * 1024L;
        private readonly HttpClient _client;
        private readonly bool _ownsClient;

        public UpdateService(HttpClient client = null, string applicationVersion = null)
        {
            _client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _ownsClient = client == null;
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "CloudflareR2Uploader/" + SanitizeUserAgentVersion(applicationVersion));
        }

        public static string GetEntryAssemblyVersion()
        {
            try
            {
                Assembly assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                AssemblyInformationalVersionAttribute informational =
                    assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (informational != null && !string.IsNullOrWhiteSpace(informational.InformationalVersion))
                    return informational.InformationalVersion;

                Version version = assembly.GetName().Version;
                return version == null ? "1.0" : version.ToString(3);
            }
            catch (Exception)
            {
                return "1.0";
            }
        }

        private static string SanitizeUserAgentVersion(string applicationVersion)
        {
            string value = string.IsNullOrWhiteSpace(applicationVersion)
                ? GetEntryAssemblyVersion()
                : applicationVersion.Trim();

            int metadata = value.IndexOf('+');
            if (metadata > 0) value = value.Substring(0, metadata);

            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character) || character == '.' || character == '-') builder.Append(character);
            }

            return builder.Length == 0 ? "1.0" : builder.ToString();
        }

        public static UpdateSourceConfiguration LoadUpdateSource(string applicationDirectory)
        {
            try
            {
                string path = Path.Combine(applicationDirectory ?? string.Empty, "update-source.json");
                if (!File.Exists(path)) return null;

                byte[] json = ReadBoundedFile(path, MaximumManifestBytes);
                UpdateSourceConfiguration source = UpdateManifestJson.DeserializeSource(json);
                return TryValidateSource(source, out _) ? source : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        public async Task<UpdateCheckResult> CheckAsync(
            UpdateSourceConfiguration source,
            string currentVersion,
            CancellationToken cancellationToken)
        {
            UpdateSourceConfiguration trustedSource = Snapshot(source);
            if (!TryValidateSource(trustedSource, out Uri manifestUri))
                throw new ArgumentException("The update source must contain an absolute HTTPS URL and a valid RSA public key.", nameof(source));
            if (!SemanticVersion.TryParse(currentVersion, out SemanticVersion installed))
                throw new ArgumentException("The installed application version is invalid.", nameof(currentVersion));

            using HttpResponseMessage response = await _client.GetAsync(
                AddCacheBuster(manifestUri),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            EnsureHttpsResponse(response, "update manifest");
            if (response.Content.Headers.ContentLength.HasValue &&
                response.Content.Headers.ContentLength.Value > MaximumManifestBytes)
                throw new InvalidDataException("The update manifest is too large.");

            byte[] json;
            using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                await CopyBoundedAsync(
                    stream,
                    buffer,
                    MaximumManifestBytes,
                    null,
                    0,
                    cancellationToken).ConfigureAwait(false);
                json = buffer.ToArray();
            }

            UpdateManifest manifest;
            try
            {
                manifest = UpdateManifestJson.DeserializeManifest(json);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The update manifest is not valid JSON.", ex);
            }

            if (!UpdateManifestSignature.Verify(manifest, trustedSource.ManifestPublicKey))
                throw new InvalidDataException("The update manifest signature is invalid.");
            if (!SemanticVersion.TryParse(manifest.Version, out SemanticVersion available))
                throw new InvalidDataException("The update manifest version is invalid.");

            return new UpdateCheckResult
            {
                Manifest = manifest,
                IsUpdateAvailable = available.CompareTo(installed) > 0
            };
        }

        public async Task<string> DownloadInstallerAsync(
            UpdateSourceConfiguration source,
            UpdateManifest manifest,
            IProgress<UpdateDownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            UpdateSourceConfiguration trustedSource = Snapshot(source);
            if (!TryValidateSource(trustedSource, out _))
                throw new ArgumentException("The update source must contain an absolute HTTPS URL and a valid RSA public key.", nameof(source));

            UpdateManifest trustedManifest = Snapshot(manifest);
            if (!UpdateManifestSignature.Verify(trustedManifest, trustedSource.ManifestPublicKey))
                throw new InvalidDataException("The update manifest signature is invalid.");

            AppPaths.EnsureDirectory(AppPaths.UpdateDirectory);
            CleanupOldDownloads();
            string safeVersion = trustedManifest.Version.Replace('+', '-');
            string finalPath = Path.Combine(
                AppPaths.UpdateDirectory,
                "CloudflareR2Uploader-v" + safeVersion + "-Setup.exe");
            string partialPath = finalPath + ".download";
            TryDelete(partialPath);

            try
            {
                using (HttpResponseMessage response = await _client.GetAsync(
                    trustedManifest.InstallerUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    EnsureHttpsResponse(response, "installer");
                    if (response.Content.Headers.ContentLength.HasValue &&
                        response.Content.Headers.ContentLength.Value != trustedManifest.SizeBytes)
                        throw new InvalidDataException("The installer length does not match the update manifest.");

                    using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using FileStream output = new FileStream(
                        partialPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        81920,
                        true);
                    await CopyBoundedAsync(
                        input,
                        output,
                        MaximumInstallerBytes,
                        progress,
                        trustedManifest.SizeBytes,
                        cancellationToken).ConfigureAwait(false);
                }

                FileInfo downloaded = new FileInfo(partialPath);
                if (downloaded.Length != trustedManifest.SizeBytes)
                    throw new InvalidDataException("The downloaded installer length does not match the update manifest.");

                string actualHash;
                using (FileStream input = new FileStream(partialPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (SHA256 sha = SHA256.Create())
                    actualHash = ToHex(sha.ComputeHash(input));
                if (!FixedTimeEquals(actualHash, trustedManifest.Sha256))
                    throw new InvalidDataException("The downloaded installer failed SHA-256 verification.");

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

        public static VerifiedInstallerLaunch OpenVerifiedInstallerForLaunch(
            UpdateSourceConfiguration source,
            UpdateManifest manifest,
            string installerPath)
        {
            UpdateSourceConfiguration trustedSource = Snapshot(source);
            UpdateManifest trustedManifest = Snapshot(manifest);
            if (!TryValidateSource(trustedSource, out _) ||
                !UpdateManifestSignature.Verify(trustedManifest, trustedSource.ManifestPublicKey))
                throw new InvalidDataException("The update manifest signature is invalid.");

            string fullPath = Path.GetFullPath(installerPath ?? string.Empty);
            FileStream installerLock = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.SequentialScan);
            try
            {
                if (installerLock.Length != trustedManifest.SizeBytes)
                    throw new InvalidDataException("The installer length no longer matches the update manifest.");

                byte[] actualHash = SHA256.HashData(installerLock);
                byte[] expectedHash = Convert.FromHexString(trustedManifest.Sha256);
                if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                    throw new InvalidDataException("The installer no longer matches the signed update manifest.");
                installerLock.Position = 0;
                return new VerifiedInstallerLaunch(fullPath, installerLock);
            }
            catch
            {
                installerLock.Dispose();
                throw;
            }
        }

        private static UpdateSourceConfiguration Snapshot(UpdateSourceConfiguration source)
        {
            if (source == null) return null;
            return new UpdateSourceConfiguration
            {
                ManifestUrl = source.ManifestUrl,
                ManifestPublicKey = source.ManifestPublicKey
            };
        }

        private static UpdateManifest Snapshot(UpdateManifest manifest)
        {
            if (manifest == null) return null;
            return new UpdateManifest
            {
                SchemaVersion = manifest.SchemaVersion,
                Version = manifest.Version,
                InstallerUrl = manifest.InstallerUrl,
                Sha256 = manifest.Sha256,
                SizeBytes = manifest.SizeBytes,
                PublishedUtc = manifest.PublishedUtc,
                Notes = manifest.Notes,
                SignatureAlgorithm = manifest.SignatureAlgorithm,
                Signature = manifest.Signature
            };
        }

        private static bool TryValidateSource(UpdateSourceConfiguration source, out Uri manifestUri)
        {
            manifestUri = null;
            return source != null &&
                TryHttpsUri(source.ManifestUrl, out manifestUri) &&
                UpdateManifestSignature.TryImportPublicKey(source.ManifestPublicKey);
        }

        private static byte[] ReadBoundedFile(string path, int maximumBytes)
        {
            using FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length <= 0 || input.Length > maximumBytes)
                throw new InvalidDataException("The update source configuration is outside the supported size range.");

            using MemoryStream output = new MemoryStream((int)input.Length);
            byte[] buffer = new byte[4096];
            int total = 0;
            while (true)
            {
                int read = input.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total += read;
                if (total > maximumBytes)
                    throw new InvalidDataException("The update source configuration is too large.");
                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }

        private static async Task CopyBoundedAsync(
            Stream input,
            Stream output,
            long maximumBytes,
            IProgress<UpdateDownloadProgress> progress,
            long totalBytes,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[81920];
            long copied = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                copied += read;
                if (copied > maximumBytes)
                    throw new InvalidDataException("The downloaded file is larger than allowed.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                if (progress != null)
                {
                    progress.Report(new UpdateDownloadProgress
                    {
                        BytesReceived = copied,
                        TotalBytes = totalBytes,
                        Percentage = totalBytes <= 0 ? 0 : (int)Math.Min(100, copied * 100 / totalBytes)
                    });
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private static void EnsureHttpsResponse(HttpResponseMessage response, string description)
        {
            if (response.RequestMessage == null || response.RequestMessage.RequestUri == null ||
                !string.Equals(
                    response.RequestMessage.RequestUri.Scheme,
                    Uri.UriSchemeHttps,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The " + description + " redirected to a non-HTTPS address.");
        }

        private static Uri AddCacheBuster(Uri uri)
        {
            UriBuilder builder = new UriBuilder(uri);
            string value = "check=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            builder.Query = string.IsNullOrEmpty(builder.Query)
                ? value
                : builder.Query.TrimStart('?') + "&" + value;
            return builder.Uri;
        }

        private static bool TryHttpsUri(string value, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrEmpty(value) ||
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

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes)
                builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static void CleanupOldDownloads()
        {
            try
            {
                foreach (string file in Directory.GetFiles(
                    AppPaths.UpdateDirectory,
                    "CloudflareR2Uploader-*-Setup.exe*"))
                {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7)) TryDelete(file);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public void Dispose()
        {
            if (_ownsClient) _client.Dispose();
        }
    }
}
