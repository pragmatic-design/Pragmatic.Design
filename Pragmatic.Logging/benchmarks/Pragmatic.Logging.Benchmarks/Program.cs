using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

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

public class Program
{
    public static void Main(string[] args)
    {
        //if (args.Length == 0)
        //{
        //    Console.WriteLine("🚀 Pragmatic.Logging - Comprehensive Benchmarks");
        //    Console.WriteLine("═══════════════════════════════════════════════════");
        //    Console.WriteLine();
        //    Console.WriteLine("Available benchmark suites:");
        //    Console.WriteLine("1. logging     - Compare with Serilog, NLog (comprehensive)");
        //    Console.WriteLine("2. expression  - Expression DSL performance (unique feature)");
        //    Console.WriteLine("3. zero        - Zero allocation benchmarks");
        //    Console.WriteLine("4. all         - Run all benchmark suites");
        //    Console.WriteLine();
        //    Console.WriteLine("Usage: dotnet run -- [benchmark_name]");
        //    Console.WriteLine();
        //    Console.WriteLine("Recommended: Start with 'logging' for library comparison");
        //    return;
        //}


        var firstArgs = args.Length == 0 ? "all" : args[0].ToLowerInvariant();
        Console.WriteLine($"🔥 Running {firstArgs} benchmarks...\n");
        switch (firstArgs)
        {
            case "logging":
                Console.WriteLine("📊 Comprehensive logging library comparison");
                BenchmarkRunner.Run<LoggingBenchmarks>();
                break;

            case "verify":
                // The equivalence check alone, without timing anything: every sink must consume the
                // same event. Throws, and exits non-zero, when one does not.
                var comparison = new LoggingBenchmarks();
                comparison.Setup();
                comparison.Cleanup();
                Console.WriteLine("✅ Every sink consumed the same event in every scenario.");

                var json = new Json.JsonSinkBenchmarks();
                json.Setup();
                json.Cleanup();
                Console.WriteLine("✅ Every JSON sink wrote the same call, and the personal data was masked.");
                break;

            case "json":
                Console.WriteLine("🧾 The same call written as a JSON line by each library");
                BenchmarkRunner.Run<Json.JsonSinkBenchmarks>(null, args[1..]);
                break;

            case "quick":
                // Pass-through to BenchmarkDotNet's CLI parsing for filtered iteration runs,
                // e.g.: dotnet run -c Release -- quick --anyCategories Simple
                Console.WriteLine("⏱️ Filtered logging comparison (args pass-through)");
                BenchmarkRunner.Run<LoggingBenchmarks>(null, args[1..]);
                break;

            case "redaction":
                Console.WriteLine("🔒 Declared redaction overhead (Pragmatic only)");
                BenchmarkRunner.Run<Redaction.RedactionOverheadBenchmarks>(null, args[1..]);
                break;

            case "expression":
                Console.WriteLine("⚡ Expression DSL performance analysis");
                BenchmarkRunner.Run<ExpressionDslBenchmarks>();
                break;

            case "zero":
                Console.WriteLine("🎯 Zero allocation performance tests");
                BenchmarkRunner.Run<ZeroAllocationBenchmark>();
                BenchmarkRunner.Run<AllocationComparisonBenchmark>();
                break;

            case "optimized":
                Console.WriteLine("🚀 Optimization performance comparison (before vs after)");
                BenchmarkRunner.Run<OptimizedPerformanceBenchmark>();
                break;

            case "all":
                // What follows "all" goes to BenchmarkDotNet: the CI workflow adds `--exporters json` for
                // the allocation ratchet (scripts/benchmarks.mjs).
                var passThrough = args.Length > 1 ? args[1..] : [];
                Console.WriteLine("🚀 Complete benchmark suite");
                Console.WriteLine("\n1/4 - Logging Library Comparison");
                BenchmarkRunner.Run<LoggingBenchmarks>(null, passThrough);
                BenchmarkRunner.Run<Json.JsonSinkBenchmarks>(null, passThrough);

                Console.WriteLine("\n2/4 - Declared Redaction Overhead");
                BenchmarkRunner.Run<Redaction.RedactionOverheadBenchmarks>(null, passThrough);

                Console.WriteLine("\n3/4 - Expression DSL Performance");
                BenchmarkRunner.Run<ExpressionDslBenchmarks>(null, passThrough);

                Console.WriteLine("\n4/4 - Zero Allocation Tests");
                BenchmarkRunner.Run<ZeroAllocationBenchmark>(null, passThrough);
                BenchmarkRunner.Run<AllocationComparisonBenchmark>(null, passThrough);
                break;

            default:
                Console.WriteLine($"❌ Unknown benchmark: {args[0]}");
                Console.WriteLine("Available options: logging, expression, zero, optimized, all");
                break;
        }
    }
}