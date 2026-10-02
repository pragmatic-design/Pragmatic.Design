using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Caching;
using Pragmatic.Configuration.Cache;

namespace Pragmatic.Configuration.Tests.Unit;

/// <summary>
///     Coverage for the less-exercised <see cref="InMemoryConfigurationCacheStack"/> paths:
///     <c>TryGetAsync</c>, both <c>GetOrSetAsync</c> overloads, and tag-based invalidation.
/// </summary>
public class InMemoryConfigurationCacheAdvancedTests : IDisposable
{
    private readonly InMemoryConfigurationCacheStack _cache = new();

    private static CacheEntryOptions FiveMinutes => CacheEntryOptions.WithDuration(TimeSpan.FromMinutes(5));

    private static CacheEntryOptions WithTags(params string[] tags) =>
        new() { Duration = TimeSpan.FromMinutes(5), Tags = ImmutableArray.Create(tags) };

    [Fact]
    public async Task TryGetAsync_Missing_ReturnsNotFound()
    {
        var (found, value) = await _cache.TryGetAsync<string>("missing");

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public async Task TryGetAsync_Present_ReturnsFoundWithValue()
    {
        await _cache.SetAsync("key", "v", FiveMinutes);

        var (found, value) = await _cache.TryGetAsync<string>("key");

        found.Should().BeTrue();
        value.Should().Be("v");
    }

    [Fact]
    public async Task TryGetAsync_Expired_ReturnsNotFound()
    {
        await _cache.SetAsync("key", "v", CacheEntryOptions.WithDuration(TimeSpan.FromMilliseconds(1)));

        await Task.Delay(50);

        var (found, _) = await _cache.TryGetAsync<string>("key");
        found.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrSetAsync_Miss_InvokesFactoryAndCaches()
    {
        var calls = 0;

        var first = await _cache.GetOrSetAsync("key", _ =>
        {
            calls++;
            return new ValueTask<string>("computed");
        }, FiveMinutes);

        var second = await _cache.GetOrSetAsync("key", _ =>
        {
            calls++;
            return new ValueTask<string>("ignored");
        }, FiveMinutes);

        first.Should().Be("computed");
        second.Should().Be("computed");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryResult_ShouldCache_StoresValue()
    {
        var result = await _cache.GetOrSetAsync<string>("key", _ =>
            new ValueTask<CacheFactoryResult<string>>(CacheFactoryResult<string>.Cache("kept")),
            FiveMinutes);

        result.Should().Be("kept");
        (await _cache.GetAsync<string>("key")).Should().Be("kept");
    }

    [Fact]
    public async Task InvalidateByTagAsync_RemovesAllKeysWithThatTag()
    {
        await _cache.SetAsync("a", "1", WithTags("group"));
        await _cache.SetAsync("b", "2", WithTags("group"));
        await _cache.SetAsync("c", "3", WithTags("other"));

        await _cache.InvalidateByTagAsync("group");

        (await _cache.GetAsync<string>("a")).Should().BeNull();
        (await _cache.GetAsync<string>("b")).Should().BeNull();
        (await _cache.GetAsync<string>("c")).Should().Be("3");
    }

    [Fact]
    public async Task InvalidateByTagAsync_UnknownTag_IsNoOp()
    {
        await _cache.SetAsync("a", "1", WithTags("group"));

        await _cache.InvalidateByTagAsync("does-not-exist");

        (await _cache.GetAsync<string>("a")).Should().Be("1");
    }

    [Fact]
    public async Task InvalidateByTagsAsync_RemovesKeysAcrossMultipleTags()
    {
        await _cache.SetAsync("a", "1", WithTags("t1"));
        await _cache.SetAsync("b", "2", WithTags("t2"));
        await _cache.SetAsync("c", "3", WithTags("t3"));

        await _cache.InvalidateByTagsAsync(["t1", "t2"]);

        (await _cache.GetAsync<string>("a")).Should().BeNull();
        (await _cache.GetAsync<string>("b")).Should().BeNull();
        (await _cache.GetAsync<string>("c")).Should().Be("3");
    }

    [Fact]
    public async Task RemoveAsync_TaggedKey_ThenInvalidateTag_IsSafe()
    {
        await _cache.SetAsync("a", "1", WithTags("group"));
        await _cache.RemoveAsync("a");

        // Invalidating the now-empty tag must not resurrect or fail.
        await _cache.InvalidateByTagAsync("group");
        (await _cache.GetAsync<string>("a")).Should().BeNull();
    }

    public void Dispose() => _cache.Dispose();
}
