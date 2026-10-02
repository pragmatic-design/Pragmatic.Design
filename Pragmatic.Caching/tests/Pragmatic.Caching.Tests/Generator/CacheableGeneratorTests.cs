using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Generator;

/// <summary>
///     Tests for [Cacheable] attribute source generation.
/// </summary>
public class CacheableGeneratorTests : CachingGeneratorTestBase
{
    [Fact]
    public async Task Cacheable_BasicQuery_GeneratesICacheable()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetUser
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty("Generated code should compile without errors");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public void Cacheable_CollectionKeyMember_SerializesByElement()
    {
        // Regression for the cache-key collision bug: a List<T> key member must be serialized
        // element-by-element. Convert.ToString on a list yields the TYPE NAME (identical for any
        // instance), which would collapse different lists onto one key and serve stale data.
        var source = """
                     using Pragmatic.Caching.Attributes;
                     using System.Collections.Generic;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class SearchByNames
                     {
                         public List<string>? Names { get; init; }
                         public int Page { get; init; }
                     }
                     """;

        var result = RunGenerator(source);
        GetCompilationErrors(result).Should().BeEmpty();

        var cacheSource = GetGeneratedSourcesAsDictionary(result)
            .Single(kv => kv.Key.Contains("SearchByNames") && kv.Key.Contains("Cache")).Value;

        cacheSource.Should().Contain("string.Join(\",\"",
            "collection key members must be joined element-by-element");
        cacheSource.Should().Contain("System.Linq.Enumerable.Select(Names",
            "the collection's elements must be projected, not the collection itself");
        cacheSource.Should().Contain("System.Convert.ToString(Page",
            "scalar members must still use Convert.ToString");
    }

    [Fact]
    public async Task Cacheable_MultipleProperties_GeneratesCorrectKey()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "1h")]
                     public partial class GetUserOrders
                     {
                         public int TenantId { get; init; }
                         public int UserId { get; init; }
                         public bool IncludeArchived { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task Cacheable_WithTags_GeneratesTagsInOptions()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m", Tags = new[] { "users", "orders" })]
                     public partial class GetUserProfile
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task Cacheable_WithTagPlaceholders_ExpandsPlaceholders()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m", Tags = new[] { "users", "tenant:{TenantId}" })]
                     public partial class GetActiveUsers
                     {
                         public int TenantId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task Cacheable_WithSlidingExpiration_GeneratesSlidingOption()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "30m", Sliding = true)]
                     public partial class GetDashboardData
                     {
                         public int UserId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task Cacheable_WithCacheKeyAttribute_ExcludesProperty()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetUserDetails
                     {
                         public int UserId { get; init; }

                         [CacheKey(Exclude = true)]
                         public bool ForceRefresh { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task Cacheable_WithCacheKeyOrder_RespectsOrder()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetOrderItems
                     {
                         [CacheKey(Order = 2)]
                         public int OrderId { get; init; }

                         [CacheKey(Order = 1)]
                         public int CustomerId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    /// <summary>
    ///     One property ordered among unordered ones — the attribute's own documented example.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Two defects met here, and each hid the other. <c>Order</c> was read with
    ///         <c>GetNamedArgument&lt;int&gt;</c>, which yields <b>0</b> for an argument nobody
    ///         wrote — so a bare <c>[CacheKey(Name = "…")]</c> silently ordered its property first,
    ///         and several of them all collided at 0. And the implicit numbering started at 0 too,
    ///         sharing the number space with explicit values, so a single <c>Order = 0</c> collided
    ///         with whatever was declared first. <c>PRAG1750</c> then reported collisions the
    ///         transform had invented, as build errors.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>Every existing test of this feature orders every property</b>
    ///         (<see cref="Cacheable_WithCacheKeyOrder_RespectsOrder" />), which is the one
    ///         arrangement in which the broken rule and the documented one agree.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Cacheable_WithOneOrderedPropertyAmongUnordered_PutsItFirstAndReportsNothing()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetUserOrders
                     {
                         public int UserId { get; init; }

                         [CacheKey(Name = "t", Order = 0)]
                         public int TenantId { get; init; }

                         [CacheKey(Name = "arch")]
                         public bool IncludeArchived { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        GetCompilationErrors(result).Should().BeEmpty();
        HasDiagnostic(result, "PRAG1750").Should().BeFalse(
            "nothing here declares the same order twice — one property declares one");

        var key = GetGeneratedSourcesAsDictionary(result)
            .Single(file => file.Key.Contains("Cache", StringComparison.Ordinal)).Value;

        key.Should().Contain("GetUserOrders:t={",
            "Order = 0 puts the tenant first, ahead of the properties that declare no order");

        key.IndexOf(":arch=", StringComparison.Ordinal).Should().BeGreaterThan(
            key.IndexOf(":UserId=", StringComparison.Ordinal),
            "and the unordered ones keep their declaration order among themselves");
    }

    [Fact]
    public async Task Cacheable_WithCacheKeyName_UsesCustomName()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "5m")]
                     public partial class GetProduct
                     {
                         [CacheKey(Name = "pid")]
                         public int ProductId { get; init; }
                     }
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task Cacheable_OnRecord_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Caching.Attributes;

                     namespace TestNamespace;

                     [Cacheable(Duration = "10m")]
                     public partial record GetCategories(int StoreId);
                     """;

        var result = RunGenerator(source);

        var errors = GetCompilationErrors(result).ToList();
        errors.Should().BeEmpty();

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }
}