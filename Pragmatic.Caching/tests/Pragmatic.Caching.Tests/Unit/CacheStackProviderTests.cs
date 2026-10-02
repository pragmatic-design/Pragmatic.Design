using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Extensions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Tests for <see cref="CacheStackProvider"/> resolution behavior.
/// </summary>
public class CacheStackProviderTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public CacheStackProviderTests()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching(cache =>
        {
            cache.ForCategory<RegisteredCategory>(o => o.KeyPrefix = "reg:");
        });
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public void ForCategory_Generic_ResolvesKeyedService_WhenRegistered()
    {
        var stack = CacheStackProvider.ForCategory<RegisteredCategory>(_provider);

        stack.Should().NotBeNull();
        // Should be the prefixed variant, not the default HybridCacheStack
        stack.Should().NotBeOfType<HybridCacheStack>(
            "a registered category should resolve to PrefixedCacheStack, not the raw default");
    }

    [Fact]
    public void ForCategory_Generic_FallsBackToDefault_WhenNotRegistered()
    {
        var stack = CacheStackProvider.ForCategory<UnregisteredCategory>(_provider);

        stack.Should().NotBeNull();
        stack.Should().BeOfType<HybridCacheStack>(
            "an unregistered category should fall back to the default ICacheStack");
    }

    [Fact]
    public void ForCategory_Generic_NullServiceProvider_Throws()
    {
        var act = () => CacheStackProvider.ForCategory<RegisteredCategory>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ForCategory_ByType_ResolvesKeyedService_WhenRegistered()
    {
        var stack = CacheStackProvider.ForCategory(_provider, typeof(RegisteredCategory));

        stack.Should().NotBeNull();
        stack.Should().NotBeOfType<HybridCacheStack>();
    }

    [Fact]
    public void ForCategory_ByType_FallsBackToDefault_WhenNotRegistered()
    {
        var stack = CacheStackProvider.ForCategory(_provider, typeof(UnregisteredCategory));

        stack.Should().NotBeNull();
        stack.Should().BeOfType<HybridCacheStack>();
    }

    [Fact]
    public void ForCategory_ByType_NullServiceProvider_Throws()
    {
        var act = () => CacheStackProvider.ForCategory(null!, typeof(RegisteredCategory));

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ForCategory_ByType_NullCategoryType_Throws()
    {
        var act = () => CacheStackProvider.ForCategory(_provider, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // Marker types
    private sealed class RegisteredCategory;
    private sealed class UnregisteredCategory;
}
