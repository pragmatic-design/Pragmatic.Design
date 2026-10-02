using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Redis.Tests;

/// <summary>
///     What one node invalidates, every node with the broadcast drops — and a node without it does not.
/// </summary>
/// <remarks>
///     <para>
///         Two hosts on one Redis: an invalidation run on one reached the shared entry and
///         not the other host's in-process copy, which kept serving the old answer for the entry's whole
///         lifetime. <see cref="RedisCacheInvalidationExtensions.AddRedisCacheInvalidationBroadcast(Microsoft.Extensions.DependencyInjection.IServiceCollection, string, System.Action{RedisCacheInvalidationOptions}?)" />
///         is the channel between them.
///     </para>
///     <para>
///         The shape of each case: both nodes produce and cache the value; the second node is asked
///         again and does <b>not</b> produce it — its copy is live; the first node invalidates; the
///         second node, asked again, has to produce it. The middle step is what makes the last one
///         mean something: without it a node that never cached anything would pass.
///     </para>
/// </remarks>
public sealed class AnInvalidationReachesEveryNodeTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    private static (string Key, string Tag) Fresh()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        return ($"k-{id}", $"t-{id}");
    }

    [Fact]
    public async Task ATagInvalidatedOnOneNode_IsDroppedOnTheOther()
    {
        await using var first = await CacheNode.StartAsync(redis.ConnectionString);
        await using var second = await CacheNode.StartAsync(redis.ConnectionString);
        var (key, tag) = Fresh();

        (await first.ProducesAsync(key, tag)).Should().BeTrue();
        (await second.ProducesAsync(key, tag)).Should().BeTrue("the nodes share no cache, so each produces its own");
        (await second.ProducesAsync(key, tag)).Should().BeFalse("and the second one now holds a live copy");

        using var applied = new RemoteInvalidationApplied(second.NodeId!, tag);
        await first.Stack.InvalidateByTagAsync(tag);
        await applied.WaitAsync();

        (await second.ProducesAsync(key, tag)).Should().BeTrue(
            "the first node's invalidation reached the second node's own copy");
    }

    [Fact]
    public async Task AKeyRemovedOnOneNode_IsDroppedOnTheOther()
    {
        await using var first = await CacheNode.StartAsync(redis.ConnectionString);
        await using var second = await CacheNode.StartAsync(redis.ConnectionString);
        var (key, tag) = Fresh();

        (await first.ProducesAsync(key, tag)).Should().BeTrue();
        (await second.ProducesAsync(key, tag)).Should().BeTrue();
        (await second.ProducesAsync(key, tag)).Should().BeFalse();

        using var applied = new RemoteInvalidationApplied(second.NodeId!, key);
        await first.Stack.RemoveAsync(key);
        await applied.WaitAsync();

        (await second.ProducesAsync(key, tag)).Should().BeTrue();
    }

    /// <summary>
    ///     The control: a node without the broadcast keeps its copy — the defect this suite exists for.
    /// </summary>
    /// <remarks>
    ///     A third node with the broadcast is the witness: once it has applied the message, the message
    ///     went out, and the node without a subscriber had its chance and nothing to hear it with.
    ///     Without the witness, "nothing happened" could only be asserted after waiting for nothing.
    /// </remarks>
    [Fact]
    public async Task ANodeWithoutTheBroadcast_KeepsItsCopy()
    {
        await using var first = await CacheNode.StartAsync(redis.ConnectionString);
        await using var deaf = await CacheNode.StartAsync(redis: null);
        await using var witness = await CacheNode.StartAsync(redis.ConnectionString);
        var (key, tag) = Fresh();

        foreach (var node in new[] { first, deaf, witness })
            (await node.ProducesAsync(key, tag)).Should().BeTrue();

        using var applied = new RemoteInvalidationApplied(witness.NodeId!, tag);
        await first.Stack.InvalidateByTagAsync(tag);
        await applied.WaitAsync();

        (await witness.ProducesAsync(key, tag)).Should().BeTrue("the witness heard it");
        (await deaf.ProducesAsync(key, tag)).Should().BeFalse(
            "a node without the broadcast serves its own copy until it expires — the gap this closes");
    }
}
