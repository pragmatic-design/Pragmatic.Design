using Pragmatic.Testing.Assertions;

using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Context;

/// <summary>
///     Tests for the CachedConfigProvider base class.
/// </summary>
public class CachedConfigProviderTests
{
    // The cache is shared per concrete provider type (survives scoped-lifetime re-creation).
    // Reset it before each test so static state does not leak between tests.
    public CachedConfigProviderTests()
    {
        new TestCachedProvider("reset", null).ClearAllCache();
        new NullKeyProvider().ClearAllCache();
    }

    #region Test Provider Implementation

    private sealed class TestCachedProvider(
        string cacheKey,
        I18NConfig? config,
        TimeSpan? cacheDuration = null)
        : CachedConfigProvider(cacheDuration)
    {
        public int LoadCount { get; private set; }

        public override int Priority => 100;

        protected override string? GetCacheKey() => cacheKey;

        protected override I18NConfig? LoadConfiguration()
        {
            LoadCount++;
            return config;
        }
    }

    private sealed class NullKeyProvider : CachedConfigProvider
    {
        public int LoadCount { get; private set; }

        public override int Priority => 100;

        protected override string? GetCacheKey() => null;

        protected override I18NConfig? LoadConfiguration()
        {
            LoadCount++;
            return new I18NConfig { DefaultUICulture = CultureCode.English };
        }
    }

    #endregion

    #region Caching Behavior

    [Fact]
    public void GetConfiguration_FirstCall_LoadsFromSource()
    {
        var config = new I18NConfig { DefaultUICulture = CultureCode.Italian };
        var provider = new TestCachedProvider("test-key", config);

        var result = provider.GetConfiguration();

        result.Should().NotBeNull();
        result!.DefaultUICulture.Should().Be(CultureCode.Italian);
        provider.LoadCount.Should().Be(1);
    }

    [Fact]
    public void GetConfiguration_SecondCall_UsesCache()
    {
        var config = new I18NConfig { DefaultUICulture = CultureCode.Italian };
        var provider = new TestCachedProvider("test-key-2", config);

        provider.GetConfiguration();
        provider.GetConfiguration();
        provider.GetConfiguration();

        provider.LoadCount.Should().Be(1, "should load only once due to caching");
    }

    [Fact]
    public void GetConfiguration_AfterInvalidate_ReloadsFromSource()
    {
        var config = new I18NConfig { DefaultUICulture = CultureCode.Italian };
        var provider = new TestCachedProvider("test-key-3", config);

        provider.GetConfiguration();
        provider.LoadCount.Should().Be(1);

        provider.InvalidateCache();

        provider.GetConfiguration();
        provider.LoadCount.Should().Be(2, "should reload after cache invalidation");
    }

    [Fact]
    public void GetConfiguration_NullCacheKey_DoesNotCache()
    {
        var provider = new NullKeyProvider();

        provider.GetConfiguration();
        provider.GetConfiguration();
        provider.GetConfiguration();

        provider.LoadCount.Should().Be(3, "should load every time when cache key is null");
    }

    [Fact]
    public void GetConfiguration_NullConfig_CachesNullValue()
    {
        var provider = new TestCachedProvider("test-null-config", null);

        var result1 = provider.GetConfiguration();
        var result2 = provider.GetConfiguration();

        result1.Should().BeNull();
        result2.Should().BeNull();
        provider.LoadCount.Should().Be(1, "should cache null values too");
    }

    #endregion

    #region Cache Expiration

    [Fact]
    public async Task GetConfiguration_AfterExpiration_ReloadsFromSource()
    {
        var config = new I18NConfig { DefaultUICulture = CultureCode.German };
        var provider = new TestCachedProvider(
            "test-expiration",
            config,
            cacheDuration: TimeSpan.FromMilliseconds(50));

        provider.GetConfiguration();
        provider.LoadCount.Should().Be(1);

        // Wait for cache to expire
        await Task.Delay(100);

        provider.GetConfiguration();
        provider.LoadCount.Should().Be(2, "should reload after expiration");
    }

    #endregion

    #region Instance Invalidation

    [Fact]
    public void InvalidateCache_ByKey_RemovesOnlyThatKey()
    {
        var config = new I18NConfig { DefaultUICulture = CultureCode.French };
        var provider1 = new TestCachedProvider("key-1", config);
        var provider2 = new TestCachedProvider("key-2", config);

        provider1.GetConfiguration();
        provider2.GetConfiguration();
        provider1.LoadCount.Should().Be(1);
        provider2.LoadCount.Should().Be(1);

        // Invalidate only key-1
        provider1.InvalidateCache("key-1");

        provider1.GetConfiguration();
        provider2.GetConfiguration();

        provider1.LoadCount.Should().Be(2, "key-1 was invalidated");
        provider2.LoadCount.Should().Be(1, "key-2 entry was untouched");
    }

    #endregion

    #region Shared Cache Across Instances (scoped-lifetime safety)

    [Fact]
    public void SameType_SameKey_ShareCacheAcrossInstances()
    {
        // Two instances of the same provider type with the same cache key hit ONE shared cache. This is what makes the TTL effective when the
        // provider is registered scoped (a fresh instance per request must reuse the cache).
        var config = new I18NConfig { DefaultUICulture = CultureCode.Japanese };
        var provider1 = new TestCachedProvider("shared-key", config);
        var provider2 = new TestCachedProvider("shared-key", config);

        provider1.GetConfiguration();
        provider1.LoadCount.Should().Be(1);

        // provider2 reads provider1's cached entry — no second load.
        provider2.GetConfiguration();
        provider2.LoadCount.Should().Be(0, "the second instance reuses the shared cache");
    }

    [Fact]
    public void SameType_DifferentKey_DoNotLeakAcrossTenants()
    {
        // Cross-tenant isolation is preserved by the cache KEY, not by instance identity.
        var config = new I18NConfig { DefaultUICulture = CultureCode.Japanese };
        var tenantA = new TestCachedProvider("tenant:A", config);
        var tenantB = new TestCachedProvider("tenant:B", config);

        tenantA.GetConfiguration();
        tenantB.GetConfiguration();

        tenantA.LoadCount.Should().Be(1);
        tenantB.LoadCount.Should().Be(1, "a different key does not read tenant A's entry");
    }

    #endregion
}
