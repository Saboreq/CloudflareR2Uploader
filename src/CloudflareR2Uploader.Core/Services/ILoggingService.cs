using System;

namespace CloudflareR2Uploader.Services
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>
    /// Sanitised diagnostic log. Implementations must never write credential material,
    /// authorization headers, signed URLs or encrypted blobs.
    /// </summary>
    public interface ILoggingService
    {
        void Debug(string operation, string message);
        void Info(string operation, string message);
        void Warning(string operation, string message);
        void Error(string operation, string message, Exception exception);

        /// <summary>
        /// Logs an HTTP-shaped failure with its status code, which is what makes R2 problems
        /// diagnosable after the fact.
        /// </summary>
        void HttpFailure(string operation, int statusCode, string awsErrorCode, string message, string objectKey, string uploadId);

        string LogDirectory { get; }
    }
}
