using BenchmarkDotNet.Running;
using Pragmatic.Persistence.Benchmarks;

// `verify` saves each row once per size with a change-detection counter attached, checks that both wrote
// every row and the interceptors ran, and times nothing. Anything else goes to BenchmarkDotNet.
if (args is ["verify"])
{
    foreach (var rows in (int[])[10, 100, 1000])
    {
        var benchmarks = new SaveChangesBenchmarks { Rows = rows };
        benchmarks.Setup();
        try
        {
            benchmarks.Verify();
        }
        finally
        {
            benchmarks.Cleanup();
        }
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(SaveChangesBenchmarks).Assembly).Run(args);
