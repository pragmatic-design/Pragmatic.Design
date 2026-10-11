using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;

namespace Pragmatic.Logging.Benchmarks;

/// <summary>
/// Shared config used by benchmark classes that don't need a custom ManualConfig.
/// Adds the default-style Markdown report to the HTML, GitHub Markdown and CSV the default config writes.
/// </summary>
public class DefaultExportConfig : ManualConfig
{
    public DefaultExportConfig()
    {
        AddJob(Job.Default);
        AddColumnProvider(DefaultColumnProviders.Instance);
        // HTML, GitHub Markdown and CSV come from the default config BenchmarkDotNet merges in.
        AddExporter(MarkdownExporter.Default);
        WithSummaryStyle(SummaryStyle.Default.WithRatioStyle(RatioStyle.Trend));
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.FastestToSlowest));
    }
}
