namespace CloudflareR2Uploader.Models
{
    /// <summary>Distinguishes the failure modes the user has to act on differently.</summary>
    public enum ConnectionTestStatus
    {
        Success = 0,
        MissingConfiguration = 1,
        InvalidEndpoint = 2,
        AuthenticationFailed = 3,
        AccessDenied = 4,
        BucketNotFound = 5,
        NetworkError = 6,
        TlsError = 7,
        RateLimited = 8,
        UnknownError = 9
    }

    /// <summary>Outcome of "Test connection". Carries no credential material.</summary>
    public sealed class ConnectionTestResult
    {
        public ConnectionTestResult(ConnectionTestStatus status, string headline, string detail, string technicalDetails)
        {
            Status = status;
            Headline = headline;
            Detail = detail;
            TechnicalDetails = technicalDetails;
        }

        public ConnectionTestStatus Status { get; private set; }

        /// <summary>Short summary, e.g. "Bucket not found".</summary>
        public string Headline { get; private set; }

        /// <summary>Actionable explanation shown under the headline.</summary>
        public string Detail { get; private set; }

        /// <summary>Sanitised diagnostics for the copy button.</summary>
        public string TechnicalDetails { get; private set; }

        public bool IsSuccess { get { return Status == ConnectionTestStatus.Success; } }
    }
}
