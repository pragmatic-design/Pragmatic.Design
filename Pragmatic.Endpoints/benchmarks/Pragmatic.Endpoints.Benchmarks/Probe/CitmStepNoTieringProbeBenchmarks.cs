using BenchmarkDotNet.Attributes;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     <see cref="CitmStepProbe" /> with tiered compilation off: every method is compiled once, fully optimized, so
///     a step that comes from the JIT replacing a method mid-run cannot happen here.
/// </summary>
[Config(typeof(NoTieringConfig))]
public class CitmStepNoTieringProbeBenchmarks : CitmStepProbe;
