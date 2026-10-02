using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Caching.Extensions;
using Pragmatic.Caching.Redis;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using StackExchange.Redis;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     What removes an entry runs here and goes out on the channel; nothing else does.
/// </summary>
/// <remarks>
///     The channel is real and the Redis beneath it is a mock: what is asserted is the message that
///     leaves, not that Redis carries it — <c>Pragmatic.Caching.Redis.Tests</c> measures that, with two
///     nodes and a container.
/// </remarks>
public class BroadcastingCacheStackTests
{
    private sealed class TestCategory;

    private readonly CacheStackMock _inner = new();
    private readonly ConnectionMultiplexerMock _redis = new();
    private readonly SubscriberMock _subscriber = new();

    private BroadcastingCacheStack CreateStack()
    {
        _redis.GetSubscriber.Returns(_subscriber);
        _subscriber.PublishAsync.Returns(Task.FromResult(1L));

        return new BroadcastingCacheStack(_inner, new RedisCacheInvalidationChannel(
            _redis,
            Options.Create(new RedisCacheInvalidationOptions()),
            NullLogger<RedisCacheInvalidationChannel>.Instance));
    }

    private static bool IsMessage(RedisValue wire, CacheInvalidationKind kind, string value)
        => CacheInvalidationMessage.FromWire(wire.ToString()) is { } m && m.Kind == kind && m.Value == value;

    [Fact]
    public async Task RemovingAKey_RemovesItHere_AndBroadcastsTheKey()
    {
        var stack = CreateStack();

        await stack.RemoveAsync("glossary:k");

        _inner.RemoveAsync.Received(1, "glossary:k", Arg.Any<CancellationToken>());
        _subscriber.PublishAsync.Received(1,
            Arg.Is<RedisChannel>(c => c == RedisChannel.Literal("pragmatic:cache:invalidations")),
            Arg.Is<RedisValue>(v => IsMessage(v, CacheInvalidationKind.Key, "glossary:k")),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task InvalidatingATag_InvalidatesItHere_AndBroadcastsTheTag()
    {
        var stack = CreateStack();

        await stack.InvalidateByTagAsync("glossary");

        _inner.InvalidateByTagAsync.Received(1, "glossary", Arg.Any<CancellationToken>());
        _subscriber.PublishAsync.Received(1, Arg.Any<RedisChannel>(),
            Arg.Is<RedisValue>(v => IsMessage(v, CacheInvalidationKind.Tag, "glossary")),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RemoveByTag_IsBroadcastOnce()
    {
        var stack = CreateStack();

        await stack.RemoveByTagAsync("glossary");

        _subscriber.PublishAsync.Received(1, Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
    }

    /// <summary>
    ///     A tag that failed locally is still stale everywhere else, so it still goes out — and the
    ///     caller still hears about the failure.
    /// </summary>
    [Fact]
    public async Task InvalidatingSeveralTags_BroadcastsEveryOne_EvenWhenTheLocalRunFails()
    {
        var stack = CreateStack();
        _inner.InvalidateByTagsAsync
            .When(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Throws(new AggregateException(new InvalidOperationException("one tag failed")));

        var act = () => stack.InvalidateByTagsAsync(["glossary", "users", " "]).AsTask();

        await act.Should().ThrowAsync<AggregateException>();
        _subscriber.PublishAsync.Received(1, Arg.Any<RedisChannel>(),
            Arg.Is<RedisValue>(v => IsMessage(v, CacheInvalidationKind.Tag, "glossary")), Arg.Any<CommandFlags>());
        _subscriber.PublishAsync.Received(1, Arg.Any<RedisChannel>(),
            Arg.Is<RedisValue>(v => IsMessage(v, CacheInvalidationKind.Tag, "users")), Arg.Any<CommandFlags>());
        _subscriber.PublishAsync.Received(2, Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
    }

    /// <summary>The control: reads and writes delegate, and say nothing on the channel.</summary>
    [Fact]
    public async Task ReadsAndWrites_BroadcastNothing()
    {
        var stack = CreateStack();
        _inner.GetAsync.Returns<int>(new ValueTask<int>(42));

        (await stack.GetAsync<int>("k")).Should().Be(42, "the read reached the inner stack");
        await stack.SetAsync("k", 7);

        _subscriber.PublishAsync.DidNotReceive();
    }

    /// <summary>
    ///     A publish that fails is logged, not thrown: the local invalidation already happened, and
    ///     failing the caller would trade a stale read elsewhere for a failed request here.
    /// </summary>
    [Fact]
    public async Task APublishThatFails_DoesNotFailTheInvalidation()
    {
        var stack = CreateStack();
        _subscriber.PublishAsync
            .When(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Throws(new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));

        await stack.InvalidateByTagAsync("glossary");

        _inner.InvalidateByTagAsync.Received(1, "glossary", Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     A category stack is a prefix over the default one, so it already goes through the broadcast
    ///     — and is not wrapped a second time, which would publish each tag twice and each key once
    ///     without its prefix.
    /// </summary>
    [Fact]
    public async Task ACategoryStack_BroadcastsThroughTheDefault_WithItsPrefix_AndOnlyOnce()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ICacheStack>(_inner);
        services.AddPragmaticCaching(cache => cache.ForCategory<TestCategory>(o => o.KeyPrefix = "cat:"));
        services.AddSingleton<IConnectionMultiplexer>(_redis);
        CreateStack();

        services.AddRedisCacheInvalidationBroadcast();

        using var provider = services.BuildServiceProvider();
        await CacheStackProvider.ForCategory<TestCategory>(provider).RemoveAsync("k");

        _subscriber.PublishAsync.Received(1, Arg.Any<RedisChannel>(),
            Arg.Is<RedisValue>(v => IsMessage(v, CacheInvalidationKind.Key, "cat:k")), Arg.Any<CommandFlags>());
        _subscriber.PublishAsync.Received(1, Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public void TheRegistration_DecoratesTheStack_AndStartsTheSubscriber()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ICacheStack>(_inner);
        services.AddSingleton<IConnectionMultiplexer>(_redis);

        services.AddRedisCacheInvalidationBroadcast();

        services.Should().Contain(d => d.ServiceType == typeof(IHostedService)
                                       && d.ImplementationType == typeof(CacheInvalidationSubscriber));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICacheStack>().Should().BeOfType<BroadcastingCacheStack>();
    }
}
