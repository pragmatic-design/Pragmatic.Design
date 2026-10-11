using BenchmarkDotNet.Running;

namespace Pragmatic.Logging.Benchmarks;

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

                var actions = new Modules.ActionsLogBenchmarks();
                actions.Setup();
                actions.Cleanup();
                Console.WriteLine("✅ Both generators' Actions call sites wrote the same line, and nothing below the level.");
                break;

            case "actions":
                Console.WriteLine("🧩 Pragmatic.Actions' invoker logging, Microsoft's generator against call sites");
                BenchmarkRunner.Run<Modules.ActionsLogBenchmarks>(null, args[1..]);
                break;

            case "json":
                Console.WriteLine("🧾 The same call written as a JSON line by each library");
                BenchmarkRunner.Run<Json.JsonSinkBenchmarks>(null, args[1..]);
                break;

            case "probe":
                BenchmarkRunner.Run<Json.Probe.ProbeBenchmarks>(null, args[1..]);
                break;

            case "classic-probe":
                BenchmarkRunner.Run<Comparison.ClassicProbeBenchmarks>(null, args[1..]);
                break;

            case "timestamp":
                BenchmarkRunner.Run<Json.Probe.TimestampBenchmarks>(null, args[1..]);
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
                // the allocation ratchet, and on a pull request a `--filter` per class the change reaches
                // (scripts/benchmarks.mjs). These are the classes the ratchet watches. A switcher per
                // class: BenchmarkRunner.Run<T> throws when a filter leaves T with nothing, and one
                // switcher over all of them stops every class when one fails validation.
                var passThrough = args.Length > 1 ? args[1..] : [];
                if (!passThrough.Contains("--filter") && !passThrough.Contains("-f"))
                    passThrough = [.. passThrough, "--filter", "*"];
                Console.WriteLine("🚀 Complete benchmark suite");
                Type[] suite =
                [
                    typeof(LoggingBenchmarks),
                    typeof(Json.JsonSinkBenchmarks),
                    typeof(Modules.ActionsLogBenchmarks),
                    typeof(Redaction.RedactionOverheadBenchmarks),
                    typeof(ExpressionDslBenchmarks),
                    typeof(ZeroAllocationBenchmark),
                    typeof(AllocationComparisonBenchmark),
                ];
                foreach (var type in suite)
                    BenchmarkSwitcher.FromTypes([type]).Run(passThrough);
                break;

            default:
                Console.WriteLine($"❌ Unknown benchmark: {args[0]}");
                Console.WriteLine("Available options: logging, expression, zero, optimized, all");
                break;
        }
    }
}