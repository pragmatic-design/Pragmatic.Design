using System.Text.Json;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Filters;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     Pragmatic's <c>[GridAdapter]</c> on the documents engine, on MongoDB and Cosmos DB: the Sql engine's grid tests (<c>GridTests</c> there),
///     the same filters and the same oracle (the specification read in memory, <c>ToExpression</c>). The refusals are the specification's, the
///     Sql engine's file: its test there covers them.
/// </summary>
[Collection("document servers")]
public class GridTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private static readonly DevExpressSortDescriptor[] ByText = [new() { Selector = "text" }];

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

    private static IReadOnlyList<object?> Parsed(string json) => JsonSerializer.Deserialize<List<object?>>(json)!;

    private static string Texts(IEnumerable<Review> rows) => string.Join(", ", rows.Select(r => r.Text));

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_grid_filter_reads_on_the_store_what_its_specification_reads_in_memory(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, all) = await DeclaredQueryTests.ReviewsAsync(servers, store, _toggle);
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
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_grid_sorts_skips_takes_and_counts_as_asked(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await DeclaredQueryTests.ReviewsAsync(servers, store, _toggle);
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
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_grid_reads_inside_the_callers_filters(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, _) = await DeclaredQueryTests.ReviewsAsync(servers, store, _toggle);
        var page = await ReviewGrid.LoadAsync(session, new DevExpressLoadOptions { Filter = Parsed("""["text", "contains", "@"]"""), Sort = ByText, RequireTotalCount = true });
        Assert.Equal("anna@MARE, anna@RIVA", Texts(page.Items)); // anna's published ones: the owner and the rule, whatever the grid asks
        Assert.Equal(2, page.TotalCount);
    }
}
