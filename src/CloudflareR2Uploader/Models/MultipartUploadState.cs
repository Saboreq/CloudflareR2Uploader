using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace CloudflareR2Uploader.Models
{
    /// <summary>
    /// Everything needed to resume an interrupted multipart upload, and nothing more.
    /// Written to <c>%LocalAppData%\CloudflareR2Uploader\UploadState\</c>.
    /// It deliberately contains no credentials.
    /// </summary>
    [DataContract(Name = "MultipartUploadState", Namespace = "")]
    public sealed class MultipartUploadState
    {
        public MultipartUploadState()
        {
            Parts = new List<CompletedPartState>();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Parts = new List<CompletedPartState>();
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (Parts == null) Parts = new List<CompletedPartState>();
        }

        /// <summary>Identifies the state file. Derived from bucket + key + local path.</summary>
        [DataMember(Name = "stateId", Order = 1)]
        public string StateId { get; set; }

        [DataMember(Name = "localFilePath", Order = 2)]
        public string LocalFilePath { get; set; }

        [DataMember(Name = "fileSize", Order = 3)]
        public long FileSize { get; set; }

        /// <summary>ISO-8601 UTC. Stored as text so the JSON stays portable and human-readable.</summary>
        [DataMember(Name = "lastWriteUtc", Order = 4)]
        public string LastWriteUtcText { get; set; }

        [DataMember(Name = "bucketName", Order = 5)]
        public string BucketName { get; set; }

        [DataMember(Name = "objectKey", Order = 6)]
        public string ObjectKey { get; set; }

        [DataMember(Name = "uploadId", Order = 7)]
        public string UploadId { get; set; }

        [DataMember(Name = "partSize", Order = 8)]
        public long PartSize { get; set; }

        [DataMember(Name = "contentType", Order = 9)]
        public string ContentType { get; set; }

        [DataMember(Name = "createdUtc", Order = 10)]
        public string CreatedUtcText { get; set; }

        [DataMember(Name = "parts", Order = 11)]
        public List<CompletedPartState> Parts { get; set; }

        public DateTime LastWriteUtc
        {
            get { return ParseUtc(LastWriteUtcText); }
            set { LastWriteUtcText = FormatUtc(value); }
        }

        public DateTime CreatedUtc
        {
            get { return ParseUtc(CreatedUtcText); }
            set { CreatedUtcText = FormatUtc(value); }
        }

        public static string FormatUtc(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
        }

        public static DateTime ParseUtc(string text)
        {
            DateTime parsed;
            if (!string.IsNullOrEmpty(text) &&
                DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
            {
                return parsed;
            }
            return DateTime.MinValue;
        }

        /// <summary>Total bytes already confirmed by R2.</summary>
        public long CompletedBytes
        {
            get
            {
                long total = 0;
                if (Parts != null)
                {
                    foreach (CompletedPartState part in Parts) total += part.Size;
                }
                return total;
            }
        }

        public bool HasPart(int partNumber)
        {
            if (Parts == null) return false;
            foreach (CompletedPartState part in Parts)
            {
                if (part.PartNumber == partNumber) return true;
            }
            return false;
        }

        /// <summary>Adds or replaces a part, keeping the list sorted by part number.</summary>
        public void SetPart(CompletedPartState part)
        {
            if (part == null) return;
            if (Parts == null) Parts = new List<CompletedPartState>();

            for (int i = 0; i < Parts.Count; i++)
            {
                if (Parts[i].PartNumber == part.PartNumber)
                {
                    Parts[i] = part;
                    return;
                }
            }

            Parts.Add(part);
            Parts.Sort(delegate (CompletedPartState a, CompletedPartState b)
            {
                return a.PartNumber.CompareTo(b.PartNumber);
            });
        }

        /// <summary>Basic self-consistency check for a file loaded from disk.</summary>
        public bool IsStructurallyValid()
        {
            return !string.IsNullOrEmpty(UploadId)
                && !string.IsNullOrEmpty(BucketName)
                && !string.IsNullOrEmpty(ObjectKey)
                && !string.IsNullOrEmpty(LocalFilePath)
                && PartSize > 0
                && FileSize >= 0;
        }
    }
}
