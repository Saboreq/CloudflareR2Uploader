using System;
using System.Text.Json.Serialization;

namespace CloudflareR2Uploader.Models
{
    public sealed class UpdateManifest
    {
        [JsonPropertyName("schemaVersion")]
        [JsonRequired]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("version")]
        [JsonRequired]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("installerUrl")]
        [JsonRequired]
        public string InstallerUrl { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        [JsonRequired]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("sizeBytes")]
        [JsonRequired]
        public long SizeBytes { get; set; }

        [JsonPropertyName("publishedUtc")]
        [JsonRequired]
        public DateTimeOffset? PublishedUtc { get; set; }

        [JsonPropertyName("notes")]
        [JsonRequired]
        public string Notes { get; set; } = string.Empty;

        [JsonPropertyName("signatureAlgorithm")]
        [JsonRequired]
        public string SignatureAlgorithm { get; set; } = string.Empty;

        [JsonPropertyName("signature")]
        [JsonRequired]
        public string Signature { get; set; } = string.Empty;
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

    public sealed class UpdateSourceConfiguration
    {
        [JsonPropertyName("manifestUrl")]
        [JsonRequired]
        public string ManifestUrl { get; set; } = string.Empty;

        [JsonPropertyName("manifestPublicKey")]
        [JsonRequired]
        public string ManifestPublicKey { get; set; } = string.Empty;
    }
}
