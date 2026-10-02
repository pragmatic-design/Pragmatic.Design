using Pragmatic.Testing.Assertions;
using Pragmatic.Caching;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class CacheStackDefaultsTests
{
    // Records the tag passed to InvalidateByTagAsync so we can assert RemoveByTagAsync
    // (a default interface method) forwards to it.
    private sealed class StubCacheStack : ICacheStack
    {
        public string? LastInvalidatedTag { get; private set; }

        public ValueTask<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<T>> factory,
            CacheEntryOptions? options = null,
            CancellationToken ct = default) => throw new NotImplementedException();

        public ValueTask<T> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, ValueTask<CacheFactoryResult<T>>> factory,
            CacheEntryOptions? options = null,
            CancellationToken ct = default) => throw new NotImplementedException();

        public ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
            => throw new NotImplementedException();

        public ValueTask<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken ct = default)
            => throw new NotImplementedException();

        public ValueTask SetAsync<T>(string key, T value, CacheEntryOptions? options = null, CancellationToken ct = default)
            => throw new NotImplementedException();

        public ValueTask RemoveAsync(string key, CancellationToken ct = default)
            => throw new NotImplementedException();

        public ValueTask InvalidateByTagAsync(string tag, CancellationToken ct = default)
        {
            LastInvalidatedTag = tag;
            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task RemoveByTagAsync_DefaultForwardsToInvalidateByTagAsync()
    {
        var stub = new StubCacheStack();
        ICacheStack cache = stub;

        await cache.RemoveByTagAsync("orders");

        stub.LastInvalidatedTag.Should().Be("orders");
    }
}
