using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Context;

/// <summary>
///     A pushed context and the logger's scopes live in one ambient chain (#134): a request holds both, and with an
///     ambient value each, every push and pop under the other copied a two-entry map of them.
/// </summary>
public class TheContextAndTheScopesShareOneAmbientChainTests
{
    private static readonly KeyValuePair<string, object?>[] Request =
    [
        new("CorrelationId", "corr-7f3a"),
        new("RequestId", "req-0042"),
    ];

    private static readonly Dictionary<string, object?> Scope = new() { ["SessionId"] = "session_abc123" };

    [Fact]
    public void AScopeInsideAContext_AllocatesNoMoreThanAScopeInsideAScope()
    {
        using var factory = Factory(Memory());
        var logger = factory.CreateLogger("Orders");

        var insideAScope = InsideAScope(logger);
        var insideAContext = InsideAContext(logger);

        insideAContext.Fewest.Should().BeLessThanOrEqualTo(insideAScope.Fewest,
            $"the context is a node of the scopes' own chain, not a second ambient value (in a context: {insideAContext.Samples}; in a scope: {insideAScope.Samples})");
    }

    [Fact]
    public void AScopeInsideAContext_SeesTheContextAndLeavesItWhenDisposed()
    {
        using var factory = Factory(Memory());
        var logger = factory.CreateLogger("Orders");

        using var context = LogContextScope.PushContext(Request);
        var pushed = LogContextScope.Current;

        using (logger.BeginScope(Scope))
            LogContextScope.Current.Should().BeSameAs(pushed);

        LogContextScope.Current.Should().BeSameAs(pushed);
    }

    [Fact]
    public void AContextInsideAScope_LeavesTheScopeWhenDisposed()
    {
        var sink = Memory();
        using var factory = Factory(sink);
        var logger = factory.CreateLogger("Orders");

        using (logger.BeginScope(Scope))
        {
            using (LogContextScope.PushContext(Request))
                LogContextScope.GetProperty("RequestId").Should().Be("req-0042");

            LogContextScope.Current.Should().BeNull();
            logger.LogInformation("inside the scope");
        }

        sink.GetLogEntries().Single().Scopes!.Should().Contain(new KeyValuePair<string, object?>("SessionId", "session_abc123"));
    }

    [Fact]
    public void OneContextPushedTwice_RestoresEachChainOnItsOwn()
    {
        var shared = new LogContext();
        shared.SetProperty("TenantId", "acme");

        using (LogContextScope.PushProperty("Outer", 1))
        {
            var outer = LogContextScope.Current;
            using (LogContextScope.Push(shared))
            {
                using (LogContextScope.Push(shared))
                    LogContextScope.Current.Should().BeSameAs(shared);

                LogContextScope.Current.Should().BeSameAs(shared);
            }

            LogContextScope.Current.Should().BeSameAs(outer);
        }

        LogContextScope.Current.Should().BeNull();
    }

    // What one scope begun and disposed costs while a context is pushed.
    private static (long Fewest, string Samples) InsideAContext(ILogger logger)
    {
        using var context = LogContextScope.PushContext(Request);
        return ScopeCost(logger);
    }

    // The same while another scope is begun: one ambient value either way.
    private static (long Fewest, string Samples) InsideAScope(ILogger logger)
    {
        using var outer = logger.BeginScope(Scope);
        return ScopeCost(logger);
    }

    private static (long Fewest, string Samples) ScopeCost(ILogger logger)
        => ThreadAllocations.Fewest(() => logger.BeginScope(Scope)!.Dispose());

    private static PragmaticMemoryProvider Memory() => new("memory", PragmaticMemoryConfiguration.ForTesting());

    private static ILoggerFactory Factory(ILoggerProvider provider)
        => LoggerFactory.Create(builder => builder.AddProvider(provider));
}
