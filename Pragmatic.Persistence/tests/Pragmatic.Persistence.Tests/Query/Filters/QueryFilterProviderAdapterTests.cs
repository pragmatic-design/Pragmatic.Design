using System.Linq.Expressions;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Tests.Fakes;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Query.Filters;

/// <summary>
///     What reaches the navigation <see cref="FilterMap" />.
/// </summary>
/// <remarks>
///     <para>
///         The adapter exists because the generator fills the map the navigation visitor reads at
///         compile time, so without it a filter registered in DI would guard a root query and not an
///         <c>Include</c> pointing at the same rows.
///     </para>
///     <para>
///         It decides nothing itself. It asks <c>IQueryFilterProvider.GetCombinedFilter(Type, …)</c>
///         for the predicate the root would get, which is why the two cannot disagree — and why the
///         ownership and scope filters are in the map, composed the way the root composes them.
///     </para>
/// </remarks>
public class QueryFilterProviderAdapterTests
{
    private sealed class Item
    {
        public bool IsConfirmed { get; init; }
        public bool IsDeleted { get; init; }
        public string TenantId { get; init; } = "";
        public string Region { get; init; } = "";
    }

    private sealed class ConfirmedOnly : VisibilityRule<Item>
    {
        public override Expression<Func<Item, bool>> ToExpression() => item => item.IsConfirmed;
    }

    private sealed class NotDeleted : IQueryFilter<Item>
    {
        public int Priority => 100;

        public Expression<Func<Item, bool>> GetFilter() => item => !item.IsDeleted;
    }

    /// <summary>Restrictive and permission-based, the shape of the generated OwnershipFilter.</summary>
    private sealed class OwnedByCaller : IQueryFilter<Item>, IPermissionBasedFilter
    {
        public string BypassPermission => "item.read-all";

        public Expression<Func<Item, bool>> GetFilter() => item => item.TenantId == "mine";
    }

    /// <summary>Additive, the shape of the generated ScopedDataFilter.</summary>
    private sealed class ScopeNorth : IQueryFilter<Item>, IPermissionBasedFilter, IScopeVisibilityFilter
    {
        public string BypassPermission => "item.read-all";

        public Expression<Func<Item, bool>> GetFilter() => item => item.Region == "north";
    }

    private sealed class ScopeSouth : IQueryFilter<Item>, IPermissionBasedFilter, IScopeVisibilityFilter
    {
        public string BypassPermission => "item.read-all";

        public Expression<Func<Item, bool>> GetFilter() => item => item.Region == "south";
    }

    private sealed class ForTenant : IQueryFilter<Item>, ITenantFilter
    {
        public Expression<Func<Item, bool>> GetFilter() => item => item.TenantId == "acme";
    }

    /// <summary>A filter that asks for the root and nothing else.</summary>
    private sealed class RootOnly : IQueryFilter<Item>
    {
        public FilterScope Scope => FilterScope.Root;

        public Expression<Func<Item, bool>> GetFilter() => item => item.IsConfirmed;
    }

    /// <summary>A filter that says nothing about itself — the non-generic interface, by hand.</summary>
    private sealed class Untyped : IQueryFilter
    {
        public int Priority => 0;
    }

    private static IReadOnlyDictionary<Type, LambdaExpression> Filters(
        FilterMode mode, params IQueryFilter[] filters)
    {
        var provider = new DefaultQueryFilterProvider(
            filters,
            new PassthroughQueryFilterTypeRegistry(),
            toggle: null,
            currentUser: new FakeCurrentUser(isAuthenticated: true));

        return new QueryFilterProviderAdapter(filters, provider)
            .GetFilters(FilterContext.At(DateTimeOffset.UnixEpoch) with { Mode = mode });
    }

    private static Func<Item, bool> Compiled(IReadOnlyDictionary<Type, LambdaExpression> map)
        => ((Expression<Func<Item, bool>>)map[typeof(Item)]).Compile();

    [Fact]
    public void ARegisteredFilter_ReachesTheMap()
    {
        Filters(FilterMode.Normal, new ConfirmedOnly())
            .Should().ContainKey(typeof(Item));
    }

    /// <summary>
    ///     ⚠️ Ownership reaches the map.
    /// </summary>
    /// <remarks>
    ///     An adapter that could only AND would have to leave every <c>IPermissionBasedFilter</c> out,
    ///     because ANDing an additive scope group is strictly narrower than the root — rows visible on
    ///     a root query vanishing from an <c>Include</c>. Ownership, which is restrictive and ANDs
    ///     perfectly well, would be left out with them and guard no navigation at all.
    /// </remarks>
    [Fact]
    public void OwnershipReachesTheMap()
    {
        var compiled = Compiled(Filters(FilterMode.Normal, new OwnedByCaller()));

        compiled(new Item { TenantId = "mine" }).Should().BeTrue();
        compiled(new Item { TenantId = "theirs" }).Should().BeFalse();
    }

    /// <summary>
    ///     ⚠️ Two scopes compose as an OR, which is the thing the old adapter could not express.
    /// </summary>
    /// <remarks>
    ///     A row is visible if it matches <em>any</em> scope. Getting this wrong in the narrowing
    ///     direction is invisible in a test that only checks the filter is present, which is why this
    ///     compiles the predicate and feeds it a row from each scope.
    /// </remarks>
    [Fact]
    public void TwoScopes_ComposeAsOr()
    {
        var compiled = Compiled(Filters(FilterMode.Normal, new ScopeNorth(), new ScopeSouth()));

        compiled(new Item { Region = "north" }).Should().BeTrue();
        compiled(new Item { Region = "south" }).Should().BeTrue();
        compiled(new Item { Region = "east" }).Should().BeFalse();
    }

    /// <summary>Restrictive AND (scope OR scope) — the root's shape, reached through the map.</summary>
    [Fact]
    public void RestrictiveAndScopes_ComposeTheWayTheRootDoes()
    {
        var compiled = Compiled(Filters(
            FilterMode.Normal, new NotDeleted(), new ScopeNorth(), new ScopeSouth()));

        compiled(new Item { Region = "north", IsDeleted = false }).Should().BeTrue();
        compiled(new Item { Region = "south", IsDeleted = false }).Should().BeTrue();
        compiled(new Item { Region = "north", IsDeleted = true }).Should().BeFalse(
            "soft-delete is restrictive: it ANDs with the scope group rather than joining the OR");
    }

    /// <summary>
    ///     ⚠️ A filter that asks for the root only stays out of the map.
    /// </summary>
    /// <remarks>
    ///     The map is per entity type and has no navigation to compare against, so without consulting
    ///     <c>FilterScope</c> a filter would reach every navigation regardless of what it declared. The
    ///     map is only ever applied to collection navigations — <c>PragmaticQueryFilterVisitor</c>
    ///     leaves references alone — so that is the context it is built with.
    /// </remarks>
    [Fact]
    public void AFilterScopedToTheRoot_StaysOutOfTheMap()
    {
        Filters(FilterMode.Normal, new RootOnly()).Should().BeEmpty();
    }

    /// <summary>The control: the default scope includes collections, so an ordinary filter is in.</summary>
    [Fact]
    public void AFilterWithTheDefaultScope_IsInTheMap()
    {
        Filters(FilterMode.Normal, new ConfirmedOnly()).Should().ContainKey(typeof(Item));
    }

    /// <summary>
    ///     A mode does not lift a plain rule.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not the last word: <c>FilterMapComposer</c> drops every dynamic provider once the mode
    ///     reaches <c>Admin</c>, so in that mode these contributions never reach the map however
    ///     carefully they were chosen. The gate stays — it protects against a provider that ignores
    ///     the context. This asserts what the adapter answers, not what survives the composer.
    /// </remarks>
    [Theory]
    [InlineData(FilterMode.Admin)]
    [InlineData(FilterMode.Background)]
    public void AMode_DoesNotLiftAPlainRule(FilterMode mode)
    {
        Filters(mode, new ConfirmedOnly()).Should().ContainKey(typeof(Item));
    }

    [Fact]
    public void ATenantFilter_IsSkippedInTheSameModeTheRootSkipsIt()
    {
        Filters(FilterMode.Normal, new ForTenant()).Should().ContainKey(typeof(Item));
        Filters(FilterMode.Background, new ForTenant()).Should().BeEmpty();
    }

    /// <summary>Two restrictive filters on one entity compose as an AND.</summary>
    [Fact]
    public void TwoFiltersOnOneEntity_AreAnded()
    {
        var compiled = Compiled(Filters(FilterMode.Normal, new ConfirmedOnly(), new NotDeleted()));

        compiled(new Item { IsConfirmed = true, IsDeleted = false }).Should().BeTrue();
        compiled(new Item { IsConfirmed = true, IsDeleted = true }).Should().BeFalse();
        compiled(new Item { IsConfirmed = false, IsDeleted = false }).Should().BeFalse();
    }

    /// <summary>A filter that names neither its entity nor its predicate is skipped, not guessed at.</summary>
    [Fact]
    public void AFilterThatSaysNothingAboutItself_IsSkipped()
    {
        Filters(FilterMode.Normal, new Untyped()).Should().BeEmpty();
    }
}
