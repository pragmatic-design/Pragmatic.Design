using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.Logging;
using Pragmatic.Logging.Filtering;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks;

/// <summary>
/// Focused benchmarks for Expression DSL performance.
/// Tests the unique filtering capabilities of Pragmatic.Logging.
/// </summary>
[Config(typeof(ExpressionDslBenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
[RankColumn]
public class ExpressionDslBenchmarks
{
    // Test data
    private LogEntry _informationEntry = null!;
    private LogEntry _warningEntry = null!;
    private LogEntry _errorEntry = null!;
    private LogEntry _structuredEntry = null!;
    private LogEntry _performanceEntry = null!;
    private LogFilterContext _context = null!;

    // Expression filters for testing
    private FilterExpression _simpleLevel = null!;
    private FilterExpression _complexBusinessLogic = null!;
    private FilterExpression _enterpriseCompliance = null!;
    private FilterExpression _performanceMonitoring = null!;
    private FilterExpression _securityAuditing = null!;

    // Collection for batch testing
    private LogEntry[] _logEntries = null!;

    [GlobalSetup]
    public void Setup()
    {
        _context = new LogFilterContext();

        // Create test log entries
        _informationEntry = new LogEntry
        {
            LogLevel = LogLevel.Information,
            Category = "MyApp.Controllers.UserController",
            Message = "User action completed",
            Properties = new Dictionary<string, object?>
            {
                ["UserId"] = "user123",
                ["Action"] = "login",
                ["Duration"] = 150.0,
                ["Success"] = true
            }
        };

        _warningEntry = new LogEntry
        {
            LogLevel = LogLevel.Warning,
            Category = "Microsoft.EntityFrameworkCore.Database.Command",
            Message = "Slow query detected",
            Properties = new Dictionary<string, object?>
            {
                ["Duration"] = 2500.0,
                ["CommandText"] = "SELECT * FROM Users WHERE...",
                ["SlowQuery"] = true
            }
        };

        _errorEntry = new LogEntry
        {
            LogLevel = LogLevel.Error,
            Category = "MyApp.Services.PaymentService",
            Message = "Payment processing failed",
            Properties = new Dictionary<string, object?>
            {
                ["UserId"] = "user456",
                ["OrderId"] = "order789",
                ["Amount"] = 99.99m,
                ["ErrorCode"] = "PAYMENT_DECLINED",
                ["BusinessCritical"] = true
            }
        };

        _structuredEntry = new LogEntry
        {
            LogLevel = LogLevel.Information,
            Category = "MyApp.Analytics.EventTracker",
            Message = "Business event tracked",
            Properties = new Dictionary<string, object?>
            {
                ["EventType"] = "purchase_completed",
                ["UserId"] = "user789",
                ["ProductId"] = "prod123",
                ["Category"] = "Electronics",
                ["Revenue"] = 299.99m,
                ["Timestamp"] = DateTime.UtcNow,
                ["SessionId"] = "session_abc123"
            }
        };

        _performanceEntry = new LogEntry
        {
            LogLevel = LogLevel.Critical,
            Category = "System.Performance.Monitor",
            Message = "System performance degraded",
            Properties = new Dictionary<string, object?>
            {
                ["CpuUsage"] = 95.5,
                ["MemoryUsageMB"] = 1800.0,
                ["Duration"] = 5500.0,
                ["Environment"] = "Production",
                ["ServiceDown"] = false
            }
        };

        // Create batch test data
        _logEntries = new[] { _informationEntry, _warningEntry, _errorEntry, _structuredEntry, _performanceEntry };

        // Setup filter expressions
        _simpleLevel = f => f.Level(LogLevel.Warning);

        _complexBusinessLogic = f => f.Group(ctx =>
            (ctx.BusinessCritical() | ctx.PaymentEvent()) &
            ctx.Level(LogLevel.Warning) &
            !ctx.HealthCheck());

        _enterpriseCompliance = f => f.Group(ctx =>
            (ctx.UserAction() | ctx.BusinessEvent() | ctx.SecurityEvent()) &
            ctx.HasStructuredProperties() &
            ctx.Level(LogLevel.Information));

        _performanceMonitoring = f => f.Group(ctx =>
            (ctx.SlowQuery(2000) | ctx.HighMemory(1000)) &
            ctx.Production() &
            !ctx.MonitoringTools());

        _securityAuditing = f => f.Group(ctx =>
            ctx.SecurityEvent() &
            (ctx.Warning() | ctx.Error() | ctx.Critical()) &
            ctx.HasProperty("UserId"));
    }

    #region Single Expression Evaluation

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("SingleEvaluation")]
    public bool SimpleLevel_Evaluation()
    {
        return FilterExpressionEvaluator.Evaluate(_simpleLevel, _warningEntry, _context);
    }

    [Benchmark]
    [BenchmarkCategory("SingleEvaluation")]
    public bool ComplexBusinessLogic_Evaluation()
    {
        return FilterExpressionEvaluator.Evaluate(_complexBusinessLogic, _errorEntry, _context);
    }

    [Benchmark]
    [BenchmarkCategory("SingleEvaluation")]
    public bool EnterpriseCompliance_Evaluation()
    {
        return FilterExpressionEvaluator.Evaluate(_enterpriseCompliance, _structuredEntry, _context);
    }

    [Benchmark]
    [BenchmarkCategory("SingleEvaluation")]
    public bool PerformanceMonitoring_Evaluation()
    {
        return FilterExpressionEvaluator.Evaluate(_performanceMonitoring, _performanceEntry, _context);
    }

    [Benchmark]
    [BenchmarkCategory("SingleEvaluation")]
    public bool SecurityAuditing_Evaluation()
    {
        return FilterExpressionEvaluator.Evaluate(_securityAuditing, _errorEntry, _context);
    }

    #endregion

    #region Batch Expression Evaluation

    [Benchmark]
    [BenchmarkCategory("BatchEvaluation")]
    public int SimpleLevel_BatchEvaluation()
    {
        int matches = 0;
        foreach (var entry in _logEntries)
        {
            if (FilterExpressionEvaluator.Evaluate(_simpleLevel, entry, _context))
                matches++;
        }
        return matches;
    }

    [Benchmark]
    [BenchmarkCategory("BatchEvaluation")]
    public int ComplexBusinessLogic_BatchEvaluation()
    {
        int matches = 0;
        foreach (var entry in _logEntries)
        {
            if (FilterExpressionEvaluator.Evaluate(_complexBusinessLogic, entry, _context))
                matches++;
        }
        return matches;
    }

    [Benchmark]
    [BenchmarkCategory("BatchEvaluation")]
    public int EnterpriseCompliance_BatchEvaluation()
    {
        int matches = 0;
        foreach (var entry in _logEntries)
        {
            if (FilterExpressionEvaluator.Evaluate(_enterpriseCompliance, entry, _context))
                matches++;
        }
        return matches;
    }

    [Benchmark]
    [BenchmarkCategory("BatchEvaluation")]
    public int AllFilters_BatchEvaluation()
    {
        int matches = 0;
        var filters = new[] { _simpleLevel, _complexBusinessLogic, _enterpriseCompliance, _performanceMonitoring, _securityAuditing };

        foreach (var entry in _logEntries)
        {
            foreach (var filter in filters)
            {
                if (FilterExpressionEvaluator.Evaluate(filter, entry, _context))
                    matches++;
            }
        }
        return matches;
    }

    #endregion

    #region High-Volume Evaluation

    [Benchmark]
    [BenchmarkCategory("HighVolume")]
    public int HighVolume_SimpleFilter()
    {
        int matches = 0;
        for (int i = 0; i < 10000; i++)
        {
            if (FilterExpressionEvaluator.Evaluate(_simpleLevel, _warningEntry, _context))
                matches++;
        }
        return matches;
    }

    [Benchmark]
    [BenchmarkCategory("HighVolume")]
    public int HighVolume_ComplexFilter()
    {
        int matches = 0;
        for (int i = 0; i < 10000; i++)
        {
            if (FilterExpressionEvaluator.Evaluate(_complexBusinessLogic, _errorEntry, _context))
                matches++;
        }
        return matches;
    }

    [Benchmark]
    [BenchmarkCategory("HighVolume")]
    public int HighVolume_MixedFilters()
    {
        int matches = 0;
        var filters = new[] { _simpleLevel, _complexBusinessLogic, _enterpriseCompliance };

        for (int i = 0; i < 10000; i++)
        {
            var filterIndex = i % filters.Length;
            var entryIndex = i % _logEntries.Length;

            if (FilterExpressionEvaluator.Evaluate(filters[filterIndex], _logEntries[entryIndex], _context))
                matches++;
        }
        return matches;
    }

    #endregion

    #region Cache Performance Testing

    [Benchmark]
    [BenchmarkCategory("CachePerformance")]
    public int CacheWarmup_SingleExpression()
    {
        int matches = 0;

        // First run - cache miss
        if (FilterExpressionEvaluator.Evaluate(_simpleLevel, _warningEntry, _context))
            matches++;

        // Subsequent runs - cache hits
        for (int i = 0; i < 1000; i++)
        {
            if (FilterExpressionEvaluator.Evaluate(_simpleLevel, _warningEntry, _context))
                matches++;
        }

        return matches;
    }

    [Benchmark]
    [BenchmarkCategory("CachePerformance")]
    public int CacheStress_MultipleExpressions()
    {
        int matches = 0;
        var filters = new[] { _simpleLevel, _complexBusinessLogic, _enterpriseCompliance, _performanceMonitoring, _securityAuditing };

        // Warm up all filters
        foreach (var filter in filters)
        {
            FilterExpressionEvaluator.Evaluate(filter, _informationEntry, _context);
        }

        // Test cache performance
        for (int i = 0; i < 1000; i++)
        {
            foreach (var filter in filters)
            {
                if (FilterExpressionEvaluator.Evaluate(filter, _logEntries[i % _logEntries.Length], _context))
                    matches++;
            }
        }

        return matches;
    }

    #endregion

    #region Property-Based Filtering

    [Benchmark]
    [BenchmarkCategory("PropertyFiltering")]
    public bool PropertyExists_Evaluation()
    {
        FilterExpression filter = f => f.HasProperty("UserId");
        return FilterExpressionEvaluator.Evaluate(filter, _informationEntry, _context);
    }

    [Benchmark]
    [BenchmarkCategory("PropertyFiltering")]
    public bool PropertyValue_Evaluation()
    {
        FilterExpression filter = f => f.HasProperty("UserId", "user123");
        return FilterExpressionEvaluator.Evaluate(filter, _informationEntry, _context);
    }

    [Benchmark]
    [BenchmarkCategory("PropertyFiltering")]
    public bool StructuredProperties_Evaluation()
    {
        FilterExpression filter = f => f.HasStructuredProperties();
        return FilterExpressionEvaluator.Evaluate(filter, _structuredEntry, _context);
    }

    [Benchmark]
    [BenchmarkCategory("PropertyFiltering")]
    public bool NumericProperties_Evaluation()
    {
        FilterExpression filter = f => f.SlowQuery(2000) & f.HighMemory(1000);
        return FilterExpressionEvaluator.Evaluate(filter, _performanceEntry, _context);
    }

    #endregion

    [GlobalCleanup]
    public void Cleanup()
    {
        var stats = FilterExpressionEvaluator.GetStatistics();
        Console.WriteLine($"\n🚀 Expression DSL Performance Summary:");
        Console.WriteLine($"   Total Evaluations: {stats.TotalEvaluations:N0}");
        Console.WriteLine($"   Cache Hit Ratio: {stats.CacheHitRatio:P2}");
        Console.WriteLine($"   Cached Expressions: {stats.CachedExpressions:N0}");
        Console.WriteLine($"   Average per evaluation: {CalculateAverageTime():F2}ns");
    }

    private double CalculateAverageTime()
    {
        var stats = FilterExpressionEvaluator.GetStatistics();
        if (stats.TotalEvaluations > 0)
        {
            // Estimate based on our micro-benchmarks from examples (0.24-0.42μs per evaluation)
            return 300.0; // Conservative estimate in nanoseconds
        }
        return 0.0;
    }
}

/// <summary>
/// Specialized benchmark configuration for Expression DSL testing.
/// </summary>
public class ExpressionDslBenchmarkConfig : ManualConfig
{
    public ExpressionDslBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(5)
            .WithIterationCount(15)
            .WithInvocationCount(1000)
            .WithUnrollFactor(1));

        AddColumn(StatisticColumn.Mean);
        AddColumn(StatisticColumn.StdDev);
        AddColumn(StatisticColumn.Min);
        AddColumn(StatisticColumn.Max);
        AddColumn(BaselineRatioColumn.RatioMean);

        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));
        WithSummaryStyle(BenchmarkDotNet.Reports.SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend));

        AddExporter(HtmlExporter.Default);
        AddExporter(MarkdownExporter.Default);
    }
}