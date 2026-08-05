using System;
using Newtonsoft.Json;

namespace CloudflareR2Uploader.Models
{
    public sealed class UpdateManifest
    {
        [JsonProperty("schemaVersion", Required = Required.Always)]
        public int SchemaVersion { get; set; }

        [JsonProperty("version", Required = Required.Always)]
        public string Version { get; set; }

        [JsonProperty("installerUrl", Required = Required.Always)]
        public string InstallerUrl { get; set; }

        [JsonProperty("sha256", Required = Required.Always)]
        public string Sha256 { get; set; }

        [JsonProperty("sizeBytes", Required = Required.Always)]
        public long SizeBytes { get; set; }

        [JsonProperty("publishedUtc")]
        public DateTime? PublishedUtc { get; set; }

        [JsonProperty("notes")]
        public string Notes { get; set; }
    }

    public sealed class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public UpdateManifest Manifest { get; set; }
    }

    public sealed class UpdateDownloadProgress
    {
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }
        public int Percentage { get; set; }
    }

    internal sealed class UpdateSourceConfiguration
    {
        [JsonProperty("manifestUrl")]
        public string ManifestUrl { get; set; }
    }
}
