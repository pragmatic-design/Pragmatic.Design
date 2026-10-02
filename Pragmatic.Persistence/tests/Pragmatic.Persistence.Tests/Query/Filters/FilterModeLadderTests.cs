using System.Linq.Expressions;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Tests.Fakes;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.Tests.Query.Filters;

/// <summary>
///     Which filters each <see cref="FilterMode" /> actually lifts.
/// </summary>
/// <remarks>
///     <para>
///         Ownership and scope filters are marked <c>IPermissionBasedFilter</c>, and
///         <c>DefaultQueryFilterProvider</c> drops every one of those at <c>Mode &gt;= Admin</c>, so
///         <c>Admin</c> already sees across owners: a step above it that claimed to lift ownership and
///         data scopes on top would add nothing.
///     </para>
///     <para>
///         This is the guard on what <c>Admin</c> means: a mode that stops lifting ownership would make
///         <c>OwnershipIsLifted</c> fail, which is the change worth being told about.
///     </para>
/// </remarks>
public class FilterModeLadderTests
{
    private sealed class Order
    {
        public string OwnerId { get; init; } = string.Empty;
        public bool IsDeleted { get; init; }
    }

    /// <summary>Stands in for the generated OwnershipFilter, with the marker it really carries.</summary>
    private sealed class OwnershipFilter : IPermissionBasedFilter<Order>
    {
        public int Priority => 200;

        public string BypassPermission => "orders.view-all";

        public Expression<Func<Order, bool>> GetFilter() => order => order.OwnerId == "me";
    }

    private sealed class SoftDelete : IQueryFilter<Order>
    {
        public int Priority => 100;

        public Expression<Func<Order, bool>> GetFilter() => order => !order.IsDeleted;
    }

    /// <remarks>
    ///     ⚠️ The user is authenticated and holds no permission. Without one, a permission-based filter
    ///     makes the provider fail <b>closed</b> — the predicate becomes <c>_ =&gt; false</c> — and
    ///     every mode looks identical for the wrong reason. That is what the first version of this
    ///     measured before its control caught it.
    /// </remarks>
    private static DefaultQueryFilterProvider Provider()
        => new(
            [new SoftDelete(), new OwnershipFilter()],
            new PassthroughQueryFilterTypeRegistry(),
            toggle: null,
            currentUser: new FakeCurrentUser(isAuthenticated: true));

    /// <summary>
    ///     The combined predicate for a mode, as text.
    /// </summary>
    /// <remarks>
    ///     Read through <c>GetCombinedFilter</c>, the public entry point the executor and the generated
    ///     repositories use, rather than through a seam opened for the test: what a mode does is what
    ///     that method returns.
    /// </remarks>
    private static string PredicateIn(FilterMode mode)
        => Provider().GetCombinedFilter<Order>(FilterContext.At(DateTimeOffset.UnixEpoch) with { Mode = mode })?.ToString() ?? string.Empty;

    /// <summary>⚠️ Ownership is lifted at Admin, one step earlier than the enum described.</summary>
    [Fact]
    public void OwnershipIsLifted_AtAdmin()
    {
        PredicateIn(FilterMode.Normal).Should().Contain("OwnerId",
            "the control: in Normal the ownership filter applies");

        PredicateIn(FilterMode.Admin).Should().NotContain("OwnerId",
            "ownership carries IPermissionBasedFilter, and Admin drops every filter that does");
    }

    /// <summary>Soft-delete survives Admin, which is the half the enum described correctly.</summary>
    [Fact]
    public void SoftDeleteSurvives_Admin()
    {
        PredicateIn(FilterMode.Admin).Should().Contain("IsDeleted");
    }

    /// <summary>
    ///     Every mode above Admin keeps lifting it — there is no step between Admin and Background.
    /// </summary>
    [Fact]
    public void NoModeAboveAdmin_LiftsAnythingMoreOfThisEntity()
    {
        PredicateIn(FilterMode.Background).Should().NotContain("OwnerId");
        PredicateIn(FilterMode.Background).Should().Contain("IsDeleted",
            "Background lifts tenant, and this entity has none — soft-delete stays");
    }
}
