using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>One job of 60 iterations with tiered compilation off (<c>DOTNET_TieredCompilation=0</c>).</summary>
public sealed class NoTieringConfig : ManualConfig
{
    public NoTieringConfig()
        => AddJob(Job.Default
            .WithIterationCount(60)
            .WithEnvironmentVariables(new EnvironmentVariable("DOTNET_TieredCompilation", "0"))
            .WithId("NoTiering"));
}
