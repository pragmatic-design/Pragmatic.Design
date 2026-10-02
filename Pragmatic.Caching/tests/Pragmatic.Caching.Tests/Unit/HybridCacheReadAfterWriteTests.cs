using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     The property <c>IncrementAsync</c> rests on, measured against the real HybridCache rather than
///     through our own code: once <c>SetAsync</c> has completed, the next read must observe that value.
/// </summary>
/// <remarks>
///     If this fails, the lost increments are not ours to fix in the per-key gate — the gate can
///     serialise perfectly and still lose a value if the read after a completed write returns the
///     previous one.
/// </remarks>
public class HybridCacheReadAfterWriteTests(ITestOutputHelper output)
{
    private static async ValueTask<long> ReadAsync(HybridCache cache, string key)
    {
        // The same shape TryGetAsync uses: probe with writes disabled so a miss stores nothing.
        var noWrite = new HybridCacheEntryOptions
        {
            Flags = HybridCacheEntryFlags.DisableLocalCacheWrite
                    | HybridCacheEntryFlags.DisableDistributedCacheWrite,
        };

        return await cache.GetOrCreateAsync(
            key, 0L, (s, _) => ValueTask.FromResult(s), noWrite).ConfigureAwait(false);
    }

    [Fact]
    public async Task UnderConcurrency_AReadAfterACompletedWrite_NeverReturnsTheOlderValue()
    {
        const int rounds = 2000;
        var services = new ServiceCollection();
        services.AddHybridCache();
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<HybridCache>();

        const string key = "raw-check";
        var stale = 0;

        // Strictly sequential writes and reads, but run as fast as the machine allows so any window
        // between a completed Set and its visibility is exercised.
        for (var i = 1; i <= rounds; i++)
        {
            await cache.SetAsync(key, (long)i);
            var seen = await ReadAsync(cache, key);
            if (seen != i)
            {
                stale++;
                if (stale <= 3)
                    output.WriteLine($"round {i}: wrote {i}, read back {seen}");
            }
        }

        stale.Should().Be(0, "a read after a completed write must never return the previous value");
    }
}
