using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Caching.Redis;
using Pragmatic.Testing.Assertions;
using Pragmatic.Tests.Generated;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Another node's message removes the entry from this node's <see cref="HybridCache" />; this
///     node's own message does not.
/// </summary>
/// <remarks>
///     A real in-memory <see cref="HybridCache" />, because "removed" is only measurable as the next
///     read calling its factory again.
/// </remarks>
public class CacheInvalidationSubscriberTests
{
    private readonly HybridCache _cache = new ServiceCollection()
        .AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>();

    private readonly RedisCacheInvalidationChannel _channel = new(
        new ConnectionMultiplexerMock(),
        Options.Create(new RedisCacheInvalidationOptions()),
        NullLogger<RedisCacheInvalidationChannel>.Instance);

    private CacheInvalidationSubscriber Subscriber()
        => new(_channel, _cache, NullLogger<CacheInvalidationSubscriber>.Instance);

    /// <summary>Reads the key, and says whether the value had to be produced again.</summary>
    private async Task<bool> WasProducedAgainAsync(string key)
    {
        var produced = false;
        await _cache.GetOrCreateAsync(key, _ =>
        {
            produced = true;
            return ValueTask.FromResult(2);
        }).ConfigureAwait(false);
        return produced;
    }

    [Fact]
    public async Task AnotherNodesTag_RemovesTheEntriesCarryingIt()
    {
        await _cache.SetAsync("k", 1, tags: ["glossary"]);

        await Subscriber().ApplyAsync(new CacheInvalidationMessage(CacheInvalidationKind.Tag, "glossary", "node-b"));

        (await WasProducedAgainAsync("k")).Should().BeTrue();
    }

    [Fact]
    public async Task AnotherNodesKey_RemovesThatEntry()
    {
        await _cache.SetAsync("k", 1);

        await Subscriber().ApplyAsync(new CacheInvalidationMessage(CacheInvalidationKind.Key, "k", "node-b"));

        (await WasProducedAgainAsync("k")).Should().BeTrue();
    }

    /// <summary>
    ///     The control: this node's own message comes back on the channel and changes nothing — the
    ///     invalidation it describes already ran here, before it was published.
    /// </summary>
    [Fact]
    public async Task ThisNodesOwnMessage_IsIgnored()
    {
        await _cache.SetAsync("k", 1, tags: ["glossary"]);

        await Subscriber().ApplyAsync(
            new CacheInvalidationMessage(CacheInvalidationKind.Tag, "glossary", _channel.NodeId));

        (await WasProducedAgainAsync("k")).Should().BeFalse();
    }
}
