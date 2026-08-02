using System.Runtime.Serialization;

namespace CloudflareR2Uploader.Models
{
    /// <summary>One part that R2 has confirmed. Needed to complete or resume the upload.</summary>
    [DataContract(Name = "CompletedPart", Namespace = "")]
    public sealed class CompletedPartState
    {
        public CompletedPartState() { }

        public CompletedPartState(int partNumber, string eTag, long size)
        {
            PartNumber = partNumber;
            ETag = eTag;
            Size = size;
        }

        [DataMember(Name = "partNumber", Order = 1)]
        public int PartNumber { get; set; }

        /// <summary>
        /// The ETag R2 returned for this part. For a multipart upload the final object ETag is
        /// a digest of the part ETags, not the whole-file MD5, so it is never treated as a
        /// content checksum.
        /// </summary>
        [DataMember(Name = "eTag", Order = 2)]
        public string ETag { get; set; }

        [DataMember(Name = "size", Order = 3)]
        public long Size { get; set; }
    }
}
