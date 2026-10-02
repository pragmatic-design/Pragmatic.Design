using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Queries;

/// <summary>
///     One declared query, two ways in: its own route, and an operation that runs it through the
///     repository it already holds.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Without the second way, reusing a <c>[Query]</c> inside a <c>[DomainAction]</c> means
///         resolving <c>IQueryExecutor</c>, obtaining a raw entity set and choosing an overload, so
///         applications write the LINQ again by hand and the declared read is reachable only from its
///         own route.
///     </para>
///     <para>
///         The query here is deliberate: <c>SearchDeactivatedPropertiesQuery</c> carries
///         <c>[WithoutFilter&lt;ActiveOnly&gt;]</c>, and that is where the two possible sources
///         diverge. <c>RunAsync</c> hands the executor <c>Set</c>; handing it <c>Query()</c> — already
///         <c>ApplyFilters(Set)</c>, and <c>ApplyFilters</c> calls <c>IgnoreQueryFilters</c>, of which
///         EF keeps the last in a chain — would change which filters are in force rather than repeat
///         them. On an ordinary query both answer the same rows and nothing would show it.
///     </para>
/// </remarks>
public class TheSameDeclaredQueryBothWaysTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>The route and the operation see the same deactivated properties.</summary>
    [Fact]
    public async Task TheRouteAndTheOperation_SeeTheSameRows()
    {
        var city = $"Cit{Guid.NewGuid():N}"[..12];
        var deactivated = await ADeactivatedPropertyAsync(city);
        await AnActivePropertyAsync(city);

        var throughTheRoute = await GetAsync<JsonElement>(
            $"/api/properties/deactivated?city={city}&pageSize=100");

        var rows = throughTheRoute.GetProperty("items").EnumerateArray().ToList();

        // ⚠️ [WithoutFilter<ActiveOnly>] lifts the rule; it does not narrow to what the rule hides. So
        // the route answers with both, and the deactivated one is the row an ordinary read cannot see.
        rows.Select(p => p.GetProperty("id").GetGuid()).Should().Contain(deactivated);
        rows.Count.Should().Be(2, "the city has one active property and one that is switched off");

        var offThroughTheRoute = rows.Count(p => !p.GetProperty("isActive").GetBoolean());

        // The operation runs the same query through its repository and acts on what it found.
        var reactivated = await PostAsync<int>("/api/properties/reactivate-city", new { city });

        reactivated.Should().Be(offThroughTheRoute,
            "the two ways in answer with the same rows, so the operation switched on exactly what the "
            + "route reports as off");
    }

    /// <summary>
    ///     The control: the lifted filter is lifted on this path too.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the assertion that separates <c>Set</c> from <c>Query()</c>. A deactivated row is
    ///     invisible to every ordinary read — <c>[VisibleWhen&lt;ActiveOnly&gt;]</c> on the entity — so
    ///     an operation that found and reactivated it can only have run the query with its
    ///     <c>[WithoutFilter]</c> honoured. After it runs, the row is visible to an ordinary search,
    ///     which is what proves it was there and hidden rather than absent.
    /// </remarks>
    [Fact]
    public async Task TheLiftedFilter_IsLiftedThroughTheRepositoryToo()
    {
        var city = $"Cit{Guid.NewGuid():N}"[..12];
        var hidden = await ADeactivatedPropertyAsync(city);

        var beforeSearch = await GetAsync<JsonElement>($"/api/properties/search?city={city}&pageSize=100");
        beforeSearch.GetProperty("items").EnumerateArray()
            .Any(p => p.GetProperty("id").GetGuid() == hidden)
            .Should().BeFalse("an ordinary read does not see a deactivated property");

        var reactivated = await PostAsync<int>("/api/properties/reactivate-city", new { city });
        reactivated.Should().Be(1, "the operation saw what the ordinary read cannot");

        // The same ordinary search as before, and it now answers with the row. ⚠️ It is also the second
        // assertion in this method: /api/properties/search is [Cacheable(Duration = "5m", Tags =
        // ["properties"])] and this call has the same key as the one above, so a page taken before the
        // write would still answer. It does not, because the action drops the "properties" tag —
        // and this line is the E2E that keeps it so.
        var afterSearch = await GetAsync<JsonElement>($"/api/properties/search?city={city}&pageSize=100");

        afterSearch.GetProperty("items").EnumerateArray()
            .Any(p => p.GetProperty("id").GetGuid() == hidden)
            .Should().BeTrue("and the row it acted on is the one that was hidden");
    }

    private async Task<Guid> AnActivePropertyAsync(string city)
    {
        var created = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"RQ-{Guid.NewGuid():N}"[..12],
            name = $"RunQ-{Guid.NewGuid():N}"[..20],
            city,
            country = "IT",
            starRating = 3
        });

        return created.GetProperty("id").GetGuid();
    }

    private async Task<Guid> ADeactivatedPropertyAsync(string city)
    {
        var id = await AnActivePropertyAsync(city);

        var response = await PutAsync($"/api/properties/{id}", new { isActive = false });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "the property is switched off before the read");

        return id;
    }
}
