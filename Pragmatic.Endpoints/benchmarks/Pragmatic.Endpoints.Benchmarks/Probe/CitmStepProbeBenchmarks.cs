using BenchmarkDotNet.Attributes;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     <see cref="CitmStepProbe" /> with tiered compilation as a host runs it, and 60 iterations: the step was seen
///     between iterations 22 and 28, so a run must reach well past it.
/// </summary>
[SimpleJob(iterationCount: 60)]
public class CitmStepProbeBenchmarks : CitmStepProbe;
