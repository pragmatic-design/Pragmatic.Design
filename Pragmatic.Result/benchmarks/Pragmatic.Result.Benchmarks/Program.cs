// =============================================================================
// Pragmatic.Result Benchmarks
// Performance measurement for Result types
// =============================================================================

using BenchmarkDotNet.Running;
using Pragmatic.Result.Benchmarks;

// `verify` runs the check that every benchmark computes what it says, without timing anything: it
// throws, and exits non-zero, when one does not.
if (args is ["verify"])
{
    new ResultBenchmarks().Setup();
    Console.WriteLine("✅ Every benchmark computed what its name says.");
    return;
}

// Run all benchmarks
BenchmarkSwitcher.FromAssembly(typeof(ResultBenchmarks).Assembly).Run(args);
