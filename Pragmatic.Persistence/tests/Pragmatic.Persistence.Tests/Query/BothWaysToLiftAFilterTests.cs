using System.Linq.Expressions;
using Pragmatic.Authorization;
using Pragmatic.Persistence.Query.Builder;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Query;

/// <summary>
///     A filter can be lifted through the toggle or through the <c>FilterContext</c>, and the two
///     forms must answer the same question the same way.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>GetFiltersForContext</c> honours both, and the fail-closed guard has to as well. A
///         guard that read only the toggle would let a <c>QueryBuilder</c> running with no caller — a
///         background sweep, a CLI tool — and lifting with <c>IgnoreQueryFilter&lt;T&gt;()</c>, which
///         travels in <c>FilterContext.DisabledFilters</c>, meet a guard that finds a permission-based
///         filter active and returns the predicate that lets nothing through. The read would come back
///         empty, while <c>filters.Disable&lt;T&gt;()</c> — the other spelling of the same request —
///         works.
///     </para>
///     <para>
///         It is easy to miss because an empty read <em>is</em> the fail-closed outcome: the guard
///         firing when it should not looks exactly like the guard doing its job.
///     </para>
///     <para>
///         ⚠️ Beside it, and the same shape: a <c>QueryBuilder.Build</c> that composed its own
///         <c>FilterContext</c> from nothing would never pass a mode set with <c>UseMode</c> to a
///         hand-composed read. <c>Disable&lt;T&gt;()</c> would still reach it, only because the
///         provider consults the toggle directly.
///     </para>
/// </remarks>
public class BothWaysToLiftAFilterTests
{
    private sealed class Member
    {
        public string OwnerId { get; init; } = "";
        public string TenantId { get; init; } = "";
    }

    /// <summary>The filter that arms the guard: permission-based, so a missing user matters.</summary>
    private sealed class OwnedMembersFilter : IQueryFilter<Member>, IPermissionBasedFilter
    {
        public int Priority => 100;
        public FilterScope Scope => FilterScope.All;
        public string BypassPermission => "members.viewall";
        public Expression<Func<Member, bool>> GetFilter() => m => m.OwnerId == "me";
    }

    /// <summary>A tenant filter, to measure a mode that is not about permissions.</summary>
    private sealed class TenantMembersFilter : IQueryFilter<Member>, ITenantFilter
    {
        public int Priority => 200;
        public FilterScope Scope => FilterScope.All;
        public Expression<Func<Member, bool>> GetFilter() => m => m.TenantId == "acme";
    }

    private static readonly Member[] Rows =
    [
        new() { OwnerId = "me", TenantId = "acme" },
        new() { OwnerId = "someone", TenantId = "globex" }
    ];

    private static DefaultQueryFilterProvider Provider(
        IQueryFilter[] filters, IQueryFilterToggle? toggle = null, Pragmatic.Identity.ICurrentUser? user = null)
        => new(filters, new PassthroughRegistry(), toggle, user);

    /// <summary>The rows a builder read lets through.</summary>
    private static IReadOnlyList<Member> Read(QueryBuilder<Member> builder, DefaultQueryFilterProvider provider)
        => builder.Build(Rows.AsQueryable(), provider).ToList();

    /// <summary>The context form of the lift reaches the fail-closed guard.</summary>
    /// <remarks>
    ///     No caller, so the guard is armed; the read asks past the one filter that arms it, and must
    ///     see rows. This is the shape of a background sweep.
    /// </remarks>
    [Fact]
    public void LiftingThroughTheBuilder_ReadsRows()
    {
        var provider = Provider([new OwnedMembersFilter()]);

        var visible = Read(new QueryBuilder<Member>().IgnoreQueryFilter<OwnedMembersFilter>(), provider);

        visible.Should().HaveCount(2,
            "the only filter that arms the guard was lifted, so nothing is left to fail closed on");
    }

    /// <summary>The control: without the lift the same read fails closed.</summary>
    /// <remarks>
    ///     Without it, "the read sees rows" would be satisfied by a guard that never fires — which is
    ///     the leak the guard exists to prevent, and the worse of the two mistakes.
    /// </remarks>
    [Fact]
    public void WithoutTheLift_TheSameReadFailsClosed()
    {
        var provider = Provider([new OwnedMembersFilter()]);

        Read(new QueryBuilder<Member>(), provider).Should().BeEmpty(
            "a permission-based filter with no user to evaluate it must return no rows");
    }

    /// <summary>The two forms of the lift agree.</summary>
    [Fact]
    public void TheToggleFormAndTheContextForm_ReadTheSameRows()
    {
        var toggle = new QueryFilterToggle();
        using var lifted = toggle.Disable<OwnedMembersFilter>();

        var throughTheToggle = Read(new QueryBuilder<Member>(), Provider([new OwnedMembersFilter()], toggle));
        var throughTheContext = Read(
            new QueryBuilder<Member>().IgnoreQueryFilter<OwnedMembersFilter>(),
            Provider([new OwnedMembersFilter()]));

        throughTheContext.Should().Contain(throughTheToggle,
            "one request, two spellings — the answer cannot depend on which was used");
        throughTheToggle.Should().Contain(throughTheContext);
    }

    /// <summary>A mode set on the toggle reaches a hand-composed read.</summary>
    /// <remarks>
    ///     ⚠️ A <c>Build</c> that composed a blank <c>FilterContext</c> would run in mode
    ///     <c>Normal</c> whatever the scope asked for. Measured on the tenant filter rather than the
    ///     permission one, because an anonymous caller lifts permission filters anyway and the
    ///     difference would not show.
    /// </remarks>
    [Fact]
    public void AModeSetOnTheToggle_ReachesTheBuilder()
    {
        var toggle = new QueryFilterToggle();
        var user = new AuthenticatedCaller();

        Read(new QueryBuilder<Member>(), Provider([new TenantMembersFilter()], toggle, user))
            .Should().HaveCount(1, "at Normal the tenant filter applies");

        using var background = toggle.UseMode(FilterMode.Background);

        Read(new QueryBuilder<Member>(), Provider([new TenantMembersFilter()], toggle, user))
            .Should().HaveCount(2, "Background lifts tenant filters, and the builder is a read like any other");
    }

    private sealed class PassthroughRegistry : IQueryFilterTypeRegistry
    {
        public bool ImplementsFilterFor(Type filterType, Type entityType) => true;
    }

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

    /// <summary>An authenticated caller, holding nothing.</summary>
    private sealed class AuthenticatedCaller : Pragmatic.Identity.ICurrentUser
    {
        public string Id => "me";
        public string? DisplayName => "Me";
        public bool IsAuthenticated => true;
        public Pragmatic.Identity.PrincipalKind Kind => Pragmatic.Identity.PrincipalKind.User;
        public string? TenantId => "acme";

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
            => new Dictionary<string, IReadOnlyList<string>>();

        public IUserAuthorization Authorization { get; } = new PlainAuthorization();

        public Pragmatic.Identity.IAuthenticationContext Authentication
            => Pragmatic.Identity.NullAuthenticationContext.Instance;
    }
}
