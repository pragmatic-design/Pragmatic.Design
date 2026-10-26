using Pragmatic.Comments;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The rules of a comment the Sql engine persists, on the five databases: with moderation only a visible comment is read (EF Core's
///     "Moderation" filter), with internal notes an internal comment only by who holds view-internal (EF Core's <c>InternalVisibilityFilter</c>).
///     anna's review at RIVA carries three comments: one visible, one pending, one internal.
/// </summary>
[Collection("pragmatic databases")]
public class CommentRulesTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<SqlSession> Seeded(SqlDialect dialect, string[]? permissions = null)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna", permissions), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var review = (await session.Query<Review>().Where(r => r.Text == "anna@RIVA").ToListAsync()).Single();
            await session.InsertAsync(new ReviewComment { Content = "visible", ReviewId = review.Id });
            await session.InsertAsync(new ReviewComment { Content = "pending", ReviewId = review.Id, Status = CommentStatus.PendingApproval });
            await session.InsertAsync(new ReviewComment { Content = "internal", ReviewId = review.Id, Visibility = CommentVisibility.Internal });
        }

        return session;
    }

    private static Task<List<string>> Contents(SqlSession session) => session.Query<ReviewComment>().OrderBy(c => c.Content).Select(c => c.Content).ToListAsync().AsTask();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_pending_or_internal_comment_is_out_of_a_readers_reads(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        Assert.Equal(["visible"], await Contents(session));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_moderation_rule_is_lifted_by_its_name_and_by_nothing_else(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using (var session = await Seeded(dialect))
        using (_toggle.DisableQueryFilter(VisibilityFilterName.Of<ReviewComment.ModerationRule>()))
            Assert.Equal(["pending", "visible"], await Contents(session));
        // Admin lifts the permission filters, not a rule.
        using (var admin = await Seeded(dialect))
        using (_toggle.UseMode(FilterMode.Admin))
            Assert.Equal(["internal", "visible"], await Contents(admin));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task An_internal_comment_is_read_by_who_holds_view_internal(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect, [new ReviewComment.InternalVisibilityFilter().BypassPermission]);
        Assert.Equal(["internal", "visible"], await Contents(session));
    }
}
