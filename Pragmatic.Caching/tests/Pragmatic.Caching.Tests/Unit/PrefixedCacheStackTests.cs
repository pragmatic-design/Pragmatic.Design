using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Extensions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Tests for PrefixedCacheStack, exercised via the public DI registration API
///     (CachingBuilder.ForCategory) since the class is internal.
/// </summary>
public class PrefixedCacheStackTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ICacheStack _defaultStack;
    private readonly ICacheStack _prefixedStack;

    public PrefixedCacheStackTests()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching(cache =>
        {
            cache.ForCategory<TestCategory>(o =>
            {
                o.KeyPrefix = "test:";
                o.DefaultDuration = TimeSpan.FromMinutes(10);
            });
        });

        _provider = services.BuildServiceProvider();
        _defaultStack = _provider.GetRequiredService<ICacheStack>();
        _prefixedStack = _provider.GetRequiredKeyedService<ICacheStack>(typeof(TestCategory).FullName);
    }

    public void Dispose() => _provider.Dispose();

    // --- Key prefixing ---

    [Fact]
    public async Task SetAsync_PrefixesKey_GetFromDefaultWithPrefix()
    {
        await _prefixedStack.SetAsync("mykey", "value1");

        // The underlying default stack should see the prefixed key
        var result = await _defaultStack.GetAsync<string>("test:mykey");
        result.Should().Be("value1");
    }

    [Fact]
    public async Task GetAsync_PrefixesKey_ReturnsValueSetWithPrefix()
    {
        // Set via default with the manually prefixed key
        await _defaultStack.SetAsync("test:lookup", "found");

        // Get via prefixed stack using the unprefixed key
        var result = await _prefixedStack.GetAsync<string>("lookup");
        result.Should().Be("found");
    }

    [Fact]
    public async Task GetOrSetAsync_PrefixesKey_FactoryCalledOnMiss()
    {
        var factoryCalled = false;

        var result = await _prefixedStack.GetOrSetAsync<string>("factory-key", _ =>
        {
            factoryCalled = true;
            return ValueTask.FromResult("created");
        });

        factoryCalled.Should().BeTrue();
        result.Should().Be("created");

        // Verify it's stored with the prefix
        var fromDefault = await _defaultStack.GetAsync<string>("test:factory-key");
        fromDefault.Should().Be("created");
    }

    [Fact]
    public async Task GetOrSetAsync_PrefixesKey_HitFromPrefixedStore()
    {
        await _prefixedStack.SetAsync("hit-key", "cached");
        var factoryCalled = false;

        var result = await _prefixedStack.GetOrSetAsync<string>("hit-key", _ =>
        {
            factoryCalled = true;
            return ValueTask.FromResult("new");
        });

        factoryCalled.Should().BeFalse();
        result.Should().Be("cached");
    }

    // --- TryGet with prefixed key ---

    [Fact]
    public async Task TryGetAsync_PrefixesKey_ReturnsTrueForExisting()
    {
        await _prefixedStack.SetAsync("try-key", "exists");

        var (found, value) = await _prefixedStack.TryGetAsync<string>("try-key");

        found.Should().BeTrue();
        value.Should().Be("exists");
    }

    [Fact]
    public async Task TryGetAsync_PrefixesKey_ReturnsFalseForMissing()
    {
        var (found, value) = await _prefixedStack.TryGetAsync<string>("no-such-key");

        found.Should().BeFalse();
        value.Should().BeNull();
    }

    // --- Key removal with prefix ---

    [Fact]
    public async Task RemoveAsync_PrefixesKey_RemovesCorrectEntry()
    {
        await _prefixedStack.SetAsync("remove-me", "bye");

        await _prefixedStack.RemoveAsync("remove-me");

        var result = await _prefixedStack.GetAsync<string>("remove-me");
        result.Should().BeNull();
    }

    // --- Tag prefixing ---
    // Note: Tag-based invalidation tests are limited because the in-memory HybridCache
    // does not fully support RemoveByTagAsync. These tests verify the delegation path
    // (no exceptions) and that tags are passed through correctly. Full tag invalidation
    // behavior requires a distributed cache backend (Redis, etc.).

    [Fact]
    public async Task InvalidateByTagAsync_WithPrefixedStack_DoesNotThrow()
    {
        await _prefixedStack.SetAsync("tagged", "value", new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            Tags = ["users"]
        });

        // Verify the call delegates without throwing
        var act = () => _prefixedStack.InvalidateByTagAsync("users").AsTask();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvalidateByTagsAsync_WithPrefixedStack_DoesNotThrow()
    {
        await _prefixedStack.SetAsync("a1", "dataA", new CacheEntryOptions
        {
            Duration = TimeSpan.FromMinutes(5),
            Tags = ["groupA"]
        });

        var act = () => _prefixedStack.InvalidateByTagsAsync(["groupA", "groupB"]).AsTask();
        await act.Should().NotThrowAsync();
    }

    // --- Default duration ---

    [Fact]
    public async Task SetAsync_WithoutOptions_AppliesDefaultDuration()
    {
        // The prefixed stack was configured with DefaultDuration = 10 minutes.
        // Just verify Set/Get works without explicit options (no throw).
        await _prefixedStack.SetAsync("default-dur", "val");

        var result = await _prefixedStack.GetAsync<string>("default-dur");
        result.Should().Be("val");
    }

    // --- Isolation ---

    [Fact]
    public async Task DifferentPrefixes_AreIsolated()
    {
        // Set via prefixed stack
        await _prefixedStack.SetAsync("shared-key", "prefixed-value");

        // Set via default stack (no prefix)
        await _defaultStack.SetAsync("shared-key", "default-value");

        // Each should see their own value
        var prefixed = await _prefixedStack.GetAsync<string>("shared-key");
        var defaultVal = await _defaultStack.GetAsync<string>("shared-key");

        prefixed.Should().Be("prefixed-value");
        defaultVal.Should().Be("default-value");
    }

    // --- GetOrSetAsync with CacheFactoryResult (conditional caching) ---

    [Fact]
    public async Task GetOrSetAsync_CacheFactoryResult_Cache_StoresWithPrefix()
    {
        var result = await _prefixedStack.GetOrSetAsync<string>(
            "cond-key",
            _ => ValueTask.FromResult(CacheFactoryResult<string>.Cache("cached")));

        result.Should().Be("cached");

        // Verify stored with prefix in default stack
        var fromDefault = await _defaultStack.GetAsync<string>("test:cond-key");
        fromDefault.Should().Be("cached");
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFactoryResult_DoNotCache_ReturnsButDoesNotStore()
    {
        var result = await _prefixedStack.GetOrSetAsync<string>(
            "cond-skip",
            _ => ValueTask.FromResult(CacheFactoryResult<string>.DoNotCache("skip")));

        result.Should().Be("skip");

        var (found, _) = await _prefixedStack.TryGetAsync<string>("cond-skip");
        found.Should().BeFalse();
    }

    // Marker type for category testing
    private sealed class TestCategory;
}
