using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Pragmatic;
using Pragmatic.Result.Http;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The reads Pragmatic's templates emit (<see cref="ReviewLoads" />, their form on the Sql engine) on the five databases, inside the
///     caller's filters: a row the caller may not see is a 404 as a missing one is, as it is on EF Core.
/// </summary>
[Collection("pragmatic databases")]
public class PortingSubstituteTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<(SqlSession Session, IReadRepository<Review> Reviews, Dictionary<string, Guid> Ids)> Seeded(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
            return (session, new Review.SqlReadRepository(session), (await session.Query<Review>().ToListAsync()).ToDictionary(r => r.Text, r => r.Id));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_loads_read_by_key_inside_the_callers_filters(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, reviews, ids) = await Seeded(dialect);
        using (session)
        {
            var mine = await ReviewLoads.LoadAsync(reviews, ids["anna@RIVA"]);
            Assert.True(mine.IsSuccess);
            Assert.Equal("anna@RIVA", mine.Value!.Text);
            var hidden = await ReviewLoads.LoadAsync(reviews, ids["bruno@RIVA"]); // bruno's: not the caller's
            Assert.IsType<NotFoundError>(hidden.Error);

            Assert.Null(await ReviewLoads.RequireExistsAsync(reviews, ids["anna@MARE"]));
            Assert.IsType<NotFoundError>(await ReviewLoads.RequireExistsAsync(reviews, ids["anna@ALPE"])); // unpublished: the rule hides it
            Assert.IsType<NotFoundError>(await ReviewLoads.RequireExistsAsync(reviews, Guid.NewGuid()));
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_list_load_answers_in_the_keys_order_or_names_every_key_missing(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, reviews, ids) = await Seeded(dialect);
        using (session)
        {
            var both = await ReviewLoads.LoadManyAsync(reviews, [ids["anna@MARE"], ids["anna@RIVA"], ids["anna@MARE"]]);
            Assert.Equal(["anna@MARE", "anna@RIVA"], both.Value!.Select(r => r.Text));

            var unknown = Guid.NewGuid();
            var missing = await ReviewLoads.LoadManyAsync(reviews, [ids["anna@RIVA"], ids["bruno@RIVA"], unknown]);
            var notFound = Assert.IsType<NotFoundError>(missing.Error);
            Assert.Equal($"{ids["bruno@RIVA"]}, {unknown}", notFound.EntityId); // bruno's is hidden from anna as an unknown key is

            Assert.Empty((await ReviewLoads.LoadManyAsync(reviews, null)).Value!);
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_query_invoker_reads_through_the_repository(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, reviews, _) = await Seeded(dialect);
        using (session)
        {
            var page = await ReviewLoads.RunAsync(reviews, new SearchReviewsQuery());
            Assert.Equal(["anna@RIVA", "anna@MARE"], page.Value!.Items.Select(r => r.Text));
            Assert.Equal(2, page.Value.TotalCount);
        }
    }
}
