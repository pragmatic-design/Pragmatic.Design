using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.Query.Filters;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

public class FilterMapComposerTests
{
    [Fact]
    public void Compose_NoFactoryNoProviders_ReturnsEmpty()
    {
        var composer = new FilterMapComposer(
            new QueryFilterToggle(),
            Array.Empty<IVisibilityFilterProvider>());

        var result = composer.Compose(FilterContext.At(DateTimeOffset.UnixEpoch));

        result.HasFilters.Should().BeFalse();
    }

    [Fact]
    public void Compose_WithStaticFactory_ReturnsStaticFilters()
    {
        FilterMap Factory(FilterContext ctx) => new(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = (Expression<Func<Invoice, bool>>)(e => !e.IsDeleted)
        });

        var composer = new FilterMapComposer(
            new QueryFilterToggle(),
            Array.Empty<IVisibilityFilterProvider>(),
            [Factory]);

        var result = composer.Compose(FilterContext.At(DateTimeOffset.UnixEpoch));

        result.HasFilterFor<Invoice>().Should().BeTrue();
    }

    [Fact]
    public void Compose_WithVisibilityProvider_MergesDynamicFilters()
    {
        var provider = new TestVisibilityProvider(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = (Expression<Func<Invoice, bool>>)(e => e.OwnerId == "user1")
        });

        var composer = new FilterMapComposer(
            new QueryFilterToggle(),
            new[] { provider });

        var result = composer.Compose(FilterContext.At(DateTimeOffset.UnixEpoch));

        result.HasFilterFor<Invoice>().Should().BeTrue();
    }

    [Fact]
    public void Compose_StaticAndDynamic_MergesBoth()
    {
        FilterMap Factory(FilterContext ctx) => new(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = (Expression<Func<Invoice, bool>>)(e => !e.IsDeleted)
        });

        var provider = new TestVisibilityProvider(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = (Expression<Func<Invoice, bool>>)(e => e.OwnerId == "user1")
        });

        var composer = new FilterMapComposer(
            new QueryFilterToggle(),
            new[] { provider },
            [Factory]);

        var result = composer.Compose(FilterContext.At(DateTimeOffset.UnixEpoch));

        result.HasFilterFor<Invoice>().Should().BeTrue();
        result.TryGetFilter<Invoice>(out var filter).Should().BeTrue();
        // Should be AND-combined (two filters merged)
        filter!.Body.NodeType.Should().Be(ExpressionType.AndAlso);
    }

    [Fact]
    public void Compose_AllDisabled_ReturnsEmpty()
    {
        FilterMap Factory(FilterContext ctx) => new(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = (Expression<Func<Invoice, bool>>)(e => !e.IsDeleted)
        });

        var toggle = new QueryFilterToggle();
        var composer = new FilterMapComposer(
            toggle,
            Array.Empty<IVisibilityFilterProvider>(),
            [Factory]);

        using (toggle.DisableAll())
        {
            var result = composer.Compose(FilterContext.At(DateTimeOffset.UnixEpoch));
            result.HasFilters.Should().BeFalse();
        }
    }

    [Fact]
    public void Compose_SkipVisibility_IgnoresDynamicProviders()
    {
        var provider = new TestVisibilityProvider(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = (Expression<Func<Invoice, bool>>)(e => e.OwnerId == "user1")
        });

        var composer = new FilterMapComposer(
            new QueryFilterToggle(),
            new[] { provider });

        var result = composer.Compose(FilterContext.At(DateTimeOffset.UnixEpoch) with { Mode = FilterMode.Admin });

        // Admin mode skips visibility → dynamic provider should be ignored
        result.HasFilters.Should().BeFalse();
    }

    [Fact]
    public void Compose_DisabledFilterType_RemovesFromResult()
    {
        FilterMap Factory(FilterContext ctx) => new(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = (Expression<Func<Invoice, bool>>)(e => !e.IsDeleted),
            [typeof(Payment)] = (Expression<Func<Payment, bool>>)(e => !e.IsDeleted)
        });

        var disabled = new HashSet<Type> { typeof(Invoice) };
        var composer = new FilterMapComposer(
            new QueryFilterToggle(),
            Array.Empty<IVisibilityFilterProvider>(),
            [Factory]);

        var result = composer.Compose(FilterContext.At(DateTimeOffset.UnixEpoch) with { DisabledFilters = disabled });

        result.HasFilterFor<Invoice>().Should().BeFalse();
        result.HasFilterFor<Payment>().Should().BeTrue();
    }

    [Fact]
    public void ApplyNavigationFilters_EmptyMap_ReturnsSameQuery()
    {
        var composer = new FilterMapComposer(
            new QueryFilterToggle(),
            Array.Empty<IVisibilityFilterProvider>());

        var query = new List<Invoice>().AsQueryable();

        var result = composer.ApplyNavigationFilters(query, FilterContext.At(DateTimeOffset.UnixEpoch));

        // Same reference — short-circuited
        ReferenceEquals(result, query).Should().BeTrue();
    }

    // Test stubs
    private sealed class Invoice
    {
        public bool IsDeleted { get; set; }
        public string OwnerId { get; set; } = "";
    }

    private sealed class Payment
    {
        public bool IsDeleted { get; set; }
    }

    private sealed class TestVisibilityProvider(
        IReadOnlyDictionary<Type, LambdaExpression> filters) : IVisibilityFilterProvider
    {
        public IReadOnlyDictionary<Type, LambdaExpression> GetFilters(FilterContext context) => filters;
    }

    /// <summary>
    ///     Every module's map is merged, not raced.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each module generates its own <c>FilterMapRegistry</c> and registers a factory for it.
    ///         Registered through <c>TryAdd</c>, the first module to register would win and the rest
    ///         would be dropped: in a three-module application the static map would belong to one
    ///         module and the other two modules' entities would have no navigation filters at all.
    ///     </para>
    ///     <para>
    ///         A test that hands the composer a single factory cannot see it, so this one hands it two.
    ///     </para>
    /// </remarks>
    [Fact]
    public void Compose_MergesEveryModulesStaticMap()
    {
        FilterMap Billing(FilterContext _) => new(new Dictionary<Type, LambdaExpression>
        {
            [typeof(Invoice)] = (Expression<Func<Invoice, bool>>)(e => !e.IsDeleted)
        });

        FilterMap Other(FilterContext _) => new(new Dictionary<Type, LambdaExpression>
        {
            [typeof(OtherModuleEntity)] = (Expression<Func<OtherModuleEntity, bool>>)(e => e.IsVisible)
        });

        var composer = new FilterMapComposer(
            new QueryFilterToggle(),
            Array.Empty<IVisibilityFilterProvider>(),
            new Func<FilterContext, FilterMap>[] { Billing, Other });

        var result = composer.Compose(FilterContext.At(DateTimeOffset.UnixEpoch));

        result.TryGetFilter(typeof(Invoice), out _).Should().BeTrue(
            "the first module's entities are filtered, as they always were");
        result.TryGetFilter(typeof(OtherModuleEntity), out _).Should().BeTrue(
            "and so are the second's — which is the whole of the defect this pins");
    }

    private sealed class OtherModuleEntity
    {
        public bool IsVisible { get; init; }
    }
}
