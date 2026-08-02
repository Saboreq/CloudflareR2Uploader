using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace CloudflareR2Uploader.Services
{
    /// <summary>Reports a retry so the caller can update a queue item's retry counter.</summary>
    public sealed class RetryAttemptInfo
    {
        public RetryAttemptInfo(int attempt, int maxAttempts, TimeSpan delay, Exception exception, string operation)
        {
            Attempt = attempt;
            MaxAttempts = maxAttempts;
            Delay = delay;
            Exception = exception;
            Operation = operation;
        }

        /// <summary>1-based number of the attempt that just failed.</summary>
        public int Attempt { get; private set; }
        public int MaxAttempts { get; private set; }
        public TimeSpan Delay { get; private set; }
        public Exception Exception { get; private set; }
        public string Operation { get; private set; }
    }

    /// <summary>
    /// Runs an operation with exponential backoff and jitter, retrying only conditions that
    /// <see cref="TransientErrorClassifier"/> considers temporary.
    /// </summary>
    public sealed class RetryService
    {
        public const int DefaultMaxAttempts = 5;

        private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

        private readonly ILoggingService _log;
        private readonly int _maxAttempts;

        [ThreadStatic]
        private static Random _random;

        public RetryService(ILoggingService log, int maxAttempts = DefaultMaxAttempts)
        {
            _log = log;
            _maxAttempts = maxAttempts < 1 ? 1 : maxAttempts;
        }

        public int MaxAttempts { get { return _maxAttempts; } }

        /// <summary>
        /// Exponential backoff with full jitter:
        /// <c>delay = random(0, min(maxDelay, base * 2^(attempt-1)))</c>, floored at a tenth of
        /// the window so a retry is never effectively immediate. Full jitter is used because it
        /// spreads concurrent part retries out instead of resynchronising them.
        /// </summary>
        public static TimeSpan CalculateDelay(int attempt, Random random = null)
        {
            if (attempt < 1) attempt = 1;

            double exponentialMs = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            if (exponentialMs > MaxDelay.TotalMilliseconds || double.IsInfinity(exponentialMs))
                exponentialMs = MaxDelay.TotalMilliseconds;

            Random source = random ?? GetRandom();
            double jittered = exponentialMs * (0.1 + (0.9 * source.NextDouble()));

            return TimeSpan.FromMilliseconds(jittered);
        }

        /// <summary>Upper bound of the backoff window for a given attempt, used by the tests.</summary>
        public static TimeSpan CalculateDelayCeiling(int attempt)
        {
            if (attempt < 1) attempt = 1;
            double exponentialMs = BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
            if (exponentialMs > MaxDelay.TotalMilliseconds || double.IsInfinity(exponentialMs))
                exponentialMs = MaxDelay.TotalMilliseconds;
            return TimeSpan.FromMilliseconds(exponentialMs);
        }

        /// <summary>
        /// Executes <paramref name="operation"/>, retrying transient failures.
        /// Permanent failures and user cancellation propagate immediately.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(
            string operationName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken,
            Action<RetryAttemptInfo> onRetry = null)
        {
            if (operation == null) throw new ArgumentNullException("operation");

            int attempt = 0;
            while (true)
            {
                attempt++;
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    return await operation(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ErrorClassification classification = TransientErrorClassifier.Classify(ex, cancellationToken);

                    if (classification == ErrorClassification.UserCancelled)
                    {
                        // Surface as cancellation, never as a failure to retry.
                        throw new OperationCanceledException(cancellationToken);
                    }

                    if (classification == ErrorClassification.Permanent)
                    {
                        LogGivingUp(operationName, attempt, ex, "the error is permanent");
                        throw;
                    }

                    if (attempt >= _maxAttempts)
                    {
                        LogGivingUp(operationName, attempt, ex, "the attempt limit was reached");
                        throw;
                    }

                    TimeSpan delay = CalculateDelay(attempt);

                    if (_log != null)
                    {
                        _log.Warning(
                            operationName,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Attempt {0}/{1} failed with a transient error (http={2} code={3}); retrying in {4} ms. {5}: {6}",
                                attempt,
                                _maxAttempts,
                                TransientErrorClassifier.GetStatusCode(ex),
                                TransientErrorClassifier.GetErrorCode(ex),
                                (int)delay.TotalMilliseconds,
                                ex.GetType().Name,
                                LoggingService.Sanitize(ex.Message)));
                    }

                    if (onRetry != null)
                    {
                        onRetry(new RetryAttemptInfo(attempt, _maxAttempts, delay, ex, operationName));
                    }

                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Void-returning overload.</summary>
        public async Task ExecuteAsync(
            string operationName,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken,
            Action<RetryAttemptInfo> onRetry = null)
        {
            await ExecuteAsync<bool>(
                operationName,
                async token =>
                {
                    await operation(token).ConfigureAwait(false);
                    return true;
                },
                cancellationToken,
                onRetry).ConfigureAwait(false);
        }

        private void LogGivingUp(string operationName, int attempt, Exception ex, string reason)
        {
            if (_log == null) return;

            _log.Error(
                operationName,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Giving up after {0} attempt(s) because {1} (http={2} code={3}).",
                    attempt, reason,
                    TransientErrorClassifier.GetStatusCode(ex),
                    TransientErrorClassifier.GetErrorCode(ex)),
                ex);
        }

        private static Random GetRandom()
        {
            if (_random == null)
            {
                // Thread-local seeds keep concurrent part retries from lining up.
                _random = new Random(Environment.TickCount ^ Thread.CurrentThread.ManagedThreadId * 7919);
            }
            return _random;
        }
    }
}
