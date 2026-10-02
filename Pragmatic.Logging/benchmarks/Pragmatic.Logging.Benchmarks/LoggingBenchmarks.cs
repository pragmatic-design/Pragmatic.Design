using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NLog.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace Pragmatic.Logging.Benchmarks;

/// <summary>
/// Null sink for Serilog benchmarking - discards all log events without I/O overhead.
/// </summary>
internal sealed class NullSink : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        // Intentionally do nothing - this measures pure logging overhead
    }
}

/// <summary>
/// Comprehensive benchmarks comparing Pragmatic.Logging with popular logging libraries.
/// Tests multiple scenarios with accurate performance measurement:
/// 
/// BENCHMARK CATEGORIES:
/// - Simple: Basic logging performance (all libraries use null sinks/providers for fair comparison)
/// - Structured: Structured logging with scope properties
/// - HighVolume: Bulk logging operations (1000 messages per benchmark)
/// - Filtering: Expression DSL filter evaluation performance  
/// - ErrorHandling: Exception logging performance (uses pre-created exceptions)
/// - Memory: Zero-allocation pattern verification
/// 
/// PRAGMATIC.LOGGING CONFIGURATIONS TESTED:
/// - Minimal: PragmaticNullConfiguration.ForBenchmarking() - Ultra-fast baseline
/// - Structured: ForStructuredBenchmarking() - With structured properties enabled
/// - Production: ForProductionBenchmarking() - Production-like settings with full processing
/// 
/// All providers are configured to discard output to measure pure logging overhead without I/O.
/// </summary>
[Config(typeof(LoggingBenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[RankColumn]
public class LoggingBenchmarks
{
    // Loggers for comparison
    private ILogger<LoggingBenchmarks> _pragmaticLogger = null!;
    private ILogger<LoggingBenchmarks> _pragmaticStructuredLogger = null!;
    private ILogger<LoggingBenchmarks> _pragmaticProductionLogger = null!;
    private ILogger<LoggingBenchmarks> _serilogLogger = null!;
    private ILogger<LoggingBenchmarks> _nlogLogger = null!;
    private ILogger<LoggingBenchmarks> _nullLogger = null!;

    // Test data
    private readonly string _userId = "user12345";
    private readonly int _orderId = 67890;
    private readonly decimal _amount = 199.99m;
    private readonly DateTime _timestamp = DateTime.UtcNow;
    private readonly Dictionary<string, object?> _properties = new()
    {
        ["UserId"] = "user12345",
        ["SessionId"] = "session_abc123",
        ["RequestPath"] = "/api/orders/create",
        ["Duration"] = 1500.0,
        ["Success"] = true
    };

    // Pre-created exception for benchmarking to avoid allocation overhead
    private static readonly Exception _benchmarkException = new InvalidOperationException("Pre-created benchmark exception to measure pure logging performance without allocation overhead");

    // Expression DSL filter for testing
    private FilterExpression _complexFilter = null!;
    private LogEntry _testLogEntry = null!;
    private LogFilterContext _filterContext = null!;

    [GlobalSetup]
    public void Setup()
    {
        SetupPragmaticLogging();
        SetupPragmaticStructuredLogging();
        SetupPragmaticProductionLogging();
        SetupSerilog();
        SetupNLog();
        SetupNullLogger();
        SetupExpressionDslFiltering();
    }

    private void SetupPragmaticLogging()
    {
        var services = new ServiceCollection();

        // Add standard Microsoft.Extensions.Logging first
        services.AddLogging();

        // Add Pragmatic Logging with NullProvider for fair performance comparison  
        services.AddPragmaticLoggingBuilder(builder =>
        {
            // Use NullProvider to measure pure logging overhead without I/O
            builder.AddProvider(serviceProvider =>
                new PragmaticNullProvider("Benchmark", PragmaticNullConfiguration.ForBenchmarking()));
        });

        var serviceProvider = services.BuildServiceProvider();
        _pragmaticLogger = serviceProvider.GetRequiredService<ILogger<LoggingBenchmarks>>();
    }

    private void SetupPragmaticStructuredLogging()
    {
        var services = new ServiceCollection();

        // Add standard Microsoft.Extensions.Logging first
        services.AddLogging();

        // Add Pragmatic Logging with structured properties enabled
        services.AddPragmaticLoggingBuilder(builder =>
        {
            builder.AddProvider(serviceProvider =>
                new PragmaticNullProvider("StructuredBenchmark", PragmaticNullConfiguration.ForStructuredBenchmarking()));
        });

        var serviceProvider = services.BuildServiceProvider();
        _pragmaticStructuredLogger = serviceProvider.GetRequiredService<ILogger<LoggingBenchmarks>>();
    }

    private void SetupPragmaticProductionLogging()
    {
        var services = new ServiceCollection();

        // Add standard Microsoft.Extensions.Logging first
        services.AddLogging();

        // Add Pragmatic Logging with production-like configuration
        services.AddPragmaticLoggingBuilder(builder =>
        {
            builder.AddProvider(serviceProvider =>
                new PragmaticNullProvider("ProductionBenchmark", PragmaticNullConfiguration.ForProductionBenchmarking()));
        });

        var serviceProvider = services.BuildServiceProvider();
        _pragmaticProductionLogger = serviceProvider.GetRequiredService<ILogger<LoggingBenchmarks>>();
    }

    private void SetupSerilog()
    {
        // Use a null sink to measure pure logging overhead without I/O
        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Sink(new NullSink())
            .CreateLogger();

        var loggerFactory = new SerilogLoggerFactory(serilogLogger);
        _serilogLogger = loggerFactory.CreateLogger<LoggingBenchmarks>();
    }

    private void SetupNLog()
    {
        // Configure NLog with null target for fair performance comparison
        var config = new NLog.Config.LoggingConfiguration();
        var nullTarget = new NLog.Targets.NullTarget("null");
        config.AddTarget(nullTarget);
        config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, nullTarget);
        NLog.LogManager.Configuration = config;

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddNLog();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var serviceProvider = services.BuildServiceProvider();
        _nlogLogger = serviceProvider.GetRequiredService<ILogger<LoggingBenchmarks>>();
    }

    private void SetupNullLogger()
    {
        _nullLogger = NullLogger<LoggingBenchmarks>.Instance;
    }

    private void SetupExpressionDslFiltering()
    {
        // Create a complex filter expression for performance testing
        _complexFilter = f => f.Group(ctx =>
            ((ctx.Error() | ctx.Critical()) & ctx.Production() & !ctx.HealthCheck()) |
            (ctx.BusinessCritical() & ctx.HasStructuredProperties()) |
            (ctx.SecurityEvent() & ctx.Level(LogLevel.Warning)));

        _testLogEntry = new LogEntry
        {
            LogLevel = LogLevel.Information,
            Category = "MyApp.Controllers.OrderController",
            Message = "Order processing completed",
            Properties = _properties
        };

        _filterContext = new LogFilterContext();
    }

    #region Simple Logging Benchmarks

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Simple")]
    public void NullLogger_SimpleLog()
    {
        _nullLogger.LogInformation("Order {OrderId} processed for user {UserId}", _orderId, _userId);
    }

    [Benchmark]
    [BenchmarkCategory("Simple")]
    public void PragmaticLogging_SimpleLog()
    {
        _pragmaticLogger.LogInformation("Order {OrderId} processed for user {UserId}", _orderId, _userId);
    }

    [Benchmark]
    [BenchmarkCategory("Simple")]
    public void Serilog_SimpleLog()
    {
        _serilogLogger.LogInformation("Order {OrderId} processed for user {UserId}", _orderId, _userId);
    }

    [Benchmark]
    [BenchmarkCategory("Simple")]
    public void NLog_SimpleLog()
    {
        _nlogLogger.LogInformation("Order {OrderId} processed for user {UserId}", _orderId, _userId);
    }

    #endregion

    #region Source-Generated Call Sites ([LoggerMessage], the sanctioned hot-path pattern)

    // All three libraries are invoked through the same Microsoft [LoggerMessage]-generated
    // method (zero-boxing struct state, cached delegate) — the pattern Pragmatic recommends
    // for hot paths. This measures each provider pipeline under the modern call-site.

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SourceGenerated")]
    public void NullLogger_SourceGenLog()
    {
        BenchmarkLog.OrderProcessed(_nullLogger, _orderId, _userId);
    }

    [Benchmark]
    [BenchmarkCategory("SourceGenerated")]
    public void PragmaticLogging_SourceGenLog()
    {
        BenchmarkLog.OrderProcessed(_pragmaticLogger, _orderId, _userId);
    }

    [Benchmark]
    [BenchmarkCategory("SourceGenerated")]
    public void Serilog_SourceGenLog()
    {
        BenchmarkLog.OrderProcessed(_serilogLogger, _orderId, _userId);
    }

    [Benchmark]
    [BenchmarkCategory("SourceGenerated")]
    public void NLog_SourceGenLog()
    {
        BenchmarkLog.OrderProcessed(_nlogLogger, _orderId, _userId);
    }

    #endregion

    #region Structured Logging Benchmarks

    [Benchmark]
    [BenchmarkCategory("Structured")]
    public void PragmaticLogging_StructuredLog()
    {
        using var scope = _pragmaticStructuredLogger.BeginScope(_properties);
        _pragmaticStructuredLogger.LogInformation("Order processed with amount {Amount:C} at {Timestamp}",
            _amount, _timestamp);
    }

    [Benchmark]
    [BenchmarkCategory("Structured")]
    public void PragmaticLogging_ProductionStructuredLog()
    {
        using var scope = _pragmaticProductionLogger.BeginScope(_properties);
        _pragmaticProductionLogger.LogInformation("Order processed with amount {Amount:C} at {Timestamp}",
            _amount, _timestamp);
    }

    [Benchmark]
    [BenchmarkCategory("Structured")]
    public void Serilog_StructuredLog()
    {
        using var scope = _serilogLogger.BeginScope(_properties);
        _serilogLogger.LogInformation("Order processed with amount {Amount:C} at {Timestamp}",
            _amount, _timestamp);
    }

    [Benchmark]
    [BenchmarkCategory("Structured")]
    public void NLog_StructuredLog()
    {
        using var scope = _nlogLogger.BeginScope(_properties);
        _nlogLogger.LogInformation("Order processed with amount {Amount:C} at {Timestamp}",
            _amount, _timestamp);
    }

    #endregion

    #region High-Volume Logging Benchmarks

    [Benchmark]
    [BenchmarkCategory("HighVolume")]
    public void PragmaticLogging_HighVolume()
    {
        for (int i = 0; i < 1000; i++)
        {
            _pragmaticLogger.LogInformation("Processing item {ItemId} for user {UserId}", i, _userId);
        }
    }

    [Benchmark]
    [BenchmarkCategory("HighVolume")]
    public void Serilog_HighVolume()
    {
        for (int i = 0; i < 1000; i++)
        {
            _serilogLogger.LogInformation("Processing item {ItemId} for user {UserId}", i, _userId);
        }
    }

    [Benchmark]
    [BenchmarkCategory("HighVolume")]
    public void NLog_HighVolume()
    {
        for (int i = 0; i < 1000; i++)
        {
            _nlogLogger.LogInformation("Processing item {ItemId} for user {UserId}", i, _userId);
        }
    }

    #endregion

    #region Expression DSL Filtering Benchmarks

    [Benchmark]
    [BenchmarkCategory("Filtering")]
    public void PragmaticLogging_ExpressionDslSimple()
    {
        FilterExpression simpleFilter = f => f.Level(LogLevel.Information);
        FilterExpressionEvaluator.Evaluate(simpleFilter, _testLogEntry, _filterContext);
    }

    [Benchmark]
    [BenchmarkCategory("Filtering")]
    public void PragmaticLogging_ExpressionDslComplex()
    {
        FilterExpressionEvaluator.Evaluate(_complexFilter, _testLogEntry, _filterContext);
    }

    [Benchmark]
    [BenchmarkCategory("Filtering")]
    public void PragmaticLogging_ExpressionDslBatch()
    {
        for (int i = 0; i < 100; i++)
        {
            FilterExpressionEvaluator.Evaluate(_complexFilter, _testLogEntry, _filterContext);
        }
    }

    #endregion

    #region Error Handling Benchmarks

    [Benchmark]
    [BenchmarkCategory("ErrorHandling")]
    public void PragmaticLogging_ExceptionLogging()
    {
        // Use pre-created exception to measure pure logging performance (minimal configuration)
        _pragmaticLogger.LogError(_benchmarkException, "Error processing order {OrderId} for user {UserId}", _orderId, _userId);
    }

    [Benchmark]
    [BenchmarkCategory("ErrorHandling")]
    public void PragmaticLogging_ProductionExceptionLogging()
    {
        // Use pre-created exception to measure production-like configuration performance
        _pragmaticProductionLogger.LogError(_benchmarkException, "Error processing order {OrderId} for user {UserId}", _orderId, _userId);
    }

    [Benchmark]
    [BenchmarkCategory("ErrorHandling")]
    public void Serilog_ExceptionLogging()
    {
        // Use pre-created exception to measure pure logging performance
        _serilogLogger.LogError(_benchmarkException, "Error processing order {OrderId} for user {UserId}", _orderId, _userId);
    }

    [Benchmark]
    [BenchmarkCategory("ErrorHandling")]
    public void NLog_ExceptionLogging()
    {
        // Use pre-created exception to measure pure logging performance
        _nlogLogger.LogError(_benchmarkException, "Error processing order {OrderId} for user {UserId}", _orderId, _userId);
    }

    #endregion

    #region Memory Allocation Benchmarks

    [Benchmark]
    [BenchmarkCategory("Memory")]
    public void PragmaticLogging_ZeroAllocation()
    {
        // Test zero-allocation patterns
        if (_pragmaticLogger.IsEnabled(LogLevel.Information))
        {
            _pragmaticLogger.LogInformation("User {UserId} completed action", _userId);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Memory")]
    public void Serilog_StandardAllocation()
    {
        if (_serilogLogger.IsEnabled(LogLevel.Information))
        {
            _serilogLogger.LogInformation("User {UserId} completed action", _userId);
        }
    }

    [Benchmark]
    [BenchmarkCategory("Memory")]
    public void NLog_StandardAllocation()
    {
        if (_nlogLogger.IsEnabled(LogLevel.Information))
        {
            _nlogLogger.LogInformation("User {UserId} completed action", _userId);
        }
    }

    #endregion

    [GlobalCleanup]
    public void Cleanup()
    {
        // Print Expression DSL statistics
        var stats = FilterExpressionEvaluator.GetStatistics();
        Console.WriteLine($"\n📊 Pragmatic.Logging Benchmark Summary:");
        Console.WriteLine($"   Expression DSL Performance:");
        Console.WriteLine($"     - Total Evaluations: {stats.TotalEvaluations:N0}");
        Console.WriteLine($"     - Cache Hits: {stats.CacheHits:N0} ({stats.CacheHitRatio:P2})");
        Console.WriteLine($"     - Cached Expressions: {stats.CachedExpressions:N0}");

        // Print configuration details for benchmarked scenarios
        Console.WriteLine($"\n   Configuration Details:");
        Console.WriteLine($"     - Minimal Config: Zero-allocation, no structured properties, no context");
        Console.WriteLine($"     - Structured Config: Structured properties enabled, minimal context");
        Console.WriteLine($"     - Production Config: Full processing, context enrichment, batching enabled");
        Console.WriteLine($"     - All providers use null sinks for fair I/O-free comparison");

        Console.WriteLine($"\n   All benchmarks measure pure logging performance without I/O overhead.");
    }
}

/// <summary>
/// Custom benchmark configuration optimized for logging performance testing.
/// </summary>
public class LoggingBenchmarkConfig : ManualConfig
{
    public LoggingBenchmarkConfig()
    {
        // Realistic benchmark configuration for production logging evaluation
        AddJob(Job.Default
            .WithWarmupCount(5)       // Sufficient warmup for JIT optimization
            .WithIterationCount(15)   // Increased from 7 for lower CV and stable results
            .WithInvocationCount(1000) // Reduced from 10000 to reasonable level
            .WithUnrollFactor(1));    // Keep single invocation for accurate memory measurement

        // Essential performance columns
        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Median);
        AddColumn(BaselineRatioColumn.RatioMean);

        // Memory analysis columns
        AddColumn(StatisticColumn.Min);
        AddColumn(StatisticColumn.Max);
        AddColumn(StatisticColumn.Q1);
        AddColumn(StatisticColumn.Q3);

        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));
        WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend));

        // Add exporters to save results in multiple formats
        AddExporter(HtmlExporter.Default);
        AddExporter(MarkdownExporter.Default);

        // Add validation to ensure results are meaningful
        AddValidator(BenchmarkDotNet.Validators.BaselineValidator.FailOnError);
        AddValidator(BenchmarkDotNet.Validators.ExecutionValidator.FailOnError);
    }
}
/// <summary>
/// Microsoft [LoggerMessage] source-generated call sites shared by the SourceGenerated
/// benchmark category — the hot-path pattern Pragmatic.Logging recommends.
/// </summary>
internal static partial class BenchmarkLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} processed for user {UserId}")]
    public static partial void OrderProcessed(Microsoft.Extensions.Logging.ILogger logger, int orderId, string userId);
}
