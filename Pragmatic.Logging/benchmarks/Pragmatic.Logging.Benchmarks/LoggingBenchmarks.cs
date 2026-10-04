using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Pragmatic.Logging.Benchmarks.Comparison;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using Serilog.Extensions.Logging;
using ZLogger;

namespace Pragmatic.Logging.Benchmarks;

/// <summary>
///     Pragmatic.Logging against Serilog, NLog and ZLogger, each writing into a sink that consumes the
///     event: the message rendered, every structured property read.
/// </summary>
/// <remarks>
///     <para>
///         Every library is called through <c>Microsoft.Extensions.Logging</c>, as an application calls
///         it, and every sink does the same work with what it receives (<see cref="EventConsumer" />).
///         <see cref="Setup" /> checks that before timing anything: one call per scenario through each
///         library must leave the same message, the same property set and the same exception, or the run
///         stops. A sink that skipped the work would be measuring something else.
///     </para>
///     <para>
///         Pragmatic is the baseline of every category, so the Ratio column reads as "how this library
///         compares with Pragmatic doing the same thing".
///     </para>
/// </remarks>
[Config(typeof(LoggingBenchmarkConfig))]
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class LoggingBenchmarks
{
    private const int HighVolumeCalls = 1000;

    private const string SimpleTemplate = "Order {OrderId} processed for user {UserId}";
    private const string StructuredTemplate = "Order processed with amount {Amount} at {Timestamp}";
    private const string ErrorTemplate = "Error processing order {OrderId} for user {UserId}";

    private static readonly Exception Failure = new InvalidOperationException("The order could not be processed");

    private readonly int _orderId = 67890;
    private readonly string _userId = "user12345";
    private readonly decimal _amount = 199.99m;
    private readonly DateTime _timestamp = new(2026, 10, 4, 12, 30, 0, DateTimeKind.Utc);
    private readonly string _correlationId = "corr-7f3a";
    private readonly string _requestId = "req-0042";

    private readonly Dictionary<string, object?> _scope = new()
    {
        ["SessionId"] = "session_abc123",
        ["RequestPath"] = "/api/orders/create",
        ["Duration"] = 1500.0,
        ["Success"] = true,
    };

    private readonly EventConsumer _pragmaticSink = new();
    private readonly EventConsumer _serilogSink = new();
    private readonly EventConsumer _nlogSink = new();
    private readonly EventConsumer _zloggerSink = new();

    private ILogger _pragmatic = null!;
    private ILogger _pragmaticProduction = null!;
    private ILogger _serilog = null!;
    private ILogger _nlog = null!;
    private ILogger _zlogger = null!;

    private readonly List<IDisposable> _owned = [];

    [GlobalSetup]
    public void Setup()
    {
        // Every library formats with the current culture somewhere; one culture for all of them.
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        _pragmatic = Pragmatic(PragmaticNullConfiguration.ForStructuredBenchmarking());
        _pragmaticProduction = Pragmatic(PragmaticNullConfiguration.ForProductionBenchmarking());
        _serilog = Serilog();
        _nlog = NLog();
        _zlogger = ZLogger();

        VerifySameWork();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var owned in _owned)
            owned.Dispose();
        global::NLog.LogManager.Shutdown();
    }

    // ── Simple: logger.LogInformation(template, args) ──

    [Benchmark(Baseline = true), BenchmarkCategory("Simple")]
    public void Pragmatic_Simple() => _pragmatic.LogInformation(SimpleTemplate, _orderId, _userId);

    [Benchmark, BenchmarkCategory("Simple")]
    public void Serilog_Simple() => _serilog.LogInformation(SimpleTemplate, _orderId, _userId);

    [Benchmark, BenchmarkCategory("Simple")]
    public void NLog_Simple() => _nlog.LogInformation(SimpleTemplate, _orderId, _userId);

    [Benchmark, BenchmarkCategory("Simple")]
    public void ZLogger_Simple() => _zlogger.LogInformation(SimpleTemplate, _orderId, _userId);

    // ── SourceGenerated: a [LoggerMessage] call site ──

    [Benchmark(Baseline = true), BenchmarkCategory("SourceGenerated")]
    public void Pragmatic_SourceGenerated() => BenchmarkLog.OrderProcessed(_pragmatic, _orderId, _userId);

    [Benchmark, BenchmarkCategory("SourceGenerated")]
    public void Serilog_SourceGenerated() => BenchmarkLog.OrderProcessed(_serilog, _orderId, _userId);

    [Benchmark, BenchmarkCategory("SourceGenerated")]
    public void NLog_SourceGenerated() => BenchmarkLog.OrderProcessed(_nlog, _orderId, _userId);

    [Benchmark, BenchmarkCategory("SourceGenerated")]
    public void ZLogger_SourceGenerated() => BenchmarkLog.OrderProcessed(_zlogger, _orderId, _userId);

    // ── Structured: a scope with four properties, and a call with two ──

    [Benchmark(Baseline = true), BenchmarkCategory("Structured")]
    public void Pragmatic_Structured() => Structured(_pragmatic);

    [Benchmark, BenchmarkCategory("Structured")]
    public void Serilog_Structured() => Structured(_serilog);

    [Benchmark, BenchmarkCategory("Structured")]
    public void NLog_Structured() => Structured(_nlog);

    [Benchmark, BenchmarkCategory("Structured")]
    public void ZLogger_Structured() => Structured(_zlogger);

    // ── Exception ──

    [Benchmark(Baseline = true), BenchmarkCategory("Exception")]
    public void Pragmatic_Exception() => _pragmatic.LogError(Failure, ErrorTemplate, _orderId, _userId);

    [Benchmark, BenchmarkCategory("Exception")]
    public void Serilog_Exception() => _serilog.LogError(Failure, ErrorTemplate, _orderId, _userId);

    [Benchmark, BenchmarkCategory("Exception")]
    public void NLog_Exception() => _nlog.LogError(Failure, ErrorTemplate, _orderId, _userId);

    [Benchmark, BenchmarkCategory("Exception")]
    public void ZLogger_Exception() => _zlogger.LogError(Failure, ErrorTemplate, _orderId, _userId);

    // ── HighVolume: the simple call a thousand times, reported per call ──

    [Benchmark(Baseline = true, OperationsPerInvoke = HighVolumeCalls), BenchmarkCategory("HighVolume")]
    public void Pragmatic_HighVolume() => HighVolume(_pragmatic);

    [Benchmark(OperationsPerInvoke = HighVolumeCalls), BenchmarkCategory("HighVolume")]
    public void Serilog_HighVolume() => HighVolume(_serilog);

    [Benchmark(OperationsPerInvoke = HighVolumeCalls), BenchmarkCategory("HighVolume")]
    public void NLog_HighVolume() => HighVolume(_nlog);

    [Benchmark(OperationsPerInvoke = HighVolumeCalls), BenchmarkCategory("HighVolume")]
    public void ZLogger_HighVolume() => HighVolume(_zlogger);

    // ── Production: two request context properties through each library's own mechanism, plus the scope ──

    [Benchmark(Baseline = true), BenchmarkCategory("Production")]
    public void Pragmatic_Production()
    {
        using var context = LogContextScope.PushContext();
        LogContextScope.Current!.SetProperty("CorrelationId", _correlationId);
        LogContextScope.Current.SetProperty("RequestId", _requestId);
        Structured(_pragmaticProduction);
    }

    [Benchmark, BenchmarkCategory("Production")]
    public void Serilog_Production()
    {
        using var correlation = global::Serilog.Context.LogContext.PushProperty("CorrelationId", _correlationId);
        using var request = global::Serilog.Context.LogContext.PushProperty("RequestId", _requestId);
        Structured(_serilog);
    }

    [Benchmark, BenchmarkCategory("Production")]
    public void NLog_Production()
    {
        using var correlation = global::NLog.ScopeContext.PushProperty("CorrelationId", _correlationId);
        using var request = global::NLog.ScopeContext.PushProperty("RequestId", _requestId);
        Structured(_nlog);
    }

    /// <summary>ZLogger has no ambient context of its own: the request context is a second MEL scope.</summary>
    [Benchmark, BenchmarkCategory("Production")]
    public void ZLogger_Production()
    {
        using var context = _zlogger.BeginScope(new KeyValuePair<string, object?>[]
        {
            new("CorrelationId", _correlationId),
            new("RequestId", _requestId),
        });
        Structured(_zlogger);
    }

    private void Structured(ILogger logger)
    {
        using var scope = logger.BeginScope(_scope);
        logger.LogInformation(StructuredTemplate, _amount, _timestamp);
    }

    private void HighVolume(ILogger logger)
    {
        for (var i = 0; i < HighVolumeCalls; i++)
            logger.LogInformation(SimpleTemplate, i, _userId);
    }

    // ── Setup ──

    private ILogger Pragmatic(PragmaticProviderConfiguration configuration)
    {
        configuration.MinimumLevel = LogLevel.Information;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLoggingBuilder(builder =>
            builder.AddProvider(_ => new PragmaticConsumingProvider("Consuming", configuration, _pragmaticSink)));
        return Owned(services.BuildServiceProvider()).GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
    }

    private ILogger Serilog()
    {
        var serilog = new global::Serilog.LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new SerilogConsumingSink(_serilogSink))
            .CreateLogger();
        return Owned(new SerilogLoggerFactory(serilog, dispose: true)).CreateLogger("Benchmark");
    }

    private ILogger NLog()
    {
        var config = new global::NLog.Config.LoggingConfiguration();
        var target = new NLogConsumingTarget(_nlogSink);
        config.AddRule(global::NLog.LogLevel.Info, global::NLog.LogLevel.Fatal, target);
        global::NLog.LogManager.Configuration = config;

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddNLog();
        });
        return Owned(services.BuildServiceProvider()).GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
    }

    private ILogger ZLogger()
    {
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
        return Owned(services.BuildServiceProvider()).GetRequiredService<ILoggerFactory>().CreateLogger("Benchmark");
    }

    private T Owned<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    // ── The equivalence check ──

    /// <summary>
    ///     Runs every scenario once through each library and throws unless the four sinks consumed the same
    ///     message, the same property set and the same exception.
    /// </summary>
    internal void VerifySameWork()
    {
        Compare("Simple", Pragmatic_Simple, Serilog_Simple, NLog_Simple, ZLogger_Simple);
        Compare("SourceGenerated", Pragmatic_SourceGenerated, Serilog_SourceGenerated, NLog_SourceGenerated, ZLogger_SourceGenerated);
        Compare("Structured", Pragmatic_Structured, Serilog_Structured, NLog_Structured, ZLogger_Structured);
        Compare("Exception", Pragmatic_Exception, Serilog_Exception, NLog_Exception, ZLogger_Exception);
        Compare("HighVolume", Pragmatic_HighVolume, Serilog_HighVolume, NLog_HighVolume, ZLogger_HighVolume);
        Compare("Production", Pragmatic_Production, Serilog_Production, NLog_Production, ZLogger_Production);
    }

    private void Compare(string scenario, Action pragmatic, Action serilog, Action nlog, Action zlogger)
    {
        var events = new (string Library, ConsumedEvent? Event)[]
        {
            ("Pragmatic", Consume(_pragmaticSink, pragmatic)),
            ("Serilog", Consume(_serilogSink, serilog)),
            ("NLog", Consume(_nlogSink, nlog)),
            ("ZLogger", Consume(_zloggerSink, zlogger)),
        };

        var reference = events[0];
        var differs = events.Any(e => e.Event is null) || events.Skip(1).Any(e => !e.Event!.SameAs(reference.Event!));
        if (!differs)
            return;

        throw new InvalidOperationException(
            $"The sinks did not consume the same event for '{scenario}', so the rows would not measure the same work:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, events.Select(e => $"  {e.Library,-10} {e.Event?.ToString() ?? "(nothing reached the sink)"}")));
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
}
