using System.Linq.Expressions;
using Pragmatic.Authorization;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Query;

/// <summary>
///     A step up the <c>FilterMode</c> ladder must never show fewer rows than the step below.
/// </summary>
/// <remarks>
///     <para>
///         Scope-visibility filters are <b>additive</b>: a row is visible if it matches <em>any</em>
///         of them, so they are OR-composed and then ANDed with the restrictive ones. Removing one
///         disjunct from an OR is therefore a <b>narrowing</b>, not a lift.
///     </para>
///     <para>
///         ⚠️ That is what the ladder did. <c>Admin</c> drops filters that are
///         <c>IPermissionBasedFilter</c>; a scope-visibility filter that is <em>not</em> one survived,
///         so the OR group collapsed to that single predicate and an administrative read came back
///         with strictly fewer rows than an ordinary one — measured empty on a consumer. The same with
///         a typed lift of one group member: the others became the whole condition, and a legitimate
///         row was reported as a conflict.
///     </para>
///     <para>
///         The ladder was tested one filter at a time, and a group of two is where the composition
///         shows. These tests evaluate the compiled predicate against rows rather than reading the
///         expression, because "monotonic" is a statement about the rows.
///     </para>
/// </remarks>
public class TheFilterModeLadderIsMonotonicTests
{
    private sealed class Member
    {
        public string OwnerId { get; init; } = "";
        public string Scope { get; init; } = "";
        public bool IsDeleted { get; init; }
    }

    /// <summary>"Mine" — additive, and gated on a permission, so <c>Admin</c> drops it.</summary>
    private sealed class OwnedMembersFilter : IQueryFilter<Member>, IScopeVisibilityFilter, IPermissionBasedFilter
    {
        public int Priority => 100;
        public FilterScope Scope => FilterScope.All;
        public string BypassPermission => "members.viewall";
        public Expression<Func<Member, bool>> GetFilter() => m => m.OwnerId == "me";
    }

    /// <summary>"In a scope I hold" — additive, and <b>not</b> permission-based, so it survived.</summary>
    private sealed class ScopedMembersFilter : IQueryFilter<Member>, IScopeVisibilityFilter
    {
        public int Priority => 200;
        public FilterScope Scope => FilterScope.All;
        public Expression<Func<Member, bool>> GetFilter() => m => m.Scope == "team-a";
    }

    /// <summary>A restrictive filter, to keep the AND half of the composition in the picture.</summary>
    private sealed class NotDeletedFilter : IQueryFilter<Member>
    {
        public int Priority => 50;
        public FilterScope Scope => FilterScope.All;
        public Expression<Func<Member, bool>> GetFilter() => m => !m.IsDeleted;
    }

    private static readonly Member[] Rows =
    [
        new() { OwnerId = "me", Scope = "team-b" },      // mine, not in my scope
        new() { OwnerId = "someone", Scope = "team-a" }, // not mine, in my scope
        new() { OwnerId = "someone", Scope = "team-z" }, // neither
        new() { OwnerId = "me", Scope = "team-a", IsDeleted = true }, // both, but deleted
    ];

    private static DefaultQueryFilterProvider Provider(IQueryFilterToggle? toggle = null)
        => new(
            [new OwnedMembersFilter(), new ScopedMembersFilter(), new NotDeletedFilter()],
            new PassthroughRegistry(),
            toggle,
            // Authenticated, and holding neither bypass permission. Without a user the pipeline
            // fails closed — correctly — and every assertion here would be about an empty set.
            new TestUser());

    private sealed class PlainAuthorization : IUserAuthorization
    {
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlySet<string> Permissions => new HashSet<string>();
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];

        public bool HasPermission(string permission) => false;
        public bool HasAnyPermission(IEnumerable<string> permissions) => false;
        public bool HasAllPermissions(IEnumerable<string> permissions) => false;
        public bool IsInRole(string role) => false;
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }

    private sealed class TestUser : Pragmatic.Identity.ICurrentUser
    {
        public string Id => "me";
        public string? DisplayName => "Me";
        public bool IsAuthenticated => true;
        public Pragmatic.Identity.PrincipalKind Kind => Pragmatic.Identity.PrincipalKind.User;
        public string? TenantId => null;

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
            => new Dictionary<string, IReadOnlyList<string>>();

        public IUserAuthorization Authorization { get; } = new PlainAuthorization();

        public Pragmatic.Identity.IAuthenticationContext Authentication
            => Pragmatic.Identity.NullAuthenticationContext.Instance;
    }

    /// <summary>The rows a mode lets through.</summary>
    private static IReadOnlyList<Member> Visible(FilterMode mode, IQueryFilterToggle? toggle = null)
    {
        var predicate = Provider(toggle).GetCombinedFilter(
            typeof(Member),
            FilterContext.At(DateTimeOffset.UnixEpoch) with { Mode = mode, UserId = "me" });

        if (predicate is null)
            return Rows;

        var compiled = (Func<Member, bool>)predicate.Compile();

        return Rows.Where(compiled).ToList();
    }

    /// <summary>Admin shows at least what Normal shows.</summary>
    [Fact]
    public void Admin_IsASupersetOfNormal()
    {
        var normal = Visible(FilterMode.Normal);
        var admin = Visible(FilterMode.Admin);

        admin.Should().Contain(normal,
            "a step up the ladder lifts restrictions; it cannot add one");
    }

    /// <summary>
    ///     The control: <c>Normal</c> is not empty and not everything, so the comparison means something.
    /// </summary>
    /// <remarks>
    ///     Without it, "Admin is a superset of Normal" is satisfied when Normal is empty — which is
    ///     also the shape of a fail-closed pipeline that shows nothing to anyone.
    /// </remarks>
    [Fact]
    public void Normal_ShowsTheAdditiveGroup_AndNothingElse()
    {
        var normal = Visible(FilterMode.Normal);

        normal.Should().HaveCount(2, "mine, or in my scope, and not deleted");
        normal.Should().NotContain(Rows[2], "neither mine nor in my scope");
        normal.Should().NotContain(Rows[3], "deleted, whatever else it is");
    }

    /// <summary>And the restrictive half still applies at the higher step.</summary>
    /// <remarks>
    ///     The second control. Lifting the additive group must not lift the soft-delete AND beside it,
    ///     or "monotonic" would be bought by turning the pipeline off.
    /// </remarks>
    [Fact]
    public void Admin_StillHidesADeletedRow()
    {
        Visible(FilterMode.Admin).Should().NotContain(Rows[3],
            "Admin lifts the scope group, not the row-state filters");
    }

    /// <summary>Lifting one member of an additive group lifts the group.</summary>
    /// <remarks>
    ///     A disjunct is an alternative way in, not a requirement, so "lift this one" has no partial
    ///     reading: dropping it narrows. ⚠️ That is what happened — the remaining members became the
    ///     whole condition and a legitimate row was reported as a conflict.
    /// </remarks>
    [Fact]
    public void LiftingOneMemberOfTheGroup_LiftsTheGroup()
    {
        var toggle = new QueryFilterToggle();
        using var lifted = toggle.Disable<ScopedMembersFilter>();

        var visible = Visible(FilterMode.Normal, toggle);

        visible.Should().Contain(Rows[2],
            "the row matched neither member: with the group lifted there is no scope restriction left");
        visible.Should().NotContain(Rows[3], "and the soft-delete filter is untouched");
    }

    private sealed class PassthroughRegistry : IQueryFilterTypeRegistry
    {
        public bool ImplementsFilterFor(Type filterType, Type entityType) => true;
    }
}
