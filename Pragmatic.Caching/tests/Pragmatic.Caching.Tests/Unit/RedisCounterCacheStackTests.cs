using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Caching.Redis;
using StackExchange.Redis;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

public class RedisCounterCacheStackTests
{
    private readonly CacheStackMock _inner = new CacheStackMock();
    private readonly ConnectionMultiplexerMock _redis = new ConnectionMultiplexerMock();
    private readonly DatabaseMock _db = new DatabaseMock();

    private RedisCounterCacheStack CreateStack()
    {
        _redis.GetDatabase.Returns(_db);
        return new RedisCounterCacheStack(_inner, _redis);
    }

    [Fact]
    public async Task IncrementAsync_EvaluatesAtomicScript_AndReturnsServerValue()
    {
        var stack = CreateStack();
        _db.ScriptEvaluateAsync4.Returns(Task.FromResult(RedisResult.Create((RedisValue)7L)));

        var result = await stack.IncrementAsync("ratelimit:ip:1", 1, TimeSpan.FromMinutes(1));

        result.Should().Be(7);
        _db.ScriptEvaluateAsync4.Received(1, Arg.Is<string>(s => s.Contains("INCRBY") && s.Contains("PEXPIRE")), Arg.Is<RedisKey[]?>(k => k!.Length == 1 && k[0] == "ratelimit:ip:1"), Arg.Is<RedisValue[]?>(v => v!.Length == 2 && (long)v[0] == 1L && (long)v[1] == 60_000L), Arg.Any<CommandFlags>());
        // Counters must never go through the inner (per-process) stack.
        _inner.IncrementAsync.DidNotReceive();
    }

    [Fact]
    public async Task IncrementAsync_WithoutTtl_PassesMinusOne_SoExpiryIsNotTouched()
    {
        var stack = CreateStack();
        _db.ScriptEvaluateAsync4.Returns(Task.FromResult(RedisResult.Create((RedisValue)(-3L))));

        var result = await stack.IncrementAsync("counter", -3);

        result.Should().Be(-3);
        _db.ScriptEvaluateAsync4.Received(1, Arg.Any<string>(), Arg.Any<RedisKey[]?>(), Arg.Is<RedisValue[]?>(v => (long)v![1] == -1L), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task NonCounterMembers_DelegateToInner()
    {
        var stack = CreateStack();
        // A generic member is configured per closed type argument, with the constructed return type.
        // The constructed return type as the generated signature renders it: generic signatures carry
        // no nullable annotations (they cannot — see the generator), so it is ValueTask<int>.
        _inner.GetAsync.Returns<int>(new ValueTask<int>(42));

        (await stack.GetAsync<int>("k")).Should().Be(42);

        await stack.RemoveAsync("k");
        _inner.RemoveAsync.Received(1, "k", Arg.Any<CancellationToken>());

        await stack.InvalidateByTagAsync("tag");
        _inner.InvalidateByTagAsync.Received(1, "tag", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddRedisAtomicCounters_DecoratesRegisteredCacheStack()
    {
        var services = new ServiceCollection();
        // Registered AS the interface: the variable is a CacheStackMock now, and AddSingleton infers
        // the service type from it — which would register the mock class and leave ICacheStack unbound.
        services.AddSingleton<ICacheStack>(_inner);
        services.AddSingleton<IConnectionMultiplexer>(_redis);

        services.AddRedisAtomicCounters();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICacheStack>().Should().BeOfType<RedisCounterCacheStack>();
    }
}
