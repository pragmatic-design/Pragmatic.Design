using System.Linq.Expressions;
using Pragmatic.Persistence.EFCore.Query.Visitors;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Unit;

/// <summary>
///     The whole chain for an ownership or scope filter: provider → adapter → map → visitor.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Why this file exists.</b> Ownership and data-scope filters reaching a collection
///         navigation is a change on a security boundary, and <b>no application exercises it</b> — the
///         Showcase has one scoped entity (<c>Invoice</c>) and reads it through no collection, and the
///         consumer laboratory has no scoped entity at all. So 548 and 124 green suites say the change
///         broke nothing that existed; they say nothing about whether it works. Measuring the
///         composition alone would have been the same gap one level up: a correct predicate that
///         nothing puts in front of a navigation.
///     </para>
///     <para>
///         Hermetic on purpose. The step this covers is expression rewriting, which needs no database:
///         the adapter is asked for a real map, the map goes into the real visitor, and the assertion
///         is on the expression that comes out.
///     </para>
/// </remarks>
public class ScopeFiltersReachCollectionsTests
{
    private sealed class Region
    {
        public IEnumerable<Site> Sites { get; init; } = [];
    }

    private sealed class Site
    {
        public string Owner { get; init; } = "";
        public string Area { get; init; } = "";
        public bool IsDeleted { get; init; }
    }

    private sealed class NotDeleted : IQueryFilter<Site>
    {
        public int Priority => 100;

        public Expression<Func<Site, bool>> GetFilter() => site => !site.IsDeleted;
    }

    /// <summary>Restrictive and permission-based — the generated OwnershipFilter's shape.</summary>
    private sealed class OwnedByCaller : IQueryFilter<Site>, IPermissionBasedFilter
    {
        public int Priority => 200;

        public string BypassPermission => "site.read-all";

        public Expression<Func<Site, bool>> GetFilter() => site => site.Owner == "me";
    }

    /// <summary>Additive — the generated ScopedDataFilter's shape.</summary>
    private sealed class ScopeNorth : IQueryFilter<Site>, IPermissionBasedFilter, IScopeVisibilityFilter
    {
        public int Priority => 250;

        public string BypassPermission => "site.read-all";

        public Expression<Func<Site, bool>> GetFilter() => site => site.Area == "north";
    }

    private sealed class ScopeSouth : IQueryFilter<Site>, IPermissionBasedFilter, IScopeVisibilityFilter
    {
        public int Priority => 250;

        public string BypassPermission => "site.read-all";

        public Expression<Func<Site, bool>> GetFilter() => site => site.Area == "south";
    }

    private sealed class Caller : Pragmatic.Identity.ICurrentUser
    {
        public string Id => "me";
        public string? DisplayName => "Me";
        public bool IsAuthenticated => true;
        public Pragmatic.Identity.PrincipalKind Kind => Pragmatic.Identity.PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims =>
            new Dictionary<string, IReadOnlyList<string>>();
        public Pragmatic.Authorization.IUserAuthorization Authorization => new NoPermissions();
        public Pragmatic.Identity.IAuthenticationContext Authentication =>
            Pragmatic.Identity.NullAuthenticationContext.Instance;

        private sealed class NoPermissions : Pragmatic.Authorization.IUserAuthorization
        {
            public IReadOnlyCollection<string> Roles => [];
            public IReadOnlySet<string> Permissions => new HashSet<string>();
            public IReadOnlyCollection<string> Groups => [];
            public IReadOnlyCollection<string> Scopes => [];
            public bool HasPermission(string permission) => false;
            public bool HasAnyPermission(IEnumerable<string> perms) => false;
            public bool HasAllPermissions(IEnumerable<string> perms) => false;
            public bool IsInRole(string role) => false;
            public bool IsInGroup(string group) => false;
            public bool HasScope(string scope) => false;
        }
    }

    /// <summary>Runs the real chain and returns the rewritten expression as text.</summary>
    private static string RewrittenCollection(params IQueryFilter[] filters)
    {
        var provider = new DefaultQueryFilterProvider(
            filters, new PassthroughQueryFilterTypeRegistry(), toggle: null, currentUser: new Caller());

        var map = new QueryFilterProviderAdapter(filters, provider)
            .GetFilters(FilterContext.At(DateTimeOffset.UnixEpoch) with { Mode = FilterMode.Normal });

        Expression<Func<Region, IEnumerable<Site>>> sites = region => region.Sites;

        return new PragmaticQueryFilterVisitor(new FilterMap(new Dictionary<Type, LambdaExpression>(map)))
            .Apply(sites)
            .ToString();
    }

    /// <summary>The zero: with no filter registered, the navigation is left alone.</summary>
    [Fact]
    public void WithNoFilters_TheCollectionIsUntouched()
    {
        RewrittenCollection().Should().NotContain("Where");
    }

    /// <summary>⚠️ Ownership reaches the collection. It reached nothing before.</summary>
    [Fact]
    public void Ownership_ReachesTheCollection()
    {
        var rewritten = RewrittenCollection(new OwnedByCaller());

        rewritten.Should().Contain("Where").And.Contain("Owner");
    }

    /// <summary>
    ///     Two scopes arrive at the collection as an OR, not an AND.
    /// </summary>
    /// <remarks>
    ///     The narrowing mistake is the one worth catching: ANDing them would keep the word "Where" in
    ///     the expression and quietly hide every row that matches one scope and not the other. Both
    ///     areas have to be in the rewritten expression, and joined by an OrElse.
    /// </remarks>
    [Fact]
    public void TwoScopes_ReachTheCollectionAsAnOr()
    {
        var rewritten = RewrittenCollection(new ScopeNorth(), new ScopeSouth());

        rewritten.Should().Contain("north").And.Contain("south").And.Contain("OrElse");
    }

    /// <summary>Restrictive AND (scope OR scope) survives the trip to the collection.</summary>
    [Fact]
    public void TheRootsShape_IsWhatReachesTheCollection()
    {
        var rewritten = RewrittenCollection(new NotDeleted(), new ScopeNorth(), new ScopeSouth());

        rewritten.Should().Contain("IsDeleted").And.Contain("OrElse").And.Contain("AndAlso");
    }
}
