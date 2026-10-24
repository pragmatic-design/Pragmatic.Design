using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Pragmatic;
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
            {
                var comment = new ReviewComment { Text = "on " + review.Text };
                comment.SetReviewId(review.Id);
                await session.InsertAsync(comment);
            }

            foreach (var property in await session.Query<Property>().ToListAsync())
            {
                var note = new PropertyNote { Text = "on " + property.Name };
                note.SetPropertyId(property.Id);
                await session.InsertAsync(note);
            }
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
        Assert.Equal(["on anna@ALPE", "on anna@MARE", "on anna@RIVA"], await session.Query<ReviewComment>().OrderBy(c => c.Text).Select(c => c.Text).ToListAsync());
        // The properties sharing desk-north (RIVA and ALPE; the tenant is the property's family, not restated on the note).
        Assert.Equal(["on ALPE", "on RIVA"], await session.Query<PropertyNote>().OrderBy(n => n.Text).Select(n => n.Text).ToListAsync());
        Assert.Equal(2, await session.Query<PropertyNote>().CountAsync());
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

    /// <summary>The bypass permission of Review, as Pragmatic names it (<c>{boundary}.{entity}.view-all</c>): read off its filter class.</summary>
    private static class PragmaticPermissionsOf
    {
        public static readonly string ReviewViewAll = new ReviewComment.ParentVisibilityFilter(new TestUser("x")).BypassPermission;
    }
}
