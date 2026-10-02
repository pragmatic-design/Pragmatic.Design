using System.Linq.Expressions;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Tests.Fakes;

namespace Pragmatic.Persistence.Tests.Query.Filters;

public class DefaultQueryFilterProviderTests
{
    [Fact]
    public void HasFilters_NoFiltersRegistered_ReturnsFalse()
    {
        var provider = CreateProvider([]);

        provider.HasFilters<TestEntity>().Should().BeFalse();
    }

    [Fact]
    public void HasFilters_WithMatchingFilter_ReturnsTrue()
    {
        var provider = CreateProvider([new SoftDeleteFilter()]);

        provider.HasFilters<TestEntity>().Should().BeTrue();
    }

    [Fact]
    public void HasFilters_WithNonMatchingFilter_ReturnsFalse()
    {
        var provider = CreateProvider([new OtherEntityFilter()]);

        provider.HasFilters<TestEntity>().Should().BeFalse();
    }

    [Fact]
    public void GetFilters_ReturnsMatchingFiltersOrderedByPriority()
    {
        var low = new LowPriorityFilter();    // Priority 300
        var high = new HighPriorityFilter();  // Priority 100
        var provider = CreateProvider([low, high]);

        var filters = provider.GetFilters<TestEntity>().ToList();

        filters.Should().HaveCount(2);
        filters[0].Should().BeOfType<HighPriorityFilter>();
        filters[1].Should().BeOfType<LowPriorityFilter>();
    }

    [Fact]
    public void GetCombinedFilter_SingleFilter_ReturnsThatFilter()
    {
        var provider = CreateProvider([new SoftDeleteFilter()]);

        var combined = provider.GetCombinedFilter<TestEntity>();

        combined.Should().NotBeNull();
        var compiled = combined!.Compile();
        compiled(new TestEntity { IsDeleted = false }).Should().BeTrue();
        compiled(new TestEntity { IsDeleted = true }).Should().BeFalse();
    }

    [Fact]
    public void GetCombinedFilter_MultipleFilters_CombinesWithAnd()
    {
        var provider = CreateProvider([new SoftDeleteFilter(), new ActiveOnlyFilter()]);

        var combined = provider.GetCombinedFilter<TestEntity>();

        combined.Should().NotBeNull();
        var compiled = combined!.Compile();

        // Both conditions must be true
        compiled(new TestEntity { IsDeleted = false, IsActive = true }).Should().BeTrue();
        compiled(new TestEntity { IsDeleted = false, IsActive = false }).Should().BeFalse();
        compiled(new TestEntity { IsDeleted = true, IsActive = true }).Should().BeFalse();
        compiled(new TestEntity { IsDeleted = true, IsActive = false }).Should().BeFalse();
    }

    [Fact]
    public void GetCombinedFilter_NoFilters_ReturnsNull()
    {
        var provider = CreateProvider([]);

        provider.GetCombinedFilter<TestEntity>().Should().BeNull();
    }

    [Fact]
    public void GetCombinedFilter_WithContext_RespectsScope()
    {
        var rootOnlyFilter = new RootOnlyFilter();
        var provider = CreateProvider([rootOnlyFilter]);

        // Root context → applies
        var rootContext = NavigationContext.Root<TestEntity>();
        var rootFilter = provider.GetCombinedFilter<TestEntity>(rootContext);
        rootFilter.Should().NotBeNull();

        // Collection context → scope doesn't match Root-only
        var collectionContext = new NavigationContext
        {
            NavigationType = typeof(ICollection<TestEntity>),
            TargetEntityType = typeof(TestEntity),
            PropertyName = "Items",
            IsCollection = true,
            IsRequired = false,
            RelationType = RelationType.OneToMany,
            Depth = 1,
            ParentEntityType = typeof(TestEntity)
        };
        var collectionFilter = provider.GetCombinedFilter<TestEntity>(collectionContext);
        collectionFilter.Should().BeNull();
    }

    [Fact]
    public void GetFilters_DisabledViaToggle_ExcludesFilter()
    {
        var toggle = new QueryFilterToggle();
        var provider = CreateProvider([new SoftDeleteFilter(), new ActiveOnlyFilter()], toggle);

        using (toggle.Disable<SoftDeleteFilter>())
        {
            var filters = provider.GetFilters<TestEntity>().ToList();
            filters.Should().HaveCount(1);
            filters[0].Should().BeOfType<ActiveOnlyFilter>();
        }

        // After dispose, SoftDelete is back
        provider.GetFilters<TestEntity>().Should().HaveCount(2);
    }

    [Fact]
    public void GetFilters_AllDisabledViaToggle_ReturnsEmpty()
    {
        var toggle = new QueryFilterToggle();
        var provider = CreateProvider([new SoftDeleteFilter(), new ActiveOnlyFilter()], toggle);

        using (toggle.DisableAll())
        {
            provider.GetFilters<TestEntity>().Should().BeEmpty();
            provider.HasFilters<TestEntity>().Should().BeFalse();
            provider.GetCombinedFilter<TestEntity>().Should().BeNull();
        }
    }

    [Fact]
    public void GetFilters_PermissionBasedFilter_SkippedWhenNoCurrentUser()
    {
        var provider = CreateProvider([new TeamFilter()], currentUser: null);

        provider.GetFilters<TestEntity>().Should().BeEmpty();
    }

    [Fact]
    public void GetFilters_PermissionBasedFilter_SkippedWhenUnauthenticated()
    {
        var user = new FakeCurrentUser(isAuthenticated: false);
        var provider = CreateProvider([new TeamFilter()], currentUser: user);

        provider.GetFilters<TestEntity>().Should().BeEmpty();
    }

    [Fact]
    public void GetFilters_PermissionBasedFilter_SkippedWhenUserHasBypassPermission()
    {
        var user = new FakeCurrentUser(isAuthenticated: true, permissions: ["view_all"]);
        var provider = CreateProvider([new TeamFilter()], currentUser: user);

        provider.GetFilters<TestEntity>().Should().BeEmpty();
    }

    [Fact]
    public void GetFilters_PermissionBasedFilter_AppliedWhenUserLacksPermission()
    {
        var user = new FakeCurrentUser(isAuthenticated: true, permissions: ["view_own"]);
        var provider = CreateProvider([new TeamFilter()], currentUser: user);

        provider.GetFilters<TestEntity>().Should().HaveCount(1);
    }

    [Fact]
    public void GetCombinedFilter_MixedFilters_CombinesCorrectly()
    {
        var user = new FakeCurrentUser(isAuthenticated: true, permissions: []);
        var provider = CreateProvider(
            [new SoftDeleteFilter(), new TeamFilter()],
            currentUser: user);

        var combined = provider.GetCombinedFilter<TestEntity>();
        combined.Should().NotBeNull();

        var compiled = combined!.Compile();
        compiled(new TestEntity { IsDeleted = false, TeamId = "A" }).Should().BeTrue();
        compiled(new TestEntity { IsDeleted = true, TeamId = "A" }).Should().BeFalse();
        compiled(new TestEntity { IsDeleted = false, TeamId = "X" }).Should().BeFalse();
    }

    // --- Scope-visibility filters are OR-composed (additive) ---

    [Fact]
    public void GetCombinedFilter_ScopeVisibilityFilters_AreOrComposed()
    {
        // Materialized scope sees only team A; computed scope sees only team B. A row in team B
        // (reachable only via the computed rule) must be visible — AND-composition would hide it.
        var provider = CreateProvider([new MaterializedScopeFilter(), new ComputedScopeStubFilter()]);

        var combined = provider.GetCombinedFilter<TestEntity>();
        combined.Should().NotBeNull();
        var compiled = combined!.Compile();

        compiled(new TestEntity { TeamId = "A" }).Should().BeTrue("matched by the materialized scope");
        compiled(new TestEntity { TeamId = "B" }).Should().BeTrue("matched by the computed scope (OR)");
        compiled(new TestEntity { TeamId = "C" }).Should().BeFalse("matched by neither scope");
    }

    [Fact]
    public void GetCombinedFilter_ScopeVisibilityOrGroup_AndedWithRestrictiveFilters()
    {
        // Restrictive soft-delete AND (materialized OR computed).
        var provider = CreateProvider(
            [new SoftDeleteFilter(), new MaterializedScopeFilter(), new ComputedScopeStubFilter()]);

        var compiled = provider.GetCombinedFilter<TestEntity>()!.Compile();

        compiled(new TestEntity { TeamId = "B", IsDeleted = false }).Should().BeTrue();
        compiled(new TestEntity { TeamId = "B", IsDeleted = true }).Should().BeFalse("soft-delete is AND-ed");
        compiled(new TestEntity { TeamId = "C", IsDeleted = false }).Should().BeFalse("outside both scopes");
    }

    [Fact]
    public void GetCombinedFilter_PassthroughScopeFilter_DoesNotWidenVisibility()
    {
        // Anti-leak: a pass-through (_ => true) scope filter OR-ed naively would expose ALL rows.
        // It must contribute nothing, leaving the materialized scope as the visibility boundary.
        var provider = CreateProvider([new MaterializedScopeFilter(), new PassthroughScopeFilter()]);

        var compiled = provider.GetCombinedFilter<TestEntity>()!.Compile();

        compiled(new TestEntity { TeamId = "A" }).Should().BeTrue();
        compiled(new TestEntity { TeamId = "C" }).Should().BeFalse("pass-through must not collapse to all-rows");
    }

    // --- fail-closed-when-anonymous is the secure default ---

    [Fact]
    public void GetCombinedFilter_AnonymousWithPermissionFilter_FailsClosedByDefault()
    {
        // Secure-by-default: with NO options configured, an anonymous request against a
        // permission-filtered entity must return an explicit no-rows predicate, not leak every row.
        var user = new FakeCurrentUser(isAuthenticated: false);
        var provider = CreateProvider([new TeamFilter()], currentUser: user);

        var combined = provider.GetCombinedFilter<TestEntity>();
        combined.Should().NotBeNull("the default must fail closed for anonymous permission-filtered reads");
        combined!.Compile()(new TestEntity { TeamId = "A" }).Should().BeFalse();
    }

    [Fact]
    public void GetCombinedFilter_AnonymousWithPermissionFilter_FailsOpenWhenExplicitlyOptedOut()
    {
        // Historical fail-open is still available for a genuinely public, permission-filtered entity,
        // but it is now an explicit opt-out rather than the silent default.
        var user = new FakeCurrentUser(isAuthenticated: false);
        var provider = CreateProvider([new TeamFilter()], currentUser: user,
            options: new QueryFilterOptions { FailClosedWhenAnonymous = false });

        provider.GetCombinedFilter<TestEntity>().Should().BeNull();
    }

    [Fact]
    public void GetCombinedFilter_AnonymousWithPermissionFilter_FailsClosedWhenOptionEnabled()
    {
        var user = new FakeCurrentUser(isAuthenticated: false);
        var provider = CreateProvider([new TeamFilter()], currentUser: user,
            options: new QueryFilterOptions { FailClosedWhenAnonymous = true });

        var combined = provider.GetCombinedFilter<TestEntity>();
        combined.Should().NotBeNull("fail-closed must return an explicit no-rows predicate");
        combined!.Compile()(new TestEntity { TeamId = "A" }).Should().BeFalse();
    }

    [Fact]
    public void GetCombinedFilter_AuthenticatedUser_NotAffectedByFailClosedOption()
    {
        var user = new FakeCurrentUser(isAuthenticated: true, permissions: ["view_own"]);
        var provider = CreateProvider([new TeamFilter()], currentUser: user,
            options: new QueryFilterOptions { FailClosedWhenAnonymous = true });

        // Authenticated → real permission filter applies, not the fail-closed predicate.
        var combined = provider.GetCombinedFilter<TestEntity>();
        combined.Should().NotBeNull();
        combined!.Compile()(new TestEntity { TeamId = "A" }).Should().BeTrue();
    }

    [Fact]
    public void GetCombinedFilter_AnonymousNoPermissionFilter_NotFailedClosed()
    {
        // Only a non-permission filter present → fail-closed must NOT trigger.
        var user = new FakeCurrentUser(isAuthenticated: false);
        var provider = CreateProvider([new SoftDeleteFilter()], currentUser: user,
            options: new QueryFilterOptions { FailClosedWhenAnonymous = true });

        var combined = provider.GetCombinedFilter<TestEntity>();
        combined.Should().NotBeNull();
        // SoftDelete still applies (not a blanket no-rows predicate).
        combined!.Compile()(new TestEntity { IsDeleted = false }).Should().BeTrue();
    }

    [Fact]
    public void GetCombinedFilter_AnonymousTenantOnlyEntity_NotFailedClosed_EvenWhenAnotherEntityIsPermissionFiltered()
    {
        // W2 regression: the PRODUCTION type registry is passthrough (ImplementsFilterFor == true for
        // all), so GetFiltersForType returns every registered filter — including other entities'
        // permission filters. ShouldFailClosed must still count only permission filters that apply to
        // THIS entity; otherwise the mere presence of any [HasOwner]/[HasAccessScopes] elsewhere in the
        // app would fail-close anonymous reads of an unrelated tenant-only entity.
        var user = new FakeCurrentUser(isAuthenticated: false);
        var provider = new DefaultQueryFilterProvider(
            [new SoftDeleteFilter(), new OtherEntityPermissionFilter()],
            new PassthroughRegistry(),
            toggle: null,
            currentUser: user,
            options: new QueryFilterOptions { FailClosedWhenAnonymous = true });

        var combined = provider.GetCombinedFilter<TestEntity>();
        combined.Should().NotBeNull("TestEntity has no permission filter of its own; another entity's must not fail-close it");
        combined!.Compile()(new TestEntity { IsDeleted = false }).Should().BeTrue();
    }

    // --- Helpers ---

    // Mirrors the production PassthroughQueryFilterTypeRegistry: returns every filter for any entity.
    private sealed class PassthroughRegistry : IQueryFilterTypeRegistry
    {
        public bool ImplementsFilterFor(Type filterType, Type entityType) => true;
    }

    private sealed class OtherEntityPermissionFilter : IPermissionBasedFilter<OtherEntity>
    {
        public string BypassPermission => "view_all";
        public Expression<Func<OtherEntity, bool>> GetFilter() => _ => true;
    }

    private static DefaultQueryFilterProvider CreateProvider(
        IQueryFilter[] filters,
        IQueryFilterToggle? toggle = null,
        Pragmatic.Identity.ICurrentUser? currentUser = null,
        QueryFilterOptions? options = null)
    {
        return new DefaultQueryFilterProvider(filters, new ReflectionQueryFilterTypeRegistry(), toggle, currentUser, options);
    }

    /// <summary>
    ///     Test-only registry that uses reflection to check IQueryFilter&lt;T&gt; implementation.
    ///     Production code uses the SG-generated registry instead.
    /// </summary>
    private sealed class ReflectionQueryFilterTypeRegistry : IQueryFilterTypeRegistry
    {
        public bool ImplementsFilterFor(Type filterType, Type entityType)
        {
            var targetInterface = typeof(IQueryFilter<>).MakeGenericType(entityType);
            return targetInterface.IsAssignableFrom(filterType);
        }
    }

    // --- Test Types ---

    private class TestEntity
    {
        public bool IsDeleted { get; init; }
        public bool IsActive { get; init; }
        public string TeamId { get; init; } = "A";
    }

    private class OtherEntity { }

    private sealed class SoftDeleteFilter : IQueryFilter<TestEntity>
    {
        public int Priority => 100;

        public Expression<Func<TestEntity, bool>> GetFilter()
            => e => !e.IsDeleted;
    }

    private sealed class ActiveOnlyFilter : IQueryFilter<TestEntity>
    {
        public int Priority => 200;

        public Expression<Func<TestEntity, bool>> GetFilter()
            => e => e.IsActive;
    }

    private sealed class HighPriorityFilter : IQueryFilter<TestEntity>
    {
        public int Priority => 100;

        public Expression<Func<TestEntity, bool>> GetFilter()
            => e => !e.IsDeleted;
    }

    private sealed class LowPriorityFilter : IQueryFilter<TestEntity>
    {
        public int Priority => 300;

        public Expression<Func<TestEntity, bool>> GetFilter()
            => e => e.IsActive;
    }

    private sealed class RootOnlyFilter : IQueryFilter<TestEntity>
    {
        public FilterScope Scope => FilterScope.Root;

        public Expression<Func<TestEntity, bool>> GetFilter()
            => e => !e.IsDeleted;
    }

    private sealed class OtherEntityFilter : IQueryFilter<OtherEntity>
    {
        public Expression<Func<OtherEntity, bool>> GetFilter()
            => _ => true;
    }

    // Scope-visibility filters are OR-composed together, then AND with restrictive ones.
    private sealed class MaterializedScopeFilter : IQueryFilter<TestEntity>, IScopeVisibilityFilter
    {
        public int Priority => 250;
        // Visible only for team "A".
        public Expression<Func<TestEntity, bool>> GetFilter() => e => e.TeamId == "A";
    }

    private sealed class ComputedScopeStubFilter : IQueryFilter<TestEntity>, IScopeVisibilityFilter
    {
        public int Priority => 260;
        // Visible only for team "B" — additive to the materialized filter.
        public Expression<Func<TestEntity, bool>> GetFilter() => e => e.TeamId == "B";
    }

    private sealed class PassthroughScopeFilter : IQueryFilter<TestEntity>, IScopeVisibilityFilter
    {
        public int Priority => 260;
        // No applicable rules → pass-through. Must NOT widen the OR group to "all rows".
        public Expression<Func<TestEntity, bool>> GetFilter() => _ => true;
    }

    private sealed class TeamFilter : IPermissionBasedFilter<TestEntity>
    {
        public string BypassPermission => "view_all";
        public int Priority => 300;

        public Expression<Func<TestEntity, bool>> GetFilter()
            => e => e.TeamId == "A";
    }
}
