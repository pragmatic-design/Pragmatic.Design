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
    public async Task A_query_derived_from_a_specification_filters_by_its_rule(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, reviews, _) = await Seeded(dialect);
        using (session)
        using (_toggle.UseMode(FilterMode.Raw))
        {
            // Published() & By(owner): anna's unpublished ALPE is out, and the owner is the query's input.
            Assert.Equal(["anna@MARE", "anna@RIVA"], (await reviews.RunAsync(new PublishedByQuery { Owner = "anna" })).Select(r => r.Text).Order(StringComparer.Ordinal));
            Assert.Equal(["bruno@RIVA"], (await reviews.RunAsync(new PublishedByQuery { Owner = "bruno" })).Select(r => r.Text));
            Assert.Empty(await reviews.RunAsync(new PublishedByQuery { Owner = "nobody" }));
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_query_declared_paged_gets_its_page_and_its_default_size(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, reviews, _) = await Seeded(dialect);
        using (session)
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var second = await reviews.RunAsync(new PagedReviewsQuery { Page = 2, PageSize = 3 });
            var whole = await reviews.RunAsync(new PagedReviewsQuery());

            Assert.Equal(Page(["bruno@RIVA"], 4), Page([.. second.Items.Select(r => r.Text)], second.TotalCount)); // the ascending default sort, past the first three
            Assert.Equal(Page(["anna@ALPE", "anna@MARE", "anna@RIVA", "bruno@RIVA"], 4), Page([.. whole.Items.Select(r => r.Text)], whole.TotalCount)); // page 1 of twenty
            Assert.Equal((1, 20), (new PagedReviewsQuery().Page, new PagedReviewsQuery().PageSize));
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_grouped_view_counts_sums_and_takes_the_maximum_per_group_inside_the_filters(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, reviews, _) = await Seeded(dialect);
        using (session)
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var all = await reviews.RunAsync(new ReviewsByPublicationQuery());
            var riva = await reviews.RunAsync(new ReviewsByPublicationQuery { Text = "RIVA" });

            // Published: RIVA 5 and 3, MARE 4; not published: ALPE 2.
            Assert.Equal([(false, 1, 2, 2), (true, 3, 12, 5)], all.Select(g => (g.Published, g.Reviews, g.Stars, g.Best)).OrderBy(g => g.Published));
            // Only RIVA's, both published: the unpublished group has no row inside the filter, and is not there.
            Assert.Equal([(true, 2, 8, 5)], riva.Select(g => (g.Published, g.Reviews, g.Stars, g.Best)));
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_declared_join_reads_the_aliased_side_keeps_on_left_and_drops_on_inner(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, reviews, _) = await Seeded(dialect);
        using (session)
        {
            // Anna (acme, desk-north) sees her published reviews, at RIVA and MARE, and only RIVA among the properties (MARE is desk-south).
            var left = await reviews.RunAsync(new ReviewPlacesQuery());
            var inner = await reviews.RunAsync(new ReviewPlacesInnerQuery());

            Assert.Equal([("anna@MARE", null), ("anna@RIVA", "RIVA")], left.Select(r => (r.Text, r.PlaceName)));
            Assert.Equal([("anna@RIVA", "RIVA")], inner.Select(r => (r.Text, r.PlaceName)));

            using (_toggle.UseMode(FilterMode.Raw))
            {
                // Every review and every property: the name read from each one's own property, the query's filter and sort applied.
                var raw = await reviews.RunAsync(new ReviewPlacesQuery { TextSort = SortDirection.Descending });
                var riva = await reviews.RunAsync(new ReviewPlacesQuery { Text = "RIVA" });

                Assert.Equal([("bruno@RIVA", "RIVA"), ("anna@RIVA", "RIVA"), ("anna@MARE", "MARE"), ("anna@ALPE", "ALPE")], raw.Select(r => (r.Text, r.PlaceName)));
                Assert.Equal([("anna@RIVA", "RIVA"), ("bruno@RIVA", "RIVA")], riva.Select(r => (r.Text, r.PlaceName)));
            }
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_query_that_maps_in_memory_reads_its_navigation_and_runs_the_selector(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _, _) = await Seeded(dialect);
        using (session)
        using (_toggle.UseMode(FilterMode.Raw))
        {
            IReadRepository<Reservation> reservations = new Reservation.SqlReadRepository(session);
            var annas = await reservations.RunAsync(new ReservationCardsQuery { OwnerId = "anna" });
            var second = await reservations.RunAsync(new ReservationCardsQuery { Page = 2, PageSize = 3 });

            Assert.Equal(["anna@ALPE", "anna@MARE", "anna@RIVA"], annas.Items.Select(c => c.Card).Order()); // the property's name, read through the include
            Assert.Equal(3, annas.TotalCount);
            Assert.Single(second.Items); // four reservations: the second page of three holds one, mapped
            Assert.Equal(4, second.TotalCount);
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
