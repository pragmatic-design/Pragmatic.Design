using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A declared query's <c>[ComplexFilter]</c> on the five databases, through <c>IReadRepository.RunAsync</c>: the filter object composed as
///     Pragmatic's <c>[FilterDto]</c> composes it for EF Core, an unset member filtering nothing, the members of an Or group joined with OR, the
///     group joined with the object's other members with AND. The reviews: anna@RIVA (5 stars), bruno@RIVA (3), anna@MARE (4), anna@ALPE (2).
/// </summary>
[Collection("pragmatic databases")]
public class ComplexFilterTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private static async Task<string[]> Texts(Review.SqlReadRepository reviews, ReviewFilter? where)
        => [.. (await reviews.RunAsync(new FilterReviewsQuery { Where = where })).Select(r => r.Text)];

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_filter_object_is_composed_as_Pragmatic_composes_it(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        var reviews = new Review.SqlReadRepository(session);
        using (_toggle.UseMode(FilterMode.Raw)) // the query's own semantics, no family in the way
        {
            string[] all = ["anna@ALPE", "anna@MARE", "anna@RIVA", "bruno@RIVA"];
            Assert.Equal(all, await Texts(reviews, null));
            Assert.Equal(all, await Texts(reviews, new ReviewFilter())); // nothing set: no filter
            Assert.Equal(all, await Texts(reviews, new ReviewFilter { Source = new ReviewSourceFilter() }));
            Assert.Equal(["anna@MARE", "bruno@RIVA"], await Texts(reviews, new ReviewFilter { Source = new ReviewSourceFilter { At = "@MARE", By = "bruno" } })); // OR
            Assert.Equal(["anna@MARE", "anna@RIVA"], await Texts(reviews, new ReviewFilter { MinStars = 4 }));
            Assert.Equal(["anna@RIVA"], await Texts(reviews, new ReviewFilter { Source = new ReviewSourceFilter { At = "@RIVA" }, MinStars = 4 })); // the group AND the member
            Assert.Equal(["anna@MARE"], await Texts(reviews, new ReviewFilter { Source = new ReviewSourceFilter { At = "@MARE", By = "bruno" }, MinStars = 4 }));
        }
    }

    /// <summary>The specification is the one EF Core runs: the same composition, evaluated in memory.</summary>
    [Fact]
    public void ToSpecification_composes_the_group_with_OR_and_the_object_with_AND()
    {
        var matches = new FilterReviewsQuery { Where = new ReviewFilter { Source = new ReviewSourceFilter { At = "@MARE", By = "bruno" }, MinStars = 4 } }
            .ToSpecification().ToExpression().Compile();
        var reviews = new[] { new Review { Text = "anna@MARE", Stars = 4 }, new Review { Text = "bruno@RIVA", Stars = 3 }, new Review { Text = "anna@RIVA", Stars = 5 } };
        Assert.Equal(["anna@MARE"], reviews.Where(matches).Select(r => r.Text));
    }
}
