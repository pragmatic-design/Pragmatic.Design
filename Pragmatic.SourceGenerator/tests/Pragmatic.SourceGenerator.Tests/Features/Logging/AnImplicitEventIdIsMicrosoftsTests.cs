using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Logging;

/// <summary>
///     A call site with no <c>EventId</c> logs the id Microsoft's generator gives the same declaration, so a
///     project that moves to Pragmatic's call sites keeps the event ids its filters and alerts are written
///     against.
/// </summary>
/// <remarks>
///     The expected ids are read from the output of Microsoft.Extensions.Logging.Generators 10.0.11, compiled
///     over these same declarations; they are written out, not computed, so a change to the derivation shows
///     up red. <c>LogStart</c>, <c>LogFailure</c> and <c>LogSuccess</c> hash negative, <c>LogExecuting</c>
///     positive, and <c>SagJmJ7</c> hashes to exactly <c>0x80000000</c>, the one value with no absolute value.
/// </remarks>
public class AnImplicitEventIdIsMicrosoftsTests
{
    private const string Source = """
        using Microsoft.Extensions.Logging;

        namespace Sample.Actions;

        public static partial class ActionLog
        {
            [LoggerMessage(Level = LogLevel.Information, Message = "start")]
            public static partial void LogStart(ILogger logger);

            [LoggerMessage(Level = LogLevel.Information, Message = "failure")]
            public static partial void LogFailure(ILogger logger);

            [LoggerMessage(Level = LogLevel.Information, Message = "success")]
            public static partial void LogSuccess(ILogger logger);

            [LoggerMessage(Level = LogLevel.Information, Message = "executing")]
            public static partial void LogExecuting(ILogger logger);

            [LoggerMessage(Level = LogLevel.Information, Message = "min")]
            public static partial void SagJmJ7(ILogger logger);

            [LoggerMessage(Level = LogLevel.Information, Message = "named", EventName = "LogStart")]
            public static partial void Named(ILogger logger);

            [LoggerMessage(Level = LogLevel.Information, Message = "empty", EventName = "")]
            public static partial void EmptyName(ILogger logger);
        }
        """;

    private static readonly CompiledCallSites CallSites = new(Source);

    [Theory]
    [InlineData("LogStart", 1092114269, "LogStart")]
    [InlineData("LogFailure", 873886395, "LogFailure")]
    [InlineData("LogSuccess", 1535103852, "LogSuccess")]
    [InlineData("LogExecuting", 328106247, "LogExecuting")]
    [InlineData("SagJmJ7", 0, "SagJmJ7")]
    [InlineData("Named", 1092114269, "LogStart")]
    [InlineData("EmptyName", 1260963059, "EmptyName")]
    public void TheCallSiteLogsTheIdMicrosoftsGeneratorGives(string method, int id, string name)
    {
        var logger = new CapturingLogger();

        CallSites.Call("Sample.Actions.ActionLog", method, logger);

        logger.EventId.Id.Should().Be(id);
        logger.EventId.Name.Should().Be(name);
    }
}
