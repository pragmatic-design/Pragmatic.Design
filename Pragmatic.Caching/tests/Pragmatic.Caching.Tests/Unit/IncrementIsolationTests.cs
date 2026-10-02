using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Whether the lost increments come from this stack's own serialisation or from the cache under it.
/// </summary>
public class IncrementIsolationTests
{
    [Fact]
    public async Task OverATriviallyCorrectCache_ParallelIncrementsSumExactly()
    {
        const int concurrency = 200;
        var sut = new HybridCacheStack(new TrivialHybridCache());

        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, concurrency).Select(async _ =>
        {
            await start.Task.ConfigureAwait(false);
            await sut.IncrementAsync("inc-isolated", 1).ConfigureAwait(false);
        }).ToArray();

        start.SetResult();
        await Task.WhenAll(tasks);

        (await sut.GetAsync<long>("inc-isolated")).Should().Be(concurrency,
            "given a correct store, only the per-key gate can lose an increment");
    }
}
