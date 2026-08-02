using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CloudflareR2Uploader.Utilities;

namespace CloudflareR2Uploader.Services
{
    /// <summary>
    /// Writes daily, sanitised log files to %LocalAppData%\CloudflareR2Uploader\Logs and
    /// prunes files older than the configured retention period.
    /// <para>
    /// Everything written passes through <see cref="Sanitize"/>, which redacts anything that
    /// looks like a secret access key, an Authorization header, an AWS signature or a
    /// presigned URL. The secret access key is never handed to this class in the first place;
    /// the redaction is a second line of defence.
    /// </para>
    /// </summary>
    public sealed class LoggingService : ILoggingService, IDisposable
    {
        private static readonly Regex AuthorizationHeaderPattern = new Regex(
            @"(?i)\bauthorization\s*[:=]\s*\S+", RegexOptions.Compiled);

        private static readonly Regex SignatureQueryPattern = new Regex(
            @"(?i)\bx-amz-(signature|security-token|credential)=[^&\s""]+", RegexOptions.Compiled);

        private static readonly Regex SignatureHeaderPattern = new Regex(
            @"(?i)\bsignature\s*=\s*[0-9a-f]{16,}", RegexOptions.Compiled);

        private static readonly Regex PresignedUrlPattern = new Regex(
            @"(?i)https?://\S*[?&]x-amz-\S*", RegexOptions.Compiled);

        private static readonly Regex SecretAssignmentPattern = new Regex(
            @"(?i)\b(secret[_\-]?access[_\-]?key|secretkey|aws_secret_access_key|password|pwd)\b\s*[:=]\s*""?[^\s"",;}]+""?",
            RegexOptions.Compiled);

        /// <summary>R2 secret access keys are 64 hex characters; never let one reach a file.</summary>
        private static readonly Regex LongHexTokenPattern = new Regex(
            @"\b[0-9a-fA-F]{40,}\b", RegexOptions.Compiled);

        private readonly object _sync = new object();
        private readonly string _directory;
        private readonly int _retentionDays;

        private StreamWriter _writer;
        private DateTime _writerDate = DateTime.MinValue;
        private bool _disposed;
        private bool _writeFailureReported;

        public LoggingService(string directory = null, int retentionDays = 14)
        {
            _directory = string.IsNullOrEmpty(directory) ? AppPaths.LogDirectory : directory;
            _retentionDays = retentionDays < 1 ? 1 : retentionDays;

            try
            {
                AppPaths.EnsureDirectory(_directory);
                CleanupOldLogs();
            }
            catch (IOException)
            {
                // Logging must never stop the application from running.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public string LogDirectory { get { return _directory; } }

        public void Debug(string operation, string message) { Write(LogLevel.Debug, operation, message, null); }
        public void Info(string operation, string message) { Write(LogLevel.Info, operation, message, null); }
        public void Warning(string operation, string message) { Write(LogLevel.Warning, operation, message, null); }

        public void Error(string operation, string message, Exception exception)
        {
            Write(LogLevel.Error, operation, message, exception);
        }

        public void HttpFailure(string operation, int statusCode, string awsErrorCode, string message, string objectKey, string uploadId)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("http=").Append(statusCode.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(awsErrorCode)) builder.Append(" code=").Append(awsErrorCode);
            if (!string.IsNullOrEmpty(objectKey)) builder.Append(" key=").Append(objectKey);
            if (!string.IsNullOrEmpty(uploadId)) builder.Append(" uploadId=").Append(ShortenUploadId(uploadId));
            if (!string.IsNullOrEmpty(message)) builder.Append(" | ").Append(message);

            Write(statusCode >= 500 ? LogLevel.Warning : LogLevel.Error, operation, builder.ToString(), null);
        }

        private void Write(LogLevel level, string operation, string message, Exception exception)
        {
            if (_disposed) return;

            StringBuilder line = new StringBuilder(256);
            line.Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append('Z');
            line.Append(" [").Append(LevelText(level)).Append("] ");
            line.Append(string.IsNullOrEmpty(operation) ? "-" : operation);
            line.Append(" | ").Append(Sanitize(message));

            if (exception != null)
            {
                line.Append(" | ").Append(exception.GetType().FullName);
                line.Append(": ").Append(Sanitize(exception.Message));

                Exception inner = exception.InnerException;
                int depth = 0;
                while (inner != null && depth < 4)
                {
                    line.Append(" <- ").Append(inner.GetType().Name).Append(": ").Append(Sanitize(inner.Message));
                    inner = inner.InnerException;
                    depth++;
                }
            }

            AppendLine(line.ToString());
        }

        private void AppendLine(string text)
        {
            lock (_sync)
            {
                if (_disposed) return;

                try
                {
                    DateTime today = DateTime.UtcNow.Date;
                    if (_writer == null || _writerDate != today)
                    {
                        CloseWriter();
                        AppPaths.EnsureDirectory(_directory);

                        string path = Path.Combine(
                            _directory,
                            "r2uploader-" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

                        _writer = new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
                        _writerDate = today;

                        // A new day's file is a good moment to prune.
                        CleanupOldLogs();
                    }

                    _writer.WriteLine(text);
                    _writeFailureReported = false;
                }
                catch (IOException)
                {
                    HandleWriteFailure();
                }
                catch (UnauthorizedAccessException)
                {
                    HandleWriteFailure();
                }
            }
        }

        private void HandleWriteFailure()
        {
            // The log file may be locked or the disk full. Drop the writer so the next call
            // retries, and surface it once to the debugger rather than looping on failures.
            CloseWriter();
            if (!_writeFailureReported)
            {
                _writeFailureReported = true;
                System.Diagnostics.Debug.WriteLine("CloudflareR2Uploader: log file could not be written.");
            }
        }

        private void CloseWriter()
        {
            if (_writer == null) return;
            try { _writer.Dispose(); }
            catch (IOException) { }
            _writer = null;
            _writerDate = DateTime.MinValue;
        }

        private void CleanupOldLogs()
        {
            try
            {
                if (!Directory.Exists(_directory)) return;

                DateTime cutoff = DateTime.UtcNow.Date.AddDays(-_retentionDays);
                foreach (string file in Directory.GetFiles(_directory, "r2uploader-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string LevelText(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO ";
                case LogLevel.Warning: return "WARN ";
                case LogLevel.Error: return "ERROR";
                default: return "INFO ";
            }
        }

        /// <summary>Shows only the tail of an upload ID: enough to correlate, not enough to reuse.</summary>
        public static string ShortenUploadId(string uploadId)
        {
            if (string.IsNullOrEmpty(uploadId)) return string.Empty;
            return uploadId.Length <= 12 ? uploadId : "…" + uploadId.Substring(uploadId.Length - 12);
        }

        /// <summary>
        /// Redacts credential-shaped content. Public so the "Copy technical details" text can
        /// be run through exactly the same filter as the log file.
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            string result = text;
            result = AuthorizationHeaderPattern.Replace(result, "Authorization: <redacted>");
            result = PresignedUrlPattern.Replace(result, "<presigned-url-redacted>");
            result = SignatureQueryPattern.Replace(result, "<signed-query-redacted>");
            result = SignatureHeaderPattern.Replace(result, "Signature=<redacted>");
            result = SecretAssignmentPattern.Replace(result, "$1=<redacted>");
            result = LongHexTokenPattern.Replace(result, "<redacted>");

            // Collapse newlines so one log entry stays one line and stays greppable.
            result = result.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            return result;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                CloseWriter();
            }
        }
    }
}
