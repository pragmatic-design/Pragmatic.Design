using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Caching;
using Pragmatic.Endpoints.AspNetCore;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

public class PragmaticOutputCacheStoreTests
{
    private const string KeyPrefix = "outputcache:";
    private const string TagPrefix = "outputcache:tag:";

    private static (PragmaticOutputCacheStore Store, CacheStackMock Cache) CreateWithCache()
    {
        var cache = new CacheStackMock();
        var services = new ServiceCollection();
        services.AddSingleton<ICacheStack>(cache);
        var provider = services.BuildServiceProvider();
        return (new PragmaticOutputCacheStore(provider), cache);
    }

    [Fact]
    public async Task GetAsync_PrefixesKeyBeforeReadingFromCache()
    {
        var (store, cache) = CreateWithCache();
        cache.GetAsync.Returns<byte[]>(new ValueTask<byte[]>([1, 2, 3]));

        var result = await store.GetAsync("page-1", CancellationToken.None);

        result.Should().Equal(1, 2, 3);
        cache.GetAsync.Received<byte[]>(1, a => Equals(a[0], KeyPrefix + "page-1"));
    }

    [Fact]
    public async Task GetAsync_CacheMiss_ReturnsNull()
    {
        var (store, cache) = CreateWithCache();
        cache.GetAsync.Returns<byte[]>(new ValueTask<byte[]>((byte[])null!));

        var result = await store.GetAsync("missing", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_PrefixesKeyAndSetsDuration()
    {
        var (store, cache) = CreateWithCache();
        var validFor = TimeSpan.FromMinutes(5);

        await store.SetAsync("page-1", [9, 9], tags: null, validFor, CancellationToken.None);

        cache.SetAsync.Received<byte[]>(1, a => Equals(a[0], KeyPrefix + "page-1") && a[2] is CacheEntryOptions o && o.Duration == validFor);
    }

    [Fact]
    public async Task SetAsync_WithTags_PrefixesEachTag()
    {
        var (store, cache) = CreateWithCache();

        await store.SetAsync(
            "page-1", [1], tags: ["products", "catalog"], TimeSpan.FromMinutes(1), CancellationToken.None);

        cache.SetAsync.Received<byte[]>(1, a => a[2] is CacheEntryOptions o
            && o.Tags.Contains(TagPrefix + "products") && o.Tags.Contains(TagPrefix + "catalog"));
    }

    [Fact]
    public async Task SetAsync_WithNullTags_SetsEmptyTagList()
    {
        var (store, cache) = CreateWithCache();

        await store.SetAsync("page-1", [1], tags: null, TimeSpan.FromMinutes(1), CancellationToken.None);

        cache.SetAsync.Received<byte[]>(1, a => a[2] is CacheEntryOptions o && o.Tags.Length == 0);
    }

    [Fact]
    public async Task EvictByTagAsync_PrefixesTagBeforeInvalidating()
    {
        var (store, cache) = CreateWithCache();

        await store.EvictByTagAsync("products", CancellationToken.None);

        cache.InvalidateByTagAsync.Received(1, TagPrefix + "products", Arg.Any<CancellationToken>());
    }

    // When Pragmatic.Caching is NOT registered, the store must degrade gracefully
    // (no-op) as documented, not throw InvalidOperationException from GetRequiredService.

    private static PragmaticOutputCacheStore CreateWithoutCache()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        return new PragmaticOutputCacheStore(provider);
    }

    [Fact]
    public async Task GetAsync_WhenCachingNotRegistered_ReturnsNullWithoutThrowing()
    {
        var store = CreateWithoutCache();

        var result = await store.GetAsync("page-1", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_WhenCachingNotRegistered_IsNoOpWithoutThrowing()
    {
        var store = CreateWithoutCache();

        var act = () => store.SetAsync(
            "page-1", [1], tags: null, TimeSpan.FromMinutes(1), CancellationToken.None).AsTask();

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task EvictByTagAsync_WhenCachingNotRegistered_IsNoOpWithoutThrowing()
    {
        var store = CreateWithoutCache();

        var act = () => store.EvictByTagAsync("products", CancellationToken.None).AsTask();

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }
}
