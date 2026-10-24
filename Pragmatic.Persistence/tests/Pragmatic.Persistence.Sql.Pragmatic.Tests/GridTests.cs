using System.Text.Json;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Pragmatic;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Pragmatic's <c>[GridAdapter]</c> on the Sql engine, on the five databases: a DevExpress filter (clauses, groups, negations, as CLR values
///     and as the JsonElements a request body holds) read into compiled leaves, its sort, its skip and take, its total; the same specification
///     read in memory as the oracle (<c>ToExpression</c>, what EF Core reads); every part that cannot apply refused, as Pragmatic's adapter refuses it.
/// </summary>
[Collection("pragmatic databases")]
public class GridTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private static readonly DevExpressSortDescriptor[] ByText = [new() { Selector = "text" }];

    /// <summary>The filters the oracle checks: a clause per family of operator, a composite, a negation, a filter as a request body parses it.</summary>
    private static readonly string[] Filters =
    [
        """["stars", ">=", 4]""",
        """["text", "contains", "RIVA"]""",
        """[["text", "contains", "RIVA"], "or", ["published", "=", false]]""",
        """["!", ["text", "startswith", "anna"]]""",
        """[["stars", "<=", 4], "and", ["text", "endswith", "RIVA"]]""",
        """[["text", "<>", "anna@RIVA"], "and", [["published", "=", true], "or", ["stars", "equals", 2]]]""",
        """[["text", "notcontains", "MARE"]]""",
    ];

    private async Task<(SqlSession Session, List<Review> All)> Seeded(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
            return (session, await session.Query<Review>().ToListAsync());
    }

    private static IReadOnlyList<object?> Parsed(string json) => JsonSerializer.Deserialize<List<object?>>(json)!;

    private static string Texts(IEnumerable<Review> rows) => string.Join(", ", rows.Select(r => r.Text));

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_grid_filter_reads_on_the_database_what_its_specification_reads_in_memory(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, all) = await Seeded(dialect);
        using (session)
        using (_toggle.UseMode(FilterMode.Raw)) // the grid's own semantics, no family in the way
        {
            foreach (var json in Filters)
            {
                var matches = ReviewGrid.ToSpecification(Parsed(json)).ToExpression().Compile();
                var page = await ReviewGrid.LoadAsync(session, new DevExpressLoadOptions { Filter = Parsed(json), Sort = ByText, RequireTotalCount = true });
                var expected = all.Where(matches).OrderBy(r => r.Text, StringComparer.Ordinal).ToList();
                Assert.True(expected.Count > 0, json); // a filter that matches nothing would prove nothing
                Assert.Equal($"{json}: {Texts(expected)}", $"{json}: {Texts(page.Items)}");
                Assert.Equal(expected.Count, page.TotalCount);
            }
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_grid_sorts_skips_takes_and_counts_as_asked(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var byStars = new DevExpressLoadOptions { Sort = [new() { Selector = "stars", Desc = true }], Skip = 1, Take = 2, RequireTotalCount = true };
            var page = await ReviewGrid.LoadAsync(session, byStars);
            Assert.Equal("anna@MARE, bruno@RIVA", Texts(page.Items));
            Assert.Equal(4, page.TotalCount);

            var rest = await ReviewGrid.LoadAsync(session, new DevExpressLoadOptions { Sort = ByText, Skip = 2 }); // a skip without a take, no total asked
            Assert.Equal("anna@RIVA, bruno@RIVA", Texts(rest.Items));
            Assert.Null(rest.TotalCount);

            var unknownSort = await ReviewGrid.LoadAsync(session, new DevExpressLoadOptions { Sort = [new() { Selector = "ownerId" }, new() { Selector = "text", Desc = true }], Take = 1 });
            Assert.Equal("bruno@RIVA", Texts(unknownSort.Items)); // a column the grid does not sort on is passed over, as Pragmatic's adapter passes it
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_grid_reads_inside_the_callers_filters(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await Seeded(dialect);
        using (session)
        {
            var page = await ReviewGrid.LoadAsync(session, new DevExpressLoadOptions { Filter = Parsed("""["text", "contains", "@"]"""), Sort = ByText, RequireTotalCount = true });
            Assert.Equal("anna@MARE, anna@RIVA", Texts(page.Items)); // anna's published ones: the owner and the rule, whatever the grid asks
            Assert.Equal(2, page.TotalCount);
        }
    }

    [Fact]
    public void What_a_grid_cannot_apply_is_refused_not_left_out()
    {
        static Exception Refusal(object filter) => Assert.ThrowsAny<Exception>(() => ReviewGrid.ToSpecification(filter as IReadOnlyList<object?> ?? Parsed((string)filter)));

        var unknown = Assert.IsType<GridFieldRejectedException>(Refusal("""["ownerId", "=", "anna"]"""));
        Assert.Equal(GridFieldRejection.Unknown, unknown.Rejection);
        Assert.Equal(GridFilterRejection.UnsupportedOperator, Assert.IsType<GridFilterRejectedException>(Refusal("""["stars", ">", 3]""")).Rejection); // not in AllowedOperators
        Assert.Equal(GridFilterRejection.UnsupportedOperator, Assert.IsType<GridFilterRejectedException>(Refusal("""["published", "contains", true]""")).Rejection);
        Assert.Equal(GridFilterRejection.UnusableValue, Assert.IsType<GridFilterRejectedException>(Refusal("""["stars", ">=", "many"]""")).Rejection);
        Assert.Equal(GridFilterRejection.UnusableValue, Assert.IsType<GridFilterRejectedException>(Refusal("""["stars", "equals", null]""")).Rejection); // not nullable
        Assert.Equal(GridFilterRejection.Malformed, Assert.IsType<GridFilterRejectedException>(Refusal("""["text", "contains"]""")).Rejection);
        Assert.Equal(GridFilterRejection.Malformed, Assert.IsType<GridFilterRejectedException>(Refusal("""[["text", "=", "a"], "xor", ["text", "=", "b"]]""")).Rejection);

        var wide = new List<object?> { new List<object?> { "text", "=", "x" } };
        for (var i = 0; i < GridFilterTooLargeException.MaxTerms; i++)
            wide.AddRange(["or", new List<object?> { "text", "=", "x" }]);
        Assert.IsType<GridFilterTooLargeException>(Refusal(wide));
    }
}
