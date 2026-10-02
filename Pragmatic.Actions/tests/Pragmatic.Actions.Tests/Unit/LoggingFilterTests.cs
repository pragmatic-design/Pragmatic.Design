using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Pipeline.Filters;
using Pragmatic.Result;
using Xunit;

namespace Pragmatic.Actions.Tests.Unit;

/// <summary>
///     Tests for the LoggingFilter behavior.
///     Covers: order, before/after logging for success and failure.
/// </summary>
public class LoggingFilterTests
{
    // =========================================================================
    // Test doubles
    // =========================================================================

    private sealed class TestAction : DomainAction<string>
    {
        public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
            => Task.FromResult(Result<string, IError>.Success("ok"));
    }

    private sealed class TestError(string code) : IError
    {
        public string Code { get; } = code;
        public int StatusCode => 500;
        public string Title => Code;
    }

    /// <summary>
    ///     Captures log messages for assertion.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static (LoggingFilter Filter, CapturingLogger<LoggingFilter> Logger) CreateFilterWithCapture()
    {
        var logger = new CapturingLogger<LoggingFilter>();
        var filter = new LoggingFilter(logger);
        return (filter, logger);
    }

    // =========================================================================
    // Tests — Filter Order
    // =========================================================================

    [Fact]
    public void Order_Returns1000()
    {
        var (filter, _) = CreateFilterWithCapture();

        filter.Order.Should().Be(FilterOrder.Logging);
        filter.Order.Should().Be(1000);
    }

    // =========================================================================
    // Tests — BeforeExecute
    // =========================================================================

    [Fact]
    public async Task BeforeExecute_LogsActionExecution()
    {
        var (filter, logger) = CreateFilterWithCapture();
        var action = new TestAction();

        var result = await filter.BeforeExecuteAsync<TestAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Debug);
        logger.Entries[0].Message.Should().Contain("TestAction");
    }

    // =========================================================================
    // Tests — AfterExecute Success
    // =========================================================================

    [Fact]
    public async Task AfterExecute_SuccessResult_LogsAtInformationLevel()
    {
        var (filter, logger) = CreateFilterWithCapture();
        var action = new TestAction();
        var result = Result<string, IError>.Success("ok");

        await filter.AfterExecuteAsync<TestAction, string>(action, result, CancellationToken.None);

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Information);
        logger.Entries[0].Message.Should().Contain("TestAction");
        logger.Entries[0].Message.Should().Contain("String");
    }

    // =========================================================================
    // Tests — AfterExecute Failure
    // =========================================================================

    [Fact]
    public async Task AfterExecute_FailureResult_LogsAtWarningLevel()
    {
        var (filter, logger) = CreateFilterWithCapture();
        var action = new TestAction();
        var result = Result<string, IError>.Failure(new TestError("NOT_FOUND"));

        await filter.AfterExecuteAsync<TestAction, string>(action, result, CancellationToken.None);

        logger.Entries.Should().ContainSingle();
        logger.Entries[0].Level.Should().Be(LogLevel.Warning);
        logger.Entries[0].Message.Should().Contain("TestAction");
        logger.Entries[0].Message.Should().Contain("NOT_FOUND");
    }

    // =========================================================================
    // Tests — BeforeExecute always succeeds (never short-circuits)
    // =========================================================================

    [Fact]
    public async Task BeforeExecute_NeverShortCircuits()
    {
        var (filter, _) = CreateFilterWithCapture();
        var action = new TestAction();

        var result = await filter.BeforeExecuteAsync<TestAction, string>(action, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
