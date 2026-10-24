using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Pragmatic;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Pragmatic's <c>IReadRepository&lt;T&gt;</c> on the Sql engine (the generated <c>Review.SqlReadRepository</c>), read through the
///     interface as Pragmatic's generated code reads it, on the five databases: the caller's filters apply to every method.
/// </summary>
[Collection("pragmatic databases")]
public class ReadRepositoryTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_read_repository_answers_within_the_callers_filters(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        List<Review> all;
        using (_toggle.UseMode(FilterMode.Raw))
            all = await session.Query<Review>().ToListAsync();
        IReadRepository<Review> reviews = new Review.SqlReadRepository(session);

        var mine = all.Single(r => r.Text == "anna@RIVA");
        var brunos = all.Single(r => r.Text == "bruno@RIVA");
        Assert.Equal("anna@RIVA", (await reviews.GetByIdAsync(mine.Id))?.Text);
        Assert.Null(await reviews.GetByIdAsync(brunos.Id)); // another owner's: the family filter

        Assert.Equal(["anna@MARE", "anna@RIVA"], (await reviews.FindAsync(ReviewSpecifications.StartingWith("anna@"))).Select(r => r.Text).Order(StringComparer.Ordinal));
        Assert.Equal(1, await reviews.CountAsync(ReviewSpecifications.StartingWith("anna@M")));
        Assert.True(await reviews.ExistsAsync(ReviewSpecifications.Search("anna", onlyPublished: true)));
        Assert.False(await reviews.ExistsAsync(ReviewSpecifications.By("bruno")));
        Assert.Equal("anna@RIVA", (await reviews.FirstOrDefaultAsync(ReviewSpecifications.StartingWith("anna@R")))?.Text);

        var refused = Assert.Throws<NotSupportedException>(() => reviews.Query());
        Assert.Contains("an IQueryable is EF Core's surface", refused.Message, StringComparison.Ordinal);
    }
}
