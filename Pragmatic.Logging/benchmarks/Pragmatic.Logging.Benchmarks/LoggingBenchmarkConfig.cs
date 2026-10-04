using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Validators;

namespace Pragmatic.Logging.Benchmarks;

/// <summary>
///     One job, BenchmarkDotNet's default, and nothing tuned by hand.
/// </summary>
/// <remarks>
///     The previous configuration added a custom job beside the class's <c>[SimpleJob]</c>, so every
///     benchmark ran twice into one report, and its <c>InvocationCount=1000</c> with <c>UnrollFactor=1</c>
///     kept every iteration under BenchmarkDotNet's minimum iteration time. The default job picks the
///     invocation count from a pilot run, which is the thing those settings were standing in for.
/// </remarks>
public sealed class LoggingBenchmarkConfig : ManualConfig
{
    public LoggingBenchmarkConfig()
    {
        AddJob(Job.Default);
        AddColumnProvider(DefaultColumnProviders.Instance);
        // No exporters here: BenchmarkDotNet merges the default config, which already writes the HTML,
        // GitHub Markdown and CSV reports, and adding them again only earns a warning.
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.Declared));
        WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend));

        AddValidator(BaselineValidator.FailOnError);
        AddValidator(ExecutionValidator.FailOnError);
    }
}
