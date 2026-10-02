using Microsoft.Extensions.Logging;

namespace Pragmatic.Resilience.Telemetry;

/// <summary>
/// High-performance structured log messages for resilience events.
/// </summary>
public static partial class ResilienceLogMessages
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Retry attempt {AttemptNumber}/{MaxRetries} for {OperationName} after {DelayMs:F0}ms. Error: {ErrorMessage}")]
    public static partial void LogRetryAttempt(ILogger logger, int attemptNumber, int maxRetries,
        string operationName, double delayMs, string errorMessage);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Operation {OperationName} timed out after {TimeoutMs:F0}ms")]
    public static partial void LogTimeout(ILogger logger, string operationName, double timeoutMs);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "All {MaxRetries} retry attempts exhausted for {OperationName}. Last error: {ErrorMessage}")]
    public static partial void LogRetryExhausted(ILogger logger, int maxRetries,
        string operationName, string errorMessage);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Circuit '{CircuitKey}' rejected request — circuit is open")]
    public static partial void LogCircuitRejected(ILogger logger, string circuitKey);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Circuit '{CircuitKey}' opened after {FailureCount} consecutive failures")]
    public static partial void LogCircuitOpened(ILogger logger, string circuitKey, int failureCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Bulkhead rejected '{OperationName}' — max concurrency {MaxConcurrency} reached")]
    public static partial void LogBulkheadRejected(ILogger logger, string operationName, int maxConcurrency);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Fallback used for '{OperationName}'. Original error: {ErrorMessage}")]
    public static partial void LogFallbackUsed(ILogger logger, string operationName, string errorMessage);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Hedging attempt {AttemptNumber}/{MaxAttempts} launched for '{OperationName}'")]
    public static partial void LogHedgingAttempt(ILogger logger, string operationName, int attemptNumber, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Hedging succeeded for '{OperationName}' on attempt {WinningAttempt}")]
    public static partial void LogHedgingSuccess(ILogger logger, string operationName, int winningAttempt);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rate limit rejected '{OperationName}' — max {MaxRequests} requests per {WindowSeconds:F0}s")]
    public static partial void LogRateLimitRejected(ILogger logger, string operationName, int maxRequests, double windowSeconds);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Retry skipped for '{OperationName}' — ShouldRetry predicate returned false. Error: {ErrorMessage}")]
    public static partial void LogRetrySkipped(ILogger logger, string operationName, string errorMessage);
}
