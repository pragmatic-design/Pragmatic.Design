using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Providers;

/// <summary>
///     A scope opened through the composed logger factory is the Pragmatic provider's own scope when it is the
///     only one: no composite, no list, the 112 bytes they cost on every <c>BeginScope</c>. With a standard
///     provider that takes scopes too, the composite is there and closing it closes both.
/// </summary>
public class AnOnlyScopeIsReturnedAsItIsTests
{
    [Fact]
    public void WithoutAStandardProvider_TheScopeIsThePragmaticOne_AndClosingItEndsIt()
    {
        var sink = new ScopeReadingProvider();
        using var services = Build(sink, standard: null);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Orders");

        var scope = logger.BeginScope(new Dictionary<string, object?> { ["SessionId"] = "s-1" });
        logger.LogInformation("inside");
        scope!.Dispose();
        logger.LogInformation("outside");

        scope.Should().NotBeOfType<CompositeDisposable>();
        sink.ScopeCounts.Should().Equal(1, 0);
    }

    [Fact]
    public void WithAStandardProviderTakingScopes_BothAreClosed()
    {
        var sink = new ScopeReadingProvider();
        var standard = new ScopeTakingProvider();
        using var services = Build(sink, standard);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Orders");

        var scope = logger.BeginScope(new Dictionary<string, object?> { ["SessionId"] = "s-1" });
        logger.LogInformation("inside");
        scope!.Dispose();
        logger.LogInformation("outside");

        scope.Should().BeOfType<CompositeDisposable>();
        standard.Open.Should().Be(0);
        standard.Opened.Should().Be(1);
        sink.ScopeCounts.Should().Equal(1, 0);
    }

    private static ServiceProvider Build(ScopeReadingProvider sink, ILoggerProvider? standard)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (standard != null)
                builder.AddProvider(standard);
        });
        services.AddPragmaticLoggingBuilder(builder => builder.AddProvider(_ => sink));
        return services.BuildServiceProvider();
    }

    private sealed class ScopeReadingProvider() : PragmaticLoggerProviderBase("scopes", Lean())
    {
        public List<int> ScopeCounts { get; } = [];

        protected override void WriteLogCore(LogEvent logEvent) => ScopeCounts.Add(logEvent.Scopes.Count);

        private static PragmaticProviderConfiguration Lean()
        {
            var configuration = PragmaticNullConfiguration.ForStructuredBenchmarking();
            configuration.MinimumLevel = LogLevel.Information;
            return configuration;
        }
    }

    // A standard provider whose loggers take scopes, counting how many are open.
    private sealed class ScopeTakingProvider : ILoggerProvider
    {
        public int Open;
        public int Opened;

        public ILogger CreateLogger(string categoryName) => new ScopeTakingLogger(this);

        public void Dispose()
        {
        }

        private sealed class ScopeTakingLogger(ScopeTakingProvider owner) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                owner.Open++;
                owner.Opened++;
                return new Closing(owner);
            }

            public bool IsEnabled(LogLevel logLevel) => false;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
            }
        }

        private sealed class Closing(ScopeTakingProvider owner) : IDisposable
        {
            public void Dispose() => owner.Open--;
        }
    }
}
