using Pragmatic.Testing.Assertions;
using Pragmatic.Caching;
using Pragmatic.Configuration.Cache;

namespace Pragmatic.Configuration.Tests.Unit;

/// <summary>
///     Tests for InMemoryConfigurationCacheStack via ICacheStack interface.
/// </summary>
public class InMemoryConfigurationCacheTests : IDisposable
{
    private readonly InMemoryConfigurationCacheStack _cache = new();

    [Fact]
    public async Task GetAsync_Empty_ReturnsDefault()
    {
        var result = await _cache.GetAsync<string>("missing");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGet_ReturnsValue()
    {
        await _cache.SetAsync("key1", "value1", CacheEntryOptions.WithDuration(TimeSpan.FromMinutes(5)));

        var result = await _cache.GetAsync<string>("key1");
        result.Should().Be("value1");
    }

    [Fact]
    public async Task SetAsync_Expired_ReturnsDefault()
    {
        await _cache.SetAsync("key1", "value1", CacheEntryOptions.WithDuration(TimeSpan.FromMilliseconds(1)));

        await Task.Delay(50); // wait for expiry

        var result = await _cache.GetAsync<string>("key1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_RemovesEntry()
    {
        await _cache.SetAsync("key1", "value1", CacheEntryOptions.WithDuration(TimeSpan.FromMinutes(5)));
        await _cache.RemoveAsync("key1");

        var result = await _cache.GetAsync<string>("key1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_NonExistent_DoesNotThrow()
    {
        var act = () => _cache.RemoveAsync("missing").AsTask();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetAsync_OverwritesExisting()
    {
        var options = CacheEntryOptions.WithDuration(TimeSpan.FromMinutes(5));
        await _cache.SetAsync("key1", "old", options);
        await _cache.SetAsync("key1", "new", options);

        var result = await _cache.GetAsync<string>("key1");
        result.Should().Be("new");
    }

    [Fact]
    public async Task GetAsync_IntType_ReturnsCorrectType()
    {
        await _cache.SetAsync("count", 42, CacheEntryOptions.WithDuration(TimeSpan.FromMinutes(5)));

        var result = await _cache.GetAsync<int>("count");
        result.Should().Be(42);
    }

    public void Dispose() => _cache.Dispose();
}
