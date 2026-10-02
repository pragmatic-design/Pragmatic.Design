using Pragmatic.Testing.Assertions;
using StackExchange.Redis;
using Xunit;

namespace Pragmatic.Caching.Redis.Tests;

/// <summary>
///     What <see cref="RedisCounterCacheStack.IncrementAsync" /> writes,
///     <c>RemoveAsync</c> removes.
/// </summary>
/// <remarks>
///     <para>
///         The decorator routes increments to Redis. In the configuration it exists for (an in-memory
///         cache with Redis-backed counters) the idempotency filter takes its reservation in Redis. A
///         <c>RemoveAsync</c> delegated to the inner stack alone would release it from memory, removing a
///         key that was never there: a first request that failed would leave the reservation standing,
///         and every retry with that key would answer <b>409</b> until the reservation's own TTL ran
///         out — an hour by default.
///     </para>
///     <para>
///         ⚠️ Delegating <c>RemoveAsync</c> looks right on the class's own terms: its summary says
///         <i>"only counters (rate limiting, quotas) move to Redis"</i>, and nobody removes a rate-limit
///         counter. It is wrong for a caller that uses the counter as a <b>reservation</b> — a
///         primitive with a release — which is a different thing wearing a counter's clothes.
///     </para>
///     <para>
///         Routing removals to Redis alone is wrong too: an ordinary entry written by <c>SetAsync</c> lives
///         in the inner stack, and a key gives no clue which of the two wrote it. A remove has to reach
///         both, which is what these cases hold from each side.
///     </para>
/// </remarks>
public sealed class AReleasedReservationCanBeTakenAgainTests(RedisFixture redis)
    : IClassFixture<RedisFixture>, IDisposable
{
    private ConnectionMultiplexer? _multiplexer;

    private static string FreshKey() => $"reservation-{Guid.NewGuid():N}";

    public void Dispose() => _multiplexer?.Dispose();

    [Fact]
    public async Task AReservationTakenInRedis_IsGoneAfterRemove()
    {
        // No broadcast: the inner stack is this node's own in-memory one, which is exactly the
        // pairing the decorator exists for — in-memory cache, Redis counters.
        await using var node = await CacheNode.StartAsync(null);
        var stack = Counters(node);
        var key = FreshKey();

        (await stack.IncrementAsync(key, 1, TimeSpan.FromMinutes(30))).Should().Be(1,
            "the first caller takes the reservation");
        (await stack.IncrementAsync(key, 1, TimeSpan.FromMinutes(30))).Should().Be(2,
            "a second caller is told it is held — this is the 409 path");

        await stack.RemoveAsync(key);

        (await stack.IncrementAsync(key, 1, TimeSpan.FromMinutes(30))).Should().Be(1,
            "the reservation was released, so the retry takes it again rather than being refused for "
            + "the whole hour the reservation would otherwise live");
    }

    /// <summary>
    ///     The control from the other side: a remove must still reach the ordinary entries, which do
    ///     not live in Redis at all.
    /// </summary>
    /// <remarks>
    ///     Without it, "the reservation is gone" is satisfied by routing every removal to Redis — and
    ///     that would leave `SetAsync`'s entries un-removable, which is the same defect mirrored.
    /// </remarks>
    [Fact]
    public async Task AnOrdinaryEntry_IsStillRemoved()
    {
        await using var node = await CacheNode.StartAsync(null);
        var stack = Counters(node);
        var key = FreshKey();

        await stack.SetAsync(key, "value");
        (await stack.TryGetAsync<string>(key)).Found.Should().BeTrue("or the removal below proves nothing");

        await stack.RemoveAsync(key);

        (await stack.TryGetAsync<string>(key)).Found.Should().BeFalse(
            "an entry written through the decorator's inner stack is still the inner stack's to drop");
    }

    /// <summary>The decorator under test, wrapping the node's own stack.</summary>
    private RedisCounterCacheStack Counters(CacheNode node)
    {
        _multiplexer ??= ConnectionMultiplexer.Connect(redis.ConnectionString);
        return new RedisCounterCacheStack(node.Stack, _multiplexer);
    }
}
