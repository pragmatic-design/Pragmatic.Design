using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;

namespace Pragmatic.Logging.Benchmarks;

/// <summary>
/// Quick benchmark to generate result files for demonstration
/// </summary>
[Config(typeof(QuickBenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob]
public class SimpleBenchmark
{
    private ILogger<SimpleBenchmark> _pragmaticLogger = null!;
    private ILogger<SimpleBenchmark> _nullLogger = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Setup Pragmatic logging
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLoggingBuilder(builder =>
        {
            builder.AddProvider(serviceProvider =>
                new PragmaticNullProvider("Quick", PragmaticNullConfiguration.ForBenchmarking()));
        });
        var serviceProvider = services.BuildServiceProvider();
        _pragmaticLogger = serviceProvider.GetRequiredService<ILogger<SimpleBenchmark>>();

        // Setup null logger
        _nullLogger = NullLogger<SimpleBenchmark>.Instance;
    }

    [Benchmark(Baseline = true)]
    public void NullLogger_Log()
    {
        _nullLogger.LogInformation("Quick test message");
    }

    [Benchmark]
    public void PragmaticLogger_Log()
    {
        _pragmaticLogger.LogInformation("Quick test message");
    }
}

/// <summary>
/// Quick benchmark configuration to generate results fast
/// </summary>
public class QuickBenchmarkConfig : ManualConfig
{
    public QuickBenchmarkConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(1)
            .WithIterationCount(3)
            .WithInvocationCount(100)
            .WithUnrollFactor(1));

        AddColumn(StatisticColumn.Mean);
        AddColumn(BaselineRatioColumn.RatioMean);

        // Add exporters to save results
        AddExporter(HtmlExporter.Default);
        AddExporter(MarkdownExporter.Default);
    }
}