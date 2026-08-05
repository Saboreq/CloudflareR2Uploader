#nullable enable

using System;
using System.Runtime.Serialization;

namespace CloudflareR2Uploader.Models
{
    /// <summary>What a recorded activity did. Values are persisted, so they never change.</summary>
    public enum ActivityAction
    {
        Upload = 0,
        Download = 1,
        Delete = 2,
        Rename = 3,
        Move = 4,
        Copy = 5,
        NewFolder = 6,
        PublicLink = 7,
        TemporaryLink = 8,
        Connection = 9,
        Update = 10
    }

    /// <summary>How it ended.</summary>
    public enum ActivityResult
    {
        Success = 0,
        Failed = 1,
        Cancelled = 2,
        Skipped = 3,
        Copied = 4,
        Created = 5
    }

    /// <summary>
    /// Which chip on the Activity screen an action belongs to.
    /// </summary>
    public enum ActivityCategory
    {
        Uploads = 0,
        Downloads = 1,
        Changes = 2,
        Other = 3
    }

    /// <summary>
    /// One entry in the local activity history.
    /// <para>
    /// Everything written here is user-visible and exportable, so it must contain no secret
    /// material: no credentials, no authorization headers, no signed-URL query strings and
    /// no private object contents. <c>ActivityHistoryService</c> sanitises every record
    /// before it is persisted; this type only carries the shape.
    /// </para>
    /// </summary>
    [DataContract(Name = "ActivityRecord", Namespace = "")]
    public sealed class ActivityRecord
    {
        public ActivityRecord()
        {
            Id = Guid.NewGuid().ToString("N");
            TimestampUtcText = FormatUtc(DateTime.UtcNow);
            Target = string.Empty;
            SecondaryTarget = string.Empty;
            BucketName = string.Empty;
            ProfileId = string.Empty;
            ErrorCode = string.Empty;
            ErrorMessage = string.Empty;
            CorrelationId = string.Empty;
        }

        [DataMember(Name = "id", Order = 1)]
        public string Id { get; set; }

        /// <summary>ISO-8601 UTC. Stored as text so the file stays portable and readable.</summary>
        [DataMember(Name = "timestampUtc", Order = 2)]
        public string TimestampUtcText { get; set; }

        [DataMember(Name = "action", Order = 3)]
        public int ActionValue { get; set; }

        [DataMember(Name = "result", Order = 4)]
        public int ResultValue { get; set; }

        /// <summary>The object key, prefix or other sanitised subject of the action.</summary>
        [DataMember(Name = "target", Order = 5)]
        public string Target { get; set; }

        /// <summary>Rename and move destinations. Empty otherwise.</summary>
        [DataMember(Name = "secondaryTarget", Order = 6)]
        public string SecondaryTarget { get; set; }

        [DataMember(Name = "bucket", Order = 7)]
        public string BucketName { get; set; }

        [DataMember(Name = "profileId", Order = 8)]
        public string ProfileId { get; set; }

        /// <summary>Bytes transferred, or null when the action moved no data.</summary>
        [DataMember(Name = "sizeBytes", Order = 9)]
        public long? SizeBytes { get; set; }

        /// <summary>Wall-clock duration in milliseconds, or null when it is not meaningful.</summary>
        [DataMember(Name = "durationMs", Order = 10)]
        public long? DurationMilliseconds { get; set; }

        /// <summary>HTTP status or S3 error code, for failures only.</summary>
        [DataMember(Name = "errorCode", Order = 11)]
        public string ErrorCode { get; set; }

        /// <summary>Sanitised, human-readable failure reason.</summary>
        [DataMember(Name = "errorMessage", Order = 12)]
        public string ErrorMessage { get; set; }

        /// <summary>Groups the records produced by one bulk operation.</summary>
        [DataMember(Name = "correlationId", Order = 13)]
        public string CorrelationId { get; set; }

        public ActivityAction Action
        {
            get { return Enum.IsDefined(typeof(ActivityAction), ActionValue) ? (ActivityAction)ActionValue : ActivityAction.Upload; }
            set { ActionValue = (int)value; }
        }

        public ActivityResult Result
        {
            get { return Enum.IsDefined(typeof(ActivityResult), ResultValue) ? (ActivityResult)ResultValue : ActivityResult.Success; }
            set { ResultValue = (int)value; }
        }

        public DateTime TimestampUtc
        {
            get { return ParseUtc(TimestampUtcText); }
            set { TimestampUtcText = FormatUtc(value); }
        }

        public ActivityCategory Category
        {
            get
            {
                switch (Action)
                {
                    case ActivityAction.Upload:
                        return ActivityCategory.Uploads;
                    case ActivityAction.Download:
                        return ActivityCategory.Downloads;
                    case ActivityAction.Delete:
                    case ActivityAction.Rename:
                    case ActivityAction.Move:
                    case ActivityAction.Copy:
                    case ActivityAction.NewFolder:
                        return ActivityCategory.Changes;
                    default:
                        return ActivityCategory.Other;
                }
            }
        }

        public bool IsError { get { return Result == ActivityResult.Failed; } }

        /// <summary>True when the record has enough shape to be shown and exported.</summary>
        public bool IsStructurallyValid()
        {
            return !string.IsNullOrEmpty(Id)
                && TimestampUtc != DateTime.MinValue
                && Enum.IsDefined(typeof(ActivityAction), ActionValue)
                && Enum.IsDefined(typeof(ActivityResult), ResultValue);
        }

        public static string FormatUtc(DateTime value)
        {
            return value.ToUniversalTime().ToString(
                "yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static DateTime ParseUtc(string? text)
        {
            DateTime parsed;
            if (!string.IsNullOrEmpty(text) &&
                DateTime.TryParse(
                    text,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                    System.Globalization.DateTimeStyles.AssumeUniversal,
                    out parsed))
            {
                return parsed;
            }
            return DateTime.MinValue;
        }
    }
}
