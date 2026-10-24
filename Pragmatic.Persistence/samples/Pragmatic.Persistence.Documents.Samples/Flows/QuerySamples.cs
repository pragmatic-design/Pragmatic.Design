using System.Text.Json;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using PragmaticDocumentSamples.Entities;
using PragmaticDocumentSamples.Queries;

namespace PragmaticDocumentSamples.Flows;

/// <summary>
///     Pragmatic's declared queries and grids on both backends, on the reviews <see cref="EntitySamples" /> wrote (anna@RIVA and bruno@RIVA
///     published, "anna draft" not; all of acme), as anna: inside her filters, and Raw. Raw lifts the tenant, so on Cosmos DB the ordered page is
///     read across partitions, range by range, and merged.
/// </summary>
internal static class QuerySamples
{
    public static async Task<SampleRun> RunAsync(string backend, DocSession anna, QueryFilterToggle toggle)
    {
        var checks = new Checks(backend);
        IReadRepository<Review> reviews = new Review.DocReadRepository(anna);
        var mine = await reviews.RunAsync(new ReviewSearchQuery());
        checks.Check("Pragmatic queries: a declared query inside the caller's filters", mine.TotalCount == 1 && mine.Items.Select(l => l.Text).SequenceEqual(["anna@RIVA"]));
        using (toggle.UseMode(FilterMode.Raw))
        {
            var riva = await reviews.RunAsync(new ReviewSearchQuery { Text = "RIVA" });
            checks.Check("Pragmatic queries: filtered, sorted by its default, paged, projected (Raw)", riva.TotalCount == 2 && riva.Items.Select(l => l.Text).SequenceEqual(["bruno@RIVA", "anna@RIVA"]));
            var grid = await ReviewGrid.LoadAsync(anna, new DevExpressLoadOptions
            {
                Filter = JsonSerializer.Deserialize("""["text", "contains", "anna"]""", SampleJson.Default.ListObject),
                Sort = [new DevExpressSortDescriptor { Selector = "text" }],
                Take = 5,
                RequireTotalCount = true,
            });
            checks.Check("Pragmatic queries: a grid's page, its filter as a request body holds it (Raw)", grid.TotalCount == 2 && grid.Items.Select(r => r.Text).SequenceEqual(["anna draft", "anna@RIVA"]));
        }

        return checks.Run;
    }
}
