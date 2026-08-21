using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    public static class UpdateManifestJson
    {
        private static readonly string[] ManifestProperties =
        {
            "schemaVersion", "version", "installerUrl", "sha256", "sizeBytes", "publishedUtc",
            "notes", "signatureAlgorithm", "signature"
        };
        private static readonly string[] SourceProperties = { "manifestUrl", "manifestPublicKey" };
        private static readonly JsonDocumentOptions DocumentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 16
        };
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 16
        };

        public static UpdateManifest DeserializeManifest(byte[] utf8Json)
        {
            ArgumentNullException.ThrowIfNull(utf8Json);
            ValidateProperties(utf8Json, ManifestProperties);
            return JsonSerializer.Deserialize<UpdateManifest>(utf8Json, SerializerOptions)
                ?? throw new JsonException("The update manifest must be a JSON object.");
        }

        public static UpdateSourceConfiguration DeserializeSource(byte[] utf8Json)
        {
            ArgumentNullException.ThrowIfNull(utf8Json);
            ValidateProperties(utf8Json, SourceProperties);
            return JsonSerializer.Deserialize<UpdateSourceConfiguration>(utf8Json, SerializerOptions)
                ?? throw new JsonException("The update source must be a JSON object.");
        }

        private static void ValidateProperties(byte[] utf8Json, string[] expectedProperties)
        {
            using JsonDocument document = JsonDocument.Parse(utf8Json, DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("The update document must be a JSON object.");

            HashSet<string> expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!expected.Contains(property.Name) || !found.Add(property.Name))
                    throw new JsonException("The update document contains an unknown or duplicate property.");
            }

            if (found.Count != expected.Count)
                throw new JsonException("The update document is missing a required property.");
        }
    }
}
