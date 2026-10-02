using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Temporal.Extensions;
using Pragmatic.Temporal.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     What a pinned clock does to tag invalidation: an entry written at the instant of its tag's last
///     invalidation is not served.
/// </summary>
/// <remarks>
///     <para>
///         HybridCache takes its clock from the container's <see cref="TimeProvider" /> — the one
///         <c>UseClock</c> replaces — and judges an entry invalidated when it was written at or before
///         the tag's last invalidation. Under a frozen clock the two instants are the same, so a write
///         that follows an invalidation, with no time in between, produces an entry that is born
///         invalidated. An application pinning its clock in its tests sees a cache that stops hitting,
///         and nothing says why: measured on a consumer application, where every declaration invalidates the tag the
///         next read writes under.
///     </para>
///     <para>
///         This pins the behaviour <c>Pragmatic.Temporal/docs/testing.md</c> and
///         <c>Pragmatic.Caching/docs/troubleshooting.md</c> describe, so the day HybridCache changes the
///         rule the pages are proven stale here. The second test is the control: without it, "not
///         served under a frozen clock" is satisfied by a cache that serves nothing at all.
///     </para>
/// </remarks>
public sealed class TagInvalidationUnderAPinnedClockTests
{
    private static readonly CacheEntryOptions UnderTheTag = new() { Tags = ["glossary"] };

    private static (HybridCacheStack Cache, TestClock Clock) APinnedApplication()
    {
        var clock = TestClock.AtNoon(2026, 6, 15);
        var services = new ServiceCollection();
        services.UseClock(clock);
        services.AddHybridCache();

        var cache = services.BuildServiceProvider().GetRequiredService<HybridCache>();
        return (new HybridCacheStack(cache), clock);
    }

    [Fact]
    public async Task AnEntryWrittenAtTheInvalidationsInstant_IsNotServed()
    {
        var (cache, _) = APinnedApplication();

        await cache.InvalidateByTagAsync("glossary");
        await cache.SetAsync("suggestions", "cached answer", UnderTheTag);

        var (found, _) = await cache.TryGetAsync<string>("suggestions");
        found.Should().BeFalse(
            "the entry was written at the same pinned instant as the invalidation, and HybridCache "
            + "counts written-at-or-before as invalidated");
    }

    [Fact]
    public async Task AnEntryWrittenAfterTheClockMoves_IsServed()
    {
        var (cache, clock) = APinnedApplication();

        await cache.InvalidateByTagAsync("glossary");
        clock.AdvanceSeconds(1);
        await cache.SetAsync("suggestions", "cached answer", UnderTheTag);

        var (found, value) = await cache.TryGetAsync<string>("suggestions");
        found.Should().BeTrue("one second later the entry is newer than the invalidation");
        value.Should().Be("cached answer");
    }
}
