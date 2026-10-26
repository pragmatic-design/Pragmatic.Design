using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A trait's child visible only where its parent is (Pragmatic's <c>ParentVisibilityFilter</c>: the parent's owner or scopes, restated
///     through the navigation, its bypass the parent's), on the five databases. The children: a comment on each review, a note on each property
///     (<see cref="PragmaticRows" />); anna owns her reviews and holds desk-north (RIVA, ALPE).
/// </summary>
[Collection("pragmatic databases")]
public class TraitParentVisibilityTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<SqlSession> Seeded(SqlDialect dialect, string[]? permissions = null)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna", permissions), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
        {
            foreach (var review in await session.Query<Review>().ToListAsync())
                await session.InsertAsync(new ReviewComment { Content = "on " + review.Text, ReviewId = review.Id });

            foreach (var property in await session.Query<Property>().ToListAsync())
                await session.InsertAsync(new PropertyNote { Content = "on " + property.Name, AuthorId = "anna", AuthorName = "Anna", PropertyId = property.Id });
        }

        return session;
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_childs_rows_are_those_of_the_parents_the_caller_may_see(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        // anna's reviews (the rule on Review is the review's, not the comment's: her unpublished one counts), not bruno's.
        Assert.Equal(["on anna@ALPE", "on anna@MARE", "on anna@RIVA"], await session.Query<ReviewComment>().OrderBy(c => c.Content).Select(c => c.Content).ToListAsync());
        // The properties sharing desk-north and of acme: RIVA. ALPE shares the scope and is another tenant's, so is its note (EF's "ParentTenant").
        Assert.Equal(["on RIVA"], await session.Query<PropertyNote>().OrderBy(n => n.Content).Select(n => n.Content).ToListAsync());
        Assert.Equal(1, await session.Query<PropertyNote>().CountAsync());
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_childs_tenant_is_its_parents_and_no_permission_crosses_it(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        // desk-north's view-all is no way into another tenant: ALPE's note stays out.
        using (var bypass = await Seeded(dialect, [PragmaticPermissionsOf.PropertyViewAll]))
            Assert.Equal(["on MARE", "on RIVA"], await bypass.Query<PropertyNote>().OrderBy(n => n.Content).Select(n => n.Content).ToListAsync());
        // Lifted by name, as EF's IgnoreQueryFilters(["ParentTenant"]) with the entity's filters disabled: every note.
        using (var session = await Seeded(dialect))
        using (_toggle.DisableQueryFilter("ParentTenant"))
        using (_toggle.Disable(typeof(PropertyNote)))
            Assert.Equal(3, await session.Query<PropertyNote>().CountAsync());
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Admin_and_the_parents_bypass_lift_it(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using (var session = await Seeded(dialect))
        using (_toggle.UseMode(FilterMode.Admin))
            Assert.Equal(4, await session.Query<ReviewComment>().CountAsync());
        using (var bypass = await Seeded(dialect, [PragmaticPermissionsOf.ReviewViewAll]))
            Assert.Equal(4, await bypass.Query<ReviewComment>().CountAsync()); // the review's view-all: the comments follow it
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_deleted_child_is_out_of_the_reads(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var riva = (await session.Query<Property>().Where(p => p.Name == "RIVA").ToListAsync()).Single();
            await session.InsertAsync(new PropertyNote { Content = "gone", AuthorId = "anna", AuthorName = "Anna", PropertyId = riva.Id, IsDeleted = true });
        }

        Assert.Equal(["on RIVA"], await session.Query<PropertyNote>().Select(n => n.Content).ToListAsync());
        Assert.Equal(["gone", "on RIVA"], await session.Query<PropertyNote>().IncludeDeleted().OrderBy(n => n.Content).Select(n => n.Content).ToListAsync());
    }

    /// <summary>The bypass permission of Review, as Pragmatic names it (<c>{boundary}.{entity}.view-all</c>): read off its filter class.</summary>
    private static class PragmaticPermissionsOf
    {
        public static readonly string ReviewViewAll = new ReviewComment.ParentVisibilityFilter(new TestUser("x")).BypassPermission;

        public static readonly string PropertyViewAll = new PropertyNote.ParentVisibilityFilter(new TestScopes("desk-north"), new TestUser("x")).BypassPermission;
    }
}
