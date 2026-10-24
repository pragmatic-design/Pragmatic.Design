using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Pragmatic's declared queries on the Sql engine, through <c>IReadRepository.RunAsync</c> as Pragmatic's generated invokers call it, on the
///     five databases: the filters as Pragmatic composes them (ToSpecification, compiled leaves), the sort and its default, the page and the
///     total, the projection into a DTO.
/// </summary>
[Collection("pragmatic databases")]
public class DeclaredQueryTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<(SqlSession Session, IReadRepository<Review> Reviews, List<Review> All)> Seeded(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
            return (session, new Review.SqlReadRepository(session), await session.Query<Review>().ToListAsync());
    }

    /// <summary>The page's texts and total, as one string: a tuple holding an array compares the array by reference.</summary>
    private static async Task<string> Run(IReadRepository<Review> reviews, SearchReviewsQuery query)
    {
        var page = await reviews.RunAsync(query);
        return Page([.. page.Items.Select(r => r.Text)], page.TotalCount);
    }

    private static string Page(string[] texts, int total) => $"[{string.Join(", ", texts)}] of {total}";

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_declared_query_filters_sorts_and_pages_as_declared(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, reviews, all) = await Seeded(dialect);
        using (session)
        using (_toggle.UseMode(FilterMode.Raw)) // the query's own semantics, no family in the way
        {
            Assert.Equal(Page(["bruno@RIVA", "anna@RIVA"], 4), await Run(reviews, new SearchReviewsQuery())); // the default sort, descending; the first page of two
            Assert.Equal(Page(["anna@MARE", "anna@ALPE"], 4), await Run(reviews, new SearchReviewsQuery { Page = 2 }));
            Assert.Equal(Page(["anna@ALPE", "anna@MARE"], 4), await Run(reviews, new SearchReviewsQuery { TextSort = SortDirection.Ascending }));
            Assert.Equal(Page(["bruno@RIVA", "anna@RIVA"], 2), await Run(reviews, new SearchReviewsQuery { Text = "RIVA" })); // Contains
            Assert.Equal(Page(["anna@ALPE"], 1), await Run(reviews, new SearchReviewsQuery { Published = false }));
            Assert.Equal(Page(["anna@RIVA", "anna@MARE"], 3), await Run(reviews, new SearchReviewsQuery { Prefix = "ANNA@" })); // StartsWith, ignoring case
            var riva = all.First(r => r.Text == "anna@RIVA").PropertyId;
            Assert.Equal(Page(["bruno@RIVA", "anna@RIVA"], 2), await Run(reviews, new SearchReviewsQuery { Properties = [riva] })); // In
            Assert.Equal(Page(["bruno@RIVA", "anna@RIVA"], 4), await Run(reviews, new SearchReviewsQuery { Properties = [] })); // an empty collection is no filter
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_declared_query_reads_inside_the_callers_filters_and_projects(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, reviews, _) = await Seeded(dialect);
        using (session)
        {
            Assert.Equal(Page(["anna@RIVA", "anna@MARE"], 2), await Run(reviews, new SearchReviewsQuery())); // anna's published ones
            var lines = await reviews.RunAsync(new ReviewLinesQuery { TextSort = SortDirection.Ascending });
            Assert.Equal(["anna@MARE", "anna@RIVA"], lines.Select(l => l.Text));
            Assert.All(lines, l => Assert.True(l.Published));
            Assert.Equal(["anna@RIVA"], (await reviews.RunAsync(new ReviewLinesQuery { Text = "anna@RIVA" })).Select(l => l.Text));
        }
    }

    [Fact]
    public void ToSpecification_is_the_filters_Pragmatic_composes()
    {
        var specification = new SearchReviewsQuery { Text = "RIVA", Published = true }.ToSpecification();
        var reviews = new[] { new Review { Text = "anna@RIVA", Published = true }, new Review { Text = "anna@RIVA", Published = false }, new Review { Text = "anna@MARE", Published = true } };
        var matches = specification.ToExpression().Compile();
        Assert.Single(reviews, r => matches(r));
    }
}
