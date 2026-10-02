// =============================================================================
// Pragmatic.Result Benchmarks
// Performance measurement for Result types
// =============================================================================

using BenchmarkDotNet.Running;
using Pragmatic.Result.Benchmarks;

// Run all benchmarks
BenchmarkSwitcher.FromAssembly(typeof(ResultBenchmarks).Assembly).Run(args);
