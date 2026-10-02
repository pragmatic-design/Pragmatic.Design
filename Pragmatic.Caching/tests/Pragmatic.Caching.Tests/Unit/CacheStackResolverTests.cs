using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Extensions;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Routing a category to its own <see cref="ICacheStack"/>.
/// </summary>
/// <remarks>
///     What these are for: the generator emits <c>CacheCategory</c> on a <c>[Cacheable]</c> query and
///     <c>InvalidationCategory</c> on an <c>[InvalidatesCache]</c> mutation, and for a while nothing
///     read either — so every declared type used the default stack and a category's key prefix and
///     duration applied to nothing. These assert the routing itself, on the same resolver the query
///     executor and the mutation invoker go through, so the two cannot drift apart again.
/// </remarks>
public sealed class CacheStackResolverTests
{
    private sealed class Analytics;

    private sealed class NeverRegistered;

    private static ServiceProvider BuildWithCategory()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching(cache =>
            cache.ForCategory<Analytics>(o => o.KeyPrefix = "analytics:"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Resolve_KnownCategory_ReturnsThatCategorysStack_NotTheDefault()
    {
        using var sp = BuildWithCategory();
        var resolver = sp.GetRequiredService<ICacheStackResolver>();

        var categoryStack = resolver.ForQuery(typeof(Analytics));

        categoryStack.Should().NotBeNull();
        categoryStack.Should().NotBeSameAs(sp.GetRequiredService<ICacheStack>(),
            "a category routes to its own prefixed stack, not the shared default one");
    }

    [Fact]
    public async Task Resolve_KnownCategory_WritesUnderThatCategorysPrefix()
    {
        // The property the whole feature exists for, and the one a routing regression breaks first:
        // a value written through the category is reachable from the default stack only under the
        // prefixed key. Asserting the instance differs would not catch a prefix that stopped being
        // applied.
        using var sp = BuildWithCategory();
        var resolver = sp.GetRequiredService<ICacheStackResolver>();
        var categoryStack = resolver.ForQuery(typeof(Analytics))!;
        var defaultStack = sp.GetRequiredService<ICacheStack>();

        await categoryStack.SetAsync("report", 42);

        (await defaultStack.GetAsync<int>("analytics:report")).Should().Be(42,
            "the category stack prefixes the key it writes");
        (await defaultStack.GetAsync<int>("report")).Should().Be(0,
            "and nothing is written under the unprefixed key");
    }

    [Fact]
    public async Task Resolve_SameCategory_InvalidatesWhatItWrote()
    {
        // Read and invalidation must route identically. Otherwise an invalidation issued for a
        // category addresses the unprefixed namespace and leaves the entry in place.
        using var sp = BuildWithCategory();
        var resolver = sp.GetRequiredService<ICacheStackResolver>();
        var writer = resolver.ForQuery(typeof(Analytics))!;
        var invalidator = resolver.ForQuery(typeof(Analytics))!;

        await writer.SetAsync("report", 42);
        await invalidator.RemoveAsync("report");

        (await sp.GetRequiredService<ICacheStack>().GetAsync<int>("analytics:report")).Should().Be(0,
            "the invalidation reached the same namespace the write went to");
    }

    [Fact]
    public void Resolve_NoCategory_ReturnsTheDefaultStack()
    {
        using var sp = BuildWithCategory();

        sp.GetRequiredService<ICacheStackResolver>().ForQuery(null)
            .Should().BeSameAs(sp.GetRequiredService<ICacheStack>());
    }

    [Fact]
    public void Resolve_UnregisteredCategory_FallsBackToTheDefaultStack()
    {
        // Not an error: nobody called ForCategory for it, so its entries were written to the default
        // stack and have to be invalidated there. Both paths fall back the same way.
        using var sp = BuildWithCategory();

        sp.GetRequiredService<ICacheStackResolver>().ForQuery(typeof(NeverRegistered))
            .Should().BeSameAs(sp.GetRequiredService<ICacheStack>());
    }

    [Fact]
    public void ForInvalidation_NoCategory_ReturnsTheDefaultAndEveryRegisteredCategory()
    {
        using var sp = BuildWithCategory();

        var all = sp.GetRequiredService<ICacheStackResolver>().ForInvalidation(null);

        all.Should().HaveCount(2, "the default stack plus the one registered category");
        all.Should().Contain(sp.GetRequiredService<ICacheStack>());
        all.Should().Contain(sp.GetRequiredKeyedService<ICacheStack>(typeof(Analytics).FullName));
    }

    [Fact]
    public void ForInvalidation_WithNoCategoriesConfigured_IsJustTheDefaultStack()
    {
        // A broadcast invalidation in a deployment that never used categories must stay one call.
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching();
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<ICacheStackResolver>().ForInvalidation(null)
            .Should().ContainSingle().Which.Should().BeSameAs(sp.GetRequiredService<ICacheStack>());
    }

    // =========================================================================
    // The two options take effect, not only reach IOptions
    // =========================================================================

    private static ServiceProvider Build(Action<CachingOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void ForQuery_WhenQueryCachingIsDisabled_ReturnsNothingToCacheWith()
    {
        // Asserting that EnableQueryCaching reaches IOptions proves nothing: an option copied there
        // and read by nobody passes that check while every [Cacheable] query stays cached. This
        // asserts the effect.
        using var sp = Build(o => o.EnableQueryCaching = false);

        sp.GetRequiredService<ICacheStackResolver>().ForQuery(null).Should().BeNull(
            "the executor caches through this and nothing else, so a null here is caching switched off");
    }

    [Fact]
    public void ForQuery_WhenQueryCachingIsEnabled_ReturnsTheStack()
    {
        using var sp = Build(o => o.EnableQueryCaching = true);

        sp.GetRequiredService<ICacheStackResolver>().ForQuery(null).Should().NotBeNull();
    }

    [Fact]
    public void ForInvalidation_WhenInvalidationIsDisabled_ReachesNothing()
    {
        using var sp = Build(o => o.EnableEventInvalidation = false);

        sp.GetRequiredService<ICacheStackResolver>().ForInvalidation(null).Should().BeEmpty();
    }

    [Fact]
    public void Flags_AreVisibleSoTheCallerCanTellDisabledFromUnregistered()
    {
        // The invoker warns when a mutation asked to invalidate and no stack exists, and must not
        // warn when invalidation was switched off on purpose. Both look like "no stacks" otherwise.
        using var sp = Build(o =>
        {
            o.EnableQueryCaching = false;
            o.EnableEventInvalidation = false;
        });
        var resolver = sp.GetRequiredService<ICacheStackResolver>();

        resolver.QueryCachingEnabled.Should().BeFalse();
        resolver.InvalidationEnabled.Should().BeFalse();
    }
}
