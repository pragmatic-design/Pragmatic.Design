using System.Linq.Expressions;
using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Query;

/// <summary>
///     A permission provider reading its own rows must not have permission filters applied to that
///     read — and it must not have to say so.
/// </summary>
/// <remarks>
///     <para>
///         The cycle is documented and real: a per-tenant permission store reads the application
///         database, the read goes through a repository, a permission-based filter asks the caller's
///         permissions, and resolution starts again. <c>CachedPermissionResolver</c> has a guard that
///         breaks it and hands back an empty set, so nothing crashes — but the read it produces is
///         wrong, and the provider is told to read "past the filter pipeline".
///     </para>
///     <para>
///         ⚠️ Saying that was the trap. <c>DisableAll()</c> works; <c>Disable&lt;TFilter&gt;()</c>
///         lifts <b>one</b> filter, so every other permission filter on the entity still resolves —
///         and a provider cannot be expected to name filters it has never heard of. The correct-looking
///         narrowing is the one that breaks, and the difference was written nowhere.
///     </para>
///     <para>
///         So the pipeline decides it instead: while a permission resolution is in progress, permission
///         filters do not apply. No lift is needed, typed or total, and the narrow form stops being a
///         footgun.
///     </para>
/// </remarks>
public class PermissionFiltersDuringResolutionTests
{
    private sealed class Order
    {
        public string OwnerId { get; set; } = "";
    }

    /// <summary>Two permission filters on the same entity — which is the ordinary case.</summary>
    private sealed class OwnedOrdersFilter : IQueryFilter<Order>, IPermissionBasedFilter
    {
        public int Priority => 100;
        public FilterScope Scope => FilterScope.All;
        public string BypassPermission => "orders.viewall";
        public Expression<Func<Order, bool>> GetFilter() => o => o.OwnerId == "me";
    }

    private sealed class ScopedOrdersFilter : IQueryFilter<Order>, IPermissionBasedFilter
    {
        public int Priority => 200;
        public FilterScope Scope => FilterScope.All;
        public string BypassPermission => "orders.viewscoped";
        public Expression<Func<Order, bool>> GetFilter() => o => o.OwnerId != "";
    }

    /// <summary>Authorization that is in the middle of resolving, and says so.</summary>
    private sealed class ResolvingAuthorization : IUserAuthorization
    {
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlySet<string> Permissions => new HashSet<string>();
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];
        public bool IsResolvingPermissions => true;

        // What the filter provider would call. Empty while resolving, which is exactly why applying a
        // permission filter here produces a wrong read rather than a refusal.
        public bool HasPermission(string permission) => false;
        public bool HasAnyPermission(IEnumerable<string> permissions) => false;
        public bool HasAllPermissions(IEnumerable<string> permissions) => false;
        public bool IsInRole(string role) => false;
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }

    private sealed class SettledAuthorization : IUserAuthorization
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

    private sealed class TestUser(IUserAuthorization authorization) : ICurrentUser
    {
        public string Id => "me";
        public string? DisplayName => "Me";
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;

        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims
            => new Dictionary<string, IReadOnlyList<string>>();

        public IUserAuthorization Authorization { get; } = authorization;
        public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;
    }

    private static DefaultQueryFilterProvider ProviderFor(IUserAuthorization authorization, IQueryFilterToggle? toggle = null)
        => new(
            [new OwnedOrdersFilter(), new ScopedOrdersFilter()],
            new PassthroughRegistry(),
            toggle,
            new TestUser(authorization));

    /// <summary>Every permission filter is off while permissions are being resolved.</summary>
    [Fact]
    public void WhileResolving_NoPermissionFilterApplies()
    {
        ProviderFor(new ResolvingAuthorization())
            .GetFilters<Order>()
            .Should().BeEmpty(
                "a provider reading its own rows would otherwise be filtered by the permissions it "
                + "is resolving, which are not known yet");
    }

    /// <summary>
    ///     And a typed lift does not have to name every filter, because none of them apply.
    /// </summary>
    /// <remarks>
    ///     <c>Disable&lt;OwnedOrdersFilter&gt;()</c> lifts one filter and would leave the other resolving
    ///     if it had to do the work. The typed form works inside a provider by not being the thing that
    ///     has to work.
    /// </remarks>
    [Fact]
    public void WhileResolving_ATypedLiftIsEnough()
    {
        var toggle = new QueryFilterToggle();
        using var lifted = toggle.Disable<OwnedOrdersFilter>();

        ProviderFor(new ResolvingAuthorization(), toggle)
            .GetFilters<Order>()
            .Should().BeEmpty("naming one filter used to leave the other one resolving");
    }

    /// <summary>
    ///     The control: outside a resolution, permission filters apply as they always did.
    /// </summary>
    /// <remarks>
    ///     Without it, "no filter applies while resolving" is satisfied by a provider that applies no
    ///     permission filter ever — which is every row of every table to everyone.
    /// </remarks>
    [Fact]
    public void OutsideAResolution_PermissionFiltersStillApply()
    {
        ProviderFor(new SettledAuthorization())
            .GetFilters<Order>()
            .Should().HaveCount(2, "the user has neither bypass permission");
    }

    /// <summary>
    ///     The second control: a typed lift still lifts exactly the filter it names.
    /// </summary>
    /// <remarks>
    ///     It keeps the change inside the resolution. Making the typed lift total would be a different
    ///     defect wearing this one's clothes.
    /// </remarks>
    [Fact]
    public void OutsideAResolution_ATypedLiftLiftsOnlyWhatItNames()
    {
        var toggle = new QueryFilterToggle();
        using var lifted = toggle.Disable<OwnedOrdersFilter>();

        ProviderFor(new SettledAuthorization(), toggle)
            .GetFilters<Order>()
            .Should().HaveCount(1, "one named, one left");
    }

    /// <summary>Every filter is for every entity — the registry is not what these tests measure.</summary>
    private sealed class PassthroughRegistry : IQueryFilterTypeRegistry
    {
        public bool ImplementsFilterFor(Type filterType, Type entityType) => true;
    }
}
