using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Extensions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Tests for <see cref="CachingBuilder.ForCategory{TCategory}"/> registration behavior.
/// </summary>
public class CachingBuilderTests : IDisposable
{
    private readonly ServiceCollection _services = new();

    public CachingBuilderTests() => _services.AddHybridCache();

    public void Dispose() { }

    [Fact]
    public void ForCategory_RegistersKeyedICacheStack()
    {
        _services.AddPragmaticCaching(cache =>
        {
            cache.ForCategory<TestCategoryA>(o => o.KeyPrefix = "a:");
        });

        using var provider = _services.BuildServiceProvider();
        var keyed = provider.GetKeyedService<ICacheStack>(typeof(TestCategoryA).FullName);

        keyed.Should().NotBeNull("ForCategory should register a keyed ICacheStack");
    }

    [Fact]
    public async Task ForCategory_WithKeyPrefix_CreatesPrefixedCacheStack()
    {
        _services.AddPragmaticCaching(cache =>
        {
            cache.ForCategory<TestCategoryA>(o => o.KeyPrefix = "pfx:");
        });

        using var provider = _services.BuildServiceProvider();
        var prefixed = provider.GetRequiredKeyedService<ICacheStack>(typeof(TestCategoryA).FullName);
        var defaultStack = provider.GetRequiredService<ICacheStack>();

        // Set via prefixed, verify via default with the prefix applied
        await prefixed.SetAsync("k1", "hello");
        var fromDefault = await defaultStack.GetAsync<string>("pfx:k1");

        fromDefault.Should().Be("hello", "prefixed stack should store with 'pfx:' prefix");
    }

    [Fact]
    public async Task ForCategory_WithCustomDuration_AppliesDefaultDuration()
    {
        _services.AddPragmaticCaching(cache =>
        {
            cache.ForCategory<TestCategoryA>(o =>
            {
                o.KeyPrefix = "dur:";
                o.DefaultDuration = TimeSpan.FromMinutes(30);
            });
        });

        using var provider = _services.BuildServiceProvider();
        var stack = provider.GetRequiredKeyedService<ICacheStack>(typeof(TestCategoryA).FullName);

        // Should work without explicit options — default duration applied internally
        await stack.SetAsync("durkey", 42);
        var result = await stack.GetAsync<int>("durkey");

        result.Should().Be(42);
    }

    [Fact]
    public void MultipleForCategory_Registrations_Coexist()
    {
        _services.AddPragmaticCaching(cache =>
        {
            cache.ForCategory<TestCategoryA>(o => o.KeyPrefix = "a:");
            cache.ForCategory<TestCategoryB>(o => o.KeyPrefix = "b:");
        });

        using var provider = _services.BuildServiceProvider();
        var stackA = provider.GetKeyedService<ICacheStack>(typeof(TestCategoryA).FullName);
        var stackB = provider.GetKeyedService<ICacheStack>(typeof(TestCategoryB).FullName);

        stackA.Should().NotBeNull();
        stackB.Should().NotBeNull();
        stackA.Should().NotBeSameAs(stackB, "each category should get its own instance");
    }

    [Fact]
    public void ForCategory_WithoutExplicitPrefix_AutoGeneratesPrefix()
    {
        _services.AddPragmaticCaching(cache =>
        {
            cache.ForCategory<TestCategoryA>(o =>
            {
                // Do not set KeyPrefix — auto-generate from type name
                o.DefaultDuration = TimeSpan.FromMinutes(1);
            });
        });

        using var provider = _services.BuildServiceProvider();
        var keyed = provider.GetKeyedService<ICacheStack>(typeof(TestCategoryA).FullName);

        // Should be registered even without explicit prefix
        keyed.Should().NotBeNull();
    }

    [Fact]
    public async Task ForCategory_WithoutPrefix_ResolvesAsPrefixedWithAutoName()
    {
        _services.AddPragmaticCaching(cache =>
        {
            cache.ForCategory<TestCategoryA>(o => { });
        });

        using var provider = _services.BuildServiceProvider();
        var prefixed = provider.GetRequiredKeyedService<ICacheStack>(typeof(TestCategoryA).FullName);
        var defaultStack = provider.GetRequiredService<ICacheStack>();

        await prefixed.SetAsync("auto", "val");

        // Auto-prefix is "testcategorya:" (lowercase type name + colon)
        var fromDefault = await defaultStack.GetAsync<string>("testcategorya:auto");
        fromDefault.Should().Be("val");
    }

    // Marker types
    private sealed class TestCategoryA;
    private sealed class TestCategoryB;
}
