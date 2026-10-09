using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using ZLogger;

namespace Pragmatic.Logging.Benchmarks.Comparison;

/// <summary>
///     The classic path against the deferred one, the same consuming sink, the same calls as
///     <see cref="LoggingBenchmarks" />: what <see cref="LogEntry" /> and the pipeline around it cost, with ZLogger
///     in the same run.
/// </summary>
/// <remarks>Not part of <c>all</c>: it explains a number. <c>dotnet run -c Release -- classic-probe</c>.</remarks>
[Config(typeof(LoggingBenchmarkConfig))]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class ClassicProbeBenchmarks
{
    private const string SimpleTemplate = "Order {OrderId} processed for user {UserId}";
    private const string StructuredTemplate = "Order processed with amount {Amount} at {Timestamp}";
    private const string ErrorTemplate = "Error processing order {OrderId} for user {UserId}";

    private static readonly Exception Failure = new InvalidOperationException("The order could not be processed");

    private readonly int _orderId = 67890;
    private readonly string _userId = "user12345";
    private readonly decimal _amount = 199.99m;
    private readonly DateTime _timestamp = new(2026, 10, 4, 12, 30, 0, DateTimeKind.Utc);

    private readonly Dictionary<string, object?> _scope = new()
    {
        ["SessionId"] = "session_abc123",
        ["RequestPath"] = "/api/orders/create",
        ["Duration"] = 1500.0,
        ["Success"] = true,
    };

    private readonly EventConsumer _classicSink = new();
    private readonly EventConsumer _deferredSink = new();
    private readonly EventConsumer _zloggerSink = new();
    private readonly EventConsumer _productionSink = new();

    private readonly string _correlationId = "corr-7f3a";
    private readonly string _requestId = "req-0042";

    private ILogger _classic = null!;
    private ILogger _deferred = null!;
    private ILogger _production = null!;
    private ILogger _zlogger = null!;

    private readonly List<IDisposable> _owned = [];

    [GlobalSetup]
    public void Setup()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        _classic = Pragmatic(c => new PragmaticConsumingProvider("Classic", c, _classicSink));
        _deferred = Pragmatic(c => new PragmaticDeferredConsumingProvider("Deferred", c, _deferredSink));
        _production = Pragmatic(
            PragmaticNullConfiguration.ForProductionBenchmarking(), c => new PragmaticConsumingProvider("Production", c, _productionSink));

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddZLoggerLogProcessor(options =>
            {
                options.IncludeScopes = true;
                return new ZLoggerConsumingProcessor(_zloggerSink);
            });
        });
        _zlogger = Owned(services.BuildServiceProvider()).GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");

        Same("Simple", () => Simple(_classic), () => Simple(_deferred), () => Simple(_zlogger));
        Same("Structured", () => Structured(_classic), () => Structured(_deferred), () => Structured(_zlogger));
        Same("Exception", () => Error(_classic), () => Error(_deferred), () => Error(_zlogger));

        var production = Consume(_productionSink, Classic_Production);
        var zloggerProduction = Consume(_zloggerSink, ZLogger_Production);
        if (production is null || zloggerProduction is null || !production.SameAs(zloggerProduction))
            throw new InvalidOperationException(
                $"'Production': the sinks did not consume the same event:{Environment.NewLine}  {production}{Environment.NewLine}  {zloggerProduction}");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var owned in _owned)
            owned.Dispose();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Simple")]
    public void Classic_Simple() => Simple(_classic);

    [Benchmark, BenchmarkCategory("Simple")]
    public void Deferred_Simple() => Simple(_deferred);

    [Benchmark, BenchmarkCategory("Simple")]
    public void ZLogger_Simple() => Simple(_zlogger);

    [Benchmark(Baseline = true), BenchmarkCategory("Structured")]
    public void Classic_Structured() => Structured(_classic);

    [Benchmark, BenchmarkCategory("Structured")]
    public void Deferred_Structured() => Structured(_deferred);

    [Benchmark, BenchmarkCategory("Structured")]
    public void ZLogger_Structured() => Structured(_zlogger);

    // Structured, taken apart: the scope alone, the call alone.

    [Benchmark, BenchmarkCategory("Structured")]
    public void Classic_Structured_ScopeOnly() => ScopeOnly(_classic);

    [Benchmark, BenchmarkCategory("Structured")]
    public void Classic_Structured_CallOnly() => StructuredCall(_classic);

    [Benchmark, BenchmarkCategory("Structured")]
    public void ZLogger_Structured_ScopeOnly() => ScopeOnly(_zlogger);

    [Benchmark, BenchmarkCategory("Structured")]
    public void ZLogger_Structured_CallOnly() => StructuredCall(_zlogger);

    // Production, taken apart: the request context alone, the call without it.

    [Benchmark(Baseline = true), BenchmarkCategory("Production")]
    public void Classic_Production()
    {
        using var context = PragmaticContext();
        Structured(_production);
    }

    [Benchmark, BenchmarkCategory("Production")]
    public void Classic_Production_ContextOnly()
    {
        using var context = PragmaticContext();
    }

    [Benchmark, BenchmarkCategory("Production")]
    public void Classic_Production_NoContext() => Structured(_production);

    // The context and the scope together, no call: against ContextOnly and ScopeOnly, what holding both costs.
    [Benchmark, BenchmarkCategory("Production")]
    public void Classic_Production_ContextAndScope()
    {
        using var context = PragmaticContext();
        ScopeOnly(_production);
    }

    [Benchmark, BenchmarkCategory("Production")]
    public void Classic_Production_ScopeOnly() => ScopeOnly(_production);

    [Benchmark, BenchmarkCategory("Production")]
    public void ZLogger_Production_ContextAndScope()
    {
        using var context = ZLoggerContext();
        ScopeOnly(_zlogger);
    }

    [Benchmark, BenchmarkCategory("Production")]
    public void ZLogger_Production()
    {
        using var context = ZLoggerContext();
        Structured(_zlogger);
    }

    [Benchmark, BenchmarkCategory("Production")]
    public void ZLogger_Production_ContextOnly()
    {
        using var context = ZLoggerContext();
    }

    [Benchmark(Baseline = true), BenchmarkCategory("Exception")]
    public void Classic_Exception() => Error(_classic);

    [Benchmark, BenchmarkCategory("Exception")]
    public void Deferred_Exception() => Error(_deferred);

    [Benchmark, BenchmarkCategory("Exception")]
    public void ZLogger_Exception() => Error(_zlogger);

    private void Simple(ILogger logger) => logger.LogInformation(SimpleTemplate, _orderId, _userId);

    private void Structured(ILogger logger)
    {
        using var scope = logger.BeginScope(_scope);
        StructuredCall(logger);
    }

    private void StructuredCall(ILogger logger) => logger.LogInformation(StructuredTemplate, _amount, _timestamp);

    private void ScopeOnly(ILogger logger)
    {
        using var scope = logger.BeginScope(_scope);
    }

    // As LoggingBenchmarks pushes them: each library's own request context mechanism.
    private IDisposable PragmaticContext()
    {
        var context = LogContextScope.PushContext();
        LogContextScope.Current!.SetProperty("CorrelationId", _correlationId);
        LogContextScope.Current.SetProperty("RequestId", _requestId);
        return context;
    }

    private IDisposable ZLoggerContext() => _zlogger.BeginScope(new KeyValuePair<string, object?>[]
    {
        new("CorrelationId", _correlationId),
        new("RequestId", _requestId),
    })!;

    private void Error(ILogger logger) => logger.LogError(Failure, ErrorTemplate, _orderId, _userId);

    private ILogger Pragmatic(Func<PragmaticProviderConfiguration, PragmaticLoggerProviderBase> provider)
        => Pragmatic(PragmaticNullConfiguration.ForStructuredBenchmarking(), provider);

    private ILogger Pragmatic(
        PragmaticProviderConfiguration configuration, Func<PragmaticProviderConfiguration, PragmaticLoggerProviderBase> provider)
    {
        configuration.MinimumLevel = LogLevel.Information;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLoggingBuilder(builder => builder.AddProvider(_ => provider(configuration)));
        return Owned(services.BuildServiceProvider()).GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
    }

    // The three sinks must consume the same event, or the rows measure different work.
    private void Same(string scenario, Action classic, Action deferred, Action zlogger)
    {
        var events = new[] { Consume(_classicSink, classic), Consume(_deferredSink, deferred), Consume(_zloggerSink, zlogger) };
        if (events.All(e => e is not null && e.SameAs(events[0]!)))
            return;

        throw new InvalidOperationException(
            $"'{scenario}': the sinks did not consume the same event:{Environment.NewLine}"
            + string.Join(Environment.NewLine, events.Select(e => "  " + (e?.ToString() ?? "(nothing)"))));
    }

    private static ConsumedEvent? Consume(EventConsumer sink, Action call)
    {
        sink.Capture = true;
        try
        {
            call();
            return sink.TakeLast();
        }
        finally
        {
            sink.Capture = false;
        }
    }

    private T Owned<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }
}
