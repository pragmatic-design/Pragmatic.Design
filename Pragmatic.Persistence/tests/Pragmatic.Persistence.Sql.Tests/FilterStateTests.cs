using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     What each mode, lift and caller does to each family of filters (<see cref="PragmaticSqlFilters" />), without a database: the table
///     Pragmatic's FilterModeLadderTests guards for EF (Admin lifts ownership and scopes, keeps soft delete and tenant; Background lifts the
///     tenant too; Raw lifts everything), plus the anonymous caller, the bypass permission and the lifts by type and by name.
/// </summary>
public class FilterStateTests
{
    private static readonly SqlFilterSlot SoftDelete = new(typeof(Reservation), SqlFilterFamily.SoftDelete, typeof(Reservation.SoftDeleteFilter), "SoftDelete");
    private static readonly SqlFilterSlot Tenant = new(typeof(Property), SqlFilterFamily.Tenant, typeof(Property.TenantFilter), "Tenant");
    private static readonly SqlFilterSlot Owner = new(typeof(Reservation), SqlFilterFamily.Permission, typeof(Reservation.OwnershipFilter), null, "reservation.view-all");
    private static readonly SqlFilterSlot Scope = new(typeof(Property), SqlFilterFamily.Permission, typeof(Property.ScopedDataFilter), null, "property.view-all");
    private static readonly SqlFilterSlot Rule = new(typeof(Reservation), SqlFilterFamily.Rule, null, "Visibility:PragmaticEntities.Tests.SomeRule");

    private const SqlFilterState A = SqlFilterState.Active;
    private const SqlFilterState L = SqlFilterState.Lifted;

    [Theory]
    [InlineData(FilterMode.Normal, A, A, A, A, A)]
    [InlineData(FilterMode.Admin, A, A, L, L, A)] // ownership and scopes are IPermissionBasedFilter; a declared rule is not lifted by a mode
    [InlineData(FilterMode.Background, A, L, L, L, A)]
    [InlineData(FilterMode.Raw, L, L, L, L, L)]
    public void Each_mode_lifts_what_it_lifts_on_EF(FilterMode mode, SqlFilterState softDelete, SqlFilterState tenant, SqlFilterState owner, SqlFilterState scope, SqlFilterState rule)
    {
        var toggle = new QueryFilterToggle();
        using var _ = toggle.UseMode(mode);
        var filters = new PragmaticSqlFilters(new TestUser("u1"), toggle: toggle);
        Assert.Equal([softDelete, tenant, owner, scope, rule], [filters.StateOf(SoftDelete), filters.StateOf(Tenant), filters.StateOf(Owner), filters.StateOf(Scope), filters.StateOf(Rule)]);
    }

    [Fact]
    public void An_anonymous_caller_sees_nothing_of_an_owned_entity_unless_the_options_say_otherwise()
    {
        Assert.Equal(SqlFilterState.Closed, new PragmaticSqlFilters(AnonymousUser.Instance).StateOf(Owner));
        Assert.Equal(SqlFilterState.Closed, new PragmaticSqlFilters().StateOf(Scope)); // no user at all
        Assert.Equal(L, new PragmaticSqlFilters(AnonymousUser.Instance, options: new QueryFilterOptions { FailClosedWhenAnonymous = false }).StateOf(Owner));
        Assert.Equal(A, new PragmaticSqlFilters(AnonymousUser.Instance).StateOf(SoftDelete)); // the other families do not care who asks
        // A mode lifts before the anonymous caller closes (a trusted mode legitimately bypasses).
        var toggle = new QueryFilterToggle();
        using var _ = toggle.UseMode(FilterMode.Admin);
        Assert.Equal(L, new PragmaticSqlFilters(AnonymousUser.Instance, toggle: toggle).StateOf(Owner));
    }

    [Fact]
    public void The_bypass_permission_lifts_the_permission_filter_of_its_entity_only()
    {
        var filters = new PragmaticSqlFilters(new TestUser("u1", ["reservation.view-all"]));
        Assert.Equal(L, filters.StateOf(Owner));
        Assert.Equal(A, filters.StateOf(Scope));
        Assert.Equal(L, new PragmaticSqlFilters(new TestUser("u1", resolving: true)).StateOf(Scope)); // mid-resolution: nothing known to filter by
    }

    [Fact]
    public void A_lift_by_type_reaches_the_provider_half_and_a_named_lift_the_EF_half()
    {
        var toggle = new QueryFilterToggle();
        var filters = new PragmaticSqlFilters(new TestUser("u1"), toggle: toggle);
        using (toggle.Disable<Reservation.OwnershipFilter>())
            Assert.Equal(L, filters.StateOf(Owner));
        using (toggle.Disable(typeof(Reservation))) // the entity's type: every provider filter of it
            Assert.Equal([L, A], new[] { filters.StateOf(Owner), filters.StateOf(SoftDelete) }); // soft delete keeps EF's named half
        using (toggle.Disable<Reservation.SoftDeleteFilter>())
        {
            Assert.Equal(A, filters.StateOf(SoftDelete));
            using (toggle.DisableQueryFilter("SoftDelete"))
                Assert.Equal(L, filters.StateOf(SoftDelete)); // both halves: what [WithoutFilter<Reservation>] lifts
        }

        using (toggle.DisableQueryFilter("Visibility:PragmaticEntities.Tests.SomeRule"))
            Assert.Equal(L, filters.StateOf(Rule));
        using (toggle.DisableAll())
            Assert.Equal([L, L, A, A], new[] { filters.StateOf(Owner), filters.StateOf(Scope), filters.StateOf(SoftDelete), filters.StateOf(Rule) });
    }

    [Fact]
    public void The_values_are_the_tenant_the_authenticated_user_and_their_resolved_scopes()
    {
        using var tenant = TenantScope.BeginScope("t1");
        var filters = new PragmaticSqlFilters(new TestUser("u1"), new TenantScope(), new TestScopes("desk-north"));
        Assert.Equal(("t1", "u1"), (filters.TenantId, filters.UserId));
        Assert.Equal(["desk-north"], filters.Scopes);
        var anonymous = new PragmaticSqlFilters(AnonymousUser.Instance, new TenantScope(), new TestScopes("desk-north"));
        Assert.Null(anonymous.UserId);
        Assert.Empty(anonymous.Scopes);
    }
}
