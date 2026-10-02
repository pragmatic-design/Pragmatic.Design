using Pragmatic.Testing.Assertions;
using Pragmatic.Caching;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     Locks the documented (deliberately non-atomic) default of
///     <see cref="ICacheStack.IncrementAsync"/>: read-modify-write over Get/Set.
/// </summary>
public sealed class CacheStackIncrementDefaultTests
{
    private sealed class DictionaryCacheStack : ICacheStack
    {
        public Dictionary<string, object?> Store { get; } = [];
        public CacheEntryOptions? LastSetOptions { get; private set; }

        public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();

        public ValueTask<T> GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => new(Store.TryGetValue(key, out var v) && v is T t ? t : default);

        public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
            => new(Store.TryGetValue(key, out var v) && v is T t ? (true, t) : (false, default(T?)));

        public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
        {
            Store[key] = value;
            LastSetOptions = options;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
        {
            Store.Remove(key);
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default) => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Increment_MissingKey_StartsFromZero()
    {
        var cache = new DictionaryCacheStack();

        var result = await ((ICacheStack)cache).IncrementAsync("counter", 5);

        result.Should().Be(5);
        cache.Store["counter"].Should().Be(5L);
    }

    [Fact]
    public async Task Increment_ExistingValue_AddsDelta()
    {
        var cache = new DictionaryCacheStack { Store = { ["counter"] = 10L } };

        (await ((ICacheStack)cache).IncrementAsync("counter", 3)).Should().Be(13);
    }

    [Fact]
    public async Task Increment_NegativeDelta_Decrements()
    {
        var cache = new DictionaryCacheStack { Store = { ["counter"] = 10L } };

        (await ((ICacheStack)cache).IncrementAsync("counter", -4)).Should().Be(6);
    }

    [Fact]
    public async Task Increment_WithTtl_SetsDurationOnTheEntry()
    {
        var cache = new DictionaryCacheStack();

        await ((ICacheStack)cache).IncrementAsync("counter", 1, TimeSpan.FromMinutes(2));

        cache.LastSetOptions.Should().NotBeNull();
        cache.LastSetOptions!.Duration.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Increment_WithoutTtl_PassesNullOptions()
    {
        var cache = new DictionaryCacheStack();

        await ((ICacheStack)cache).IncrementAsync("counter", 1);

        cache.LastSetOptions.Should().BeNull();
    }
}
