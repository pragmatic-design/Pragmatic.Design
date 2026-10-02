using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     <c>POST /api/properties/devexpress</c>: a grid framework's own format, filtered and
///     sorted as the grid asked.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ This endpoint is one of two grid paths Showcase shows and the only one that speaks a grid
///         vendor's native format. The assertions below are what tell a working translation from one
///         that quietly returns everything.
///     </para>
///     <para>
///         ⚠️ <b>The compile-time alternative is what these should have been written against.</b>
///         <c>[GridAdapter&lt;T&gt;]</c> generates the same translation without reflection, and with a
///         column nobody declared unreachable. A filter that arrives as JSON is a <c>JsonElement</c>,
///         not a string, and these tests are what show the generated parser reads it.
///     </para>
/// </remarks>
public class TheGridInItsOwnFormatTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Grid = "/api/properties/devexpress";

    [Fact]
    public async Task AFilterTheGridSends_IsApplied()
    {
        var city = $"Gen{Guid.NewGuid():N}"[..12];
        await SeedPropertyAsync(city, starRating: 3);
        await SeedPropertyAsync($"Other{Guid.NewGuid():N}"[..12], starRating: 3);

        var items = await RowsAsync(new
        {
            filter = new object[] { "city", "=", city },
            skip = 0,
            take = 20
        });

        items.GetArrayLength().Should().Be(1, "one property is in that city");
        items[0].GetProperty("city").GetString().Should().Be(city);
    }

    /// <summary>
    ///     The control: with no filter the same grid returns more than the one row above.
    /// </summary>
    /// <remarks>
    ///     Without it, "the filter is applied" is satisfied by an adapter that returns one row whatever
    ///     it is asked — and by a seed that produced one property.
    /// </remarks>
    [Fact]
    public async Task WithNoFilter_TheGridReturnsTheRest()
    {
        await SeedPropertyAsync($"Any{Guid.NewGuid():N}"[..12], starRating: 3);
        await SeedPropertyAsync($"Any{Guid.NewGuid():N}"[..12], starRating: 4);

        var items = await RowsAsync(new { skip = 0, take = 50 });

        items.GetArrayLength().Should().BeGreaterThan(1);
    }

    /// <summary>
    ///     A column the adapter does not declare cannot be filtered on — and the request is still served.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <c>TenantId</c> is the column the row filter uses. It comes from the token and never
    ///         from the caller, and a runtime adapter would resolve it by name like any other property,
    ///         because a name is all it has.
    ///         <c>[GridExclude]</c> is what takes it back, and a generated <c>switch</c> is what makes
    ///         the exclusion a boundary rather than a convention.
    ///     </para>
    ///     <para>
    ///         The filter is <b>ignored</b> rather than refused, which is what the generated dispatcher
    ///         does with a name it does not know — so the assertion is that the answer is the unfiltered
    ///         one, not an error.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AColumnTheGridDoesNotDeclare_CannotBeFilteredOn()
    {
        await SeedPropertyAsync($"Ten{Guid.NewGuid():N}"[..12], starRating: 3);

        var everything = await RowsAsync(new { skip = 0, take = 50 });
        var asking = await RowsAsync(new
        {
            filter = new object[] { "tenantId", "=", "another-tenant" },
            skip = 0,
            take = 50
        });

        asking.GetArrayLength().Should().Be(everything.GetArrayLength(),
            "the grid cannot reach a column it was not given, so the filter does nothing — and it "
            + "certainly does not answer with another tenant's rows");
    }

    /// <summary>A sort the grid sends is applied, by the name the grid knows the column as.</summary>
    /// <remarks>
    ///     ⚠️ <c>"stars"</c> and not <c>"starRating"</c>: <c>ThePropertyGrid</c> declares that alias, so
    ///     the front end's own vocabulary is written once instead of being translated at each call
    ///     site. It is a change to this endpoint's wire contract, made deliberately when the endpoint
    ///     moved onto the generated adapter, and <see cref="TheColumnsOldName_IsNotAnswered" /> is what
    ///     keeps it from being a change nobody notices.
    /// </remarks>
    [Fact]
    public async Task ASortTheGridSends_IsApplied()
    {
        var city = $"Srt{Guid.NewGuid():N}"[..12];
        await SeedPropertyAsync(city, starRating: 5);
        await SeedPropertyAsync(city, starRating: 2);

        var items = await RowsAsync(new
        {
            filter = new object[] { "city", "=", city },
            sort = new[] { new { selector = "stars", desc = false } },
            skip = 0,
            take = 20
        });

        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("starRating").GetInt32().Should()
            .BeLessThan(items[1].GetProperty("starRating").GetInt32());
    }

    /// <summary>
    ///     The control: once a column is renamed on the wire, its property name is not a second way in.
    /// </summary>
    /// <remarks>
    ///     An adapter that answered to both would make the alias a suggestion, and would put the
    ///     property names of the entity back into the client contract — which is the thing declaring
    ///     them in one place was for.
    /// </remarks>
    [Fact]
    public async Task TheColumnsOldName_IsNotAnswered()
    {
        var city = $"Old{Guid.NewGuid():N}"[..12];
        await SeedPropertyAsync(city, starRating: 5);
        await SeedPropertyAsync(city, starRating: 2);

        // ⚠️ A filter and not a sort: an ignored sort leaves the rows in whatever order the database
        // felt like, which is not something to assert: a test that does passes until the query plan
        // changes.
        //
        // A composite one, because the city is what narrows this to the two rows the test wrote and
        // the old name is what is on trial. It is also the only assertion anywhere on the AND branch.
        var items = await RowsAsync(new
        {
            filter = new object[]
            {
                new object[] { "city", "=", city }, "and", new object[] { "starRating", ">", 3 }
            },
            skip = 0,
            take = 50
        });

        items.GetArrayLength().Should().Be(2,
            "both rows come back: 'starRating' is not a name this grid knows, so that half of the "
            + "filter did nothing and only the city narrowed it");
    }

    private async Task<JsonElement> RowsAsync(object loadOptions)
    {
        var response = await PostAsync(Grid, new { loadOptions });

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("items");
    }

    private async Task SeedPropertyAsync(string city, int starRating)
    {
        var response = await PostAsync("/api/properties", new
        {
            code = $"GA-{Guid.NewGuid():N}"[..12],
            name = $"GA-{Guid.NewGuid():N}"[..20],
            city,
            country = "IT",
            starRating
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
