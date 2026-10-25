using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The read of the query invoker (<see cref="ReviewLoads" />, its form on the Sql engine) on the five databases, inside the caller's
///     filters.
/// </summary>
[Collection("pragmatic databases")]
public class PortingSubstituteTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_query_invoker_reads_through_the_repository(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (session)
        {
            IReadRepository<Review> reviews = new Review.SqlReadRepository(session);
            var page = await ReviewLoads.RunAsync(reviews, new SearchReviewsQuery());
            Assert.Equal(["anna@RIVA", "anna@MARE"], page.Value!.Items.Select(r => r.Text));
            Assert.Equal(2, page.Value.TotalCount);
        }
    }
}
