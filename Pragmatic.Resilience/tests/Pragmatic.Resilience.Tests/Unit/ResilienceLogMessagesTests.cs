using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Resilience.Telemetry;

namespace Pragmatic.Resilience.Tests.Unit;

public class ResilienceLogMessagesTests
{
    [Fact]
    public void LogRetryAttempt_EmitsWarning_WithFormattedMessage()
    {
        var logger = new RecordingLogger();

        ResilienceLogMessages.LogRetryAttempt(logger, attemptNumber: 2, maxRetries: 5,
            operationName: "fetch", delayMs: 250, errorMessage: "timeout");

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("Retry attempt 2/5 for fetch");
        entry.Message.Should().Contain("250ms");
        entry.Message.Should().Contain("timeout");
    }

    [Fact]
    public void LogTimeout_EmitsWarning_WithOperationAndDuration()
    {
        var logger = new RecordingLogger();

        ResilienceLogMessages.LogTimeout(logger, "slow-op", timeoutMs: 5000);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Be("Operation slow-op timed out after 5000ms");
    }

    [Fact]
    public void LogRetryExhausted_EmitsError()
    {
        var logger = new RecordingLogger();

        ResilienceLogMessages.LogRetryExhausted(logger, maxRetries: 3, operationName: "fetch", errorMessage: "boom");

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain("All 3 retry attempts exhausted for fetch");
        entry.Message.Should().Contain("boom");
    }

    [Fact]
    public void LogCircuitRejected_EmitsWarning_WithCircuitKey()
    {
        var logger = new RecordingLogger();

        ResilienceLogMessages.LogCircuitRejected(logger, "payments");

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("payments");
        entry.Message.Should().Contain("circuit is open");
    }

    [Fact]
    public void LogCircuitOpened_EmitsWarning_WithFailureCount()
    {
        var logger = new RecordingLogger();

        ResilienceLogMessages.LogCircuitOpened(logger, "payments", failureCount: 7);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Message.Should().Be("Circuit 'payments' opened after 7 consecutive failures");
    }

    [Fact]
    public void LogBulkheadRejected_EmitsWarning_WithConcurrency()
    {
        var logger = new RecordingLogger();

        ResilienceLogMessages.LogBulkheadRejected(logger, "orders", maxConcurrency: 8);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Message.Should().Contain("orders");
        entry.Message.Should().Contain("max concurrency 8");
    }

    [Fact]
    public void LogFallbackUsed_EmitsInformation()
    {
        var logger = new RecordingLogger();

        ResilienceLogMessages.LogFallbackUsed(logger, "checkout", "downstream failed");

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Contain("Fallback used for 'checkout'");
        entry.Message.Should().Contain("downstream failed");
    }

    [Fact]
    public void LogHedgingAttempt_EmitsInformation_WithCorrectFieldBinding()
    {
        var logger = new RecordingLogger();

        // Verifies that named-parameter binding (not positional) produces the right message,
        // since the signature order differs from the message-template order.
        ResilienceLogMessages.LogHedgingAttempt(logger, operationName: "search", attemptNumber: 2, maxAttempts: 3);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Be("Hedging attempt 2/3 launched for 'search'");
    }

    [Fact]
    public void LogHedgingSuccess_EmitsInformation_WithWinningAttempt()
    {
        var logger = new RecordingLogger();

        ResilienceLogMessages.LogHedgingSuccess(logger, "search", winningAttempt: 2);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Message.Should().Be("Hedging succeeded for 'search' on attempt 2");
    }

    [Fact]
    public void LogRateLimitRejected_EmitsWarning_WithLimitAndWindow()
    {
        var logger = new RecordingLogger();

        ResilienceLogMessages.LogRateLimitRejected(logger, "api", maxRequests: 100, windowSeconds: 1);

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("Rate limit rejected 'api'");
        entry.Message.Should().Contain("max 100 requests per 1s");
    }

    [Fact]
    public void LogRetrySkipped_EmitsDebug()
    {
        var logger = new RecordingLogger();

        ResilienceLogMessages.LogRetrySkipped(logger, "fetch", "non-retryable");

        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Debug);
        entry.Message.Should().Contain("Retry skipped for 'fetch'");
        entry.Message.Should().Contain("non-retryable");
    }

    /// <summary>Minimal <see cref="ILogger"/> that captures level + formatted message of each entry.</summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));

        public sealed record LogEntry(LogLevel Level, string Message);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
