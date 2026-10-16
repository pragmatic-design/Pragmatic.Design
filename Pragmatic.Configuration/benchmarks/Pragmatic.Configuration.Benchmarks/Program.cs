using BenchmarkDotNet.Running;
using Pragmatic.Configuration.Benchmarks;

// `verify` reads the options once each way and checks every row returns the tenant's values over appsettings'.
// It times nothing. Anything else goes to BenchmarkDotNet.
if (args is ["verify"])
{
    var benchmarks = new TenantOptionsBenchmarks();
    await benchmarks.SetupAsync().ConfigureAwait(false);
    try
    {
        await benchmarks.VerifyAsync().ConfigureAwait(false);
    }
    finally
    {
        await benchmarks.CleanupAsync().ConfigureAwait(false);
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(TenantOptionsBenchmarks).Assembly).Run(args);
