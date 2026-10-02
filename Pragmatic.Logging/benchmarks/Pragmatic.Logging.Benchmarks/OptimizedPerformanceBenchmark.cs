using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;
using Pragmatic.Logging.ZeroAllocation;

namespace Pragmatic.Logging.Benchmarks;

/// <summary>
/// Benchmark to measure the performance improvements after optimization.
/// Compares old vs new implementations to validate optimization effectiveness.
/// </summary>
[Config(typeof(DefaultExportConfig))]
[MemoryDiagnoser]
[DisassemblyDiagnoser]
public class OptimizedPerformanceBenchmark
{
    private ILogger<OptimizedPerformanceBenchmark> _optimizedLogger = null!;
    private FilterExpression _simpleFilter = null!;
    private FilterExpression _complexFilter = null!;
    private LogEntry _testLogEntry = null!;
    private LogFilterContext _filterContext = null!;

    // Test data
    private readonly string _userId = "user12345";
    private readonly int _orderId = 67890;
    private readonly decimal _amount = 199.99m;
    private readonly Dictionary<string, object?> _properties = new()
    {
        ["UserId"] = "user12345",
        ["RequestId"] = "req_abc123",
        ["CorrelationId"] = "corr_xyz789"
    };

    [GlobalSetup]
    public void Setup()
    {
        SetupOptimizedLogger();
        SetupFilterTests();
    }

    private void SetupOptimizedLogger()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLoggingBuilder(builder =>
        {
            builder.AddProvider(serviceProvider =>
                new PragmaticNullProvider("OptimizedBenchmark", PragmaticNullConfiguration.ForBenchmarking()));
        });

        var serviceProvider = services.BuildServiceProvider();
        _optimizedLogger = serviceProvider.GetRequiredService<ILogger<OptimizedPerformanceBenchmark>>();
    }

    private void SetupFilterTests()
    {
        _simpleFilter = f => f.Level(LogLevel.Information);
        _complexFilter = f => f.Group(ctx =>
            ((ctx.Error() | ctx.Critical()) & ctx.Production() & !ctx.HealthCheck()) |
            (ctx.BusinessCritical() & ctx.HasStructuredProperties()));

        _testLogEntry = new LogEntry
        {
            LogLevel = LogLevel.Information,
            Category = "OptimizedBenchmark",
            Message = "Test message",
            Properties = _properties
        };

        _filterContext = new LogFilterContext();
    }

    #region Expression DSL Performance Tests

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ExpressionDSL")]
    public bool OriginalExpressionEvaluation()
    {
        // Original implementation
        return FilterExpressionEvaluator.Evaluate(_simpleFilter, _testLogEntry, _filterContext);
    }

    [Benchmark]
    [BenchmarkCategory("ExpressionDSL")]
    public bool OptimizedExpressionEvaluation()
    {
        // New optimized implementation with compilation
        var compiled = FilterExpressionCompiler.Compile(_simpleFilter);
        return compiled.Evaluate(_testLogEntry, _filterContext);
    }

    [Benchmark]
    [BenchmarkCategory("ExpressionDSL")]
    public bool OptimizedComplexExpressionEvaluation()
    {
        // Test complex expression with optimization
        var compiled = FilterExpressionCompiler.Compile(_complexFilter);
        return compiled.Evaluate(_testLogEntry, _filterContext);
    }

    #endregion

    #region Zero-Allocation Formatting Tests

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Formatting")]
    public string OriginalStringInterpolation()
    {
        return $"Order {_orderId} for user {_userId} with amount {_amount:C}";
    }

    [Benchmark]
    [BenchmarkCategory("Formatting")]
    public string OptimizedZeroAllocFormatting()
    {
        return ZeroAllocMessageFormatter.FormatFast("Order {OrderId} for user {UserId} with amount {Amount:C}".AsSpan(),
            _orderId, _userId, _amount);
    }

    [Benchmark]
    [BenchmarkCategory("Formatting")]
    public bool OptimizedSpanFormatting()
    {
        Span<char> buffer = stackalloc char[200];
        return ZeroAllocMessageFormatter.TryFormat("Order {OrderId} for user {UserId} with amount {Amount:C}".AsSpan(),
            new object[] { _orderId, _userId, _amount }, buffer, out _);
    }

    #endregion

    #region Rate Limiting Performance Tests

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("RateLimiting")]
    public bool OriginalSimpleRateLimiter()
    {
        var rateLimiter = new SimpleRateLimiter(100, TimeSpan.FromMinutes(1));
        return rateLimiter.ShouldAllow();
    }

    [Benchmark]
    [BenchmarkCategory("RateLimiting")]
    public bool OptimizedHighPerformanceRateLimiter()
    {
        var filter = HighPerformanceRateLimiter.CreateRateLimitedFilter(
            HighPerformanceRateLimiter.RateLimitStrategy.TokenBucket, 100, TimeSpan.FromMinutes(1));
        return filter.Evaluate(_testLogEntry, _filterContext);
    }

    #endregion

    #region Integration Performance Tests

    [Benchmark]
    [BenchmarkCategory("Integration")]
    public void OptimizedFullLoggingPipeline()
    {
        // Test the complete optimized pipeline
        var props = new Dictionary<string, object?>(3, StringComparer.Ordinal)
        {
            ["UserId"] = _userId,
            ["OrderId"] = _orderId,
            ["Amount"] = _amount,
        };

        using var scope = _optimizedLogger.BeginScope(props);

        const string MessageTemplate = "Processing order {OrderId} for user {UserId}";
        var message = ZeroAllocMessageFormatter.FormatFast(MessageTemplate.AsSpan(),
            _orderId, _userId);

        _optimizedLogger.LogInformation(MessageTemplate, _orderId, _userId);
    }

    #endregion

    [GlobalCleanup]
    public void Cleanup()
    {
        FilterExpressionCompiler.ClearCache();

        // Print optimization statistics
        var (cachedExpressions, validExpressions) = FilterExpressionCompiler.GetStatistics();

        Console.WriteLine($"\n🚀 Optimization Performance Summary:");
        Console.WriteLine($"   Expression Compilation Cache: {cachedExpressions} expressions, {validExpressions} valid");
        Console.WriteLine($"   All optimizations target <50ns for hot paths");
    }
}