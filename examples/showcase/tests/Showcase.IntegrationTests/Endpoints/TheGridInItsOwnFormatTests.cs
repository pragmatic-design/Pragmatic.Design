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
    ///     A column the adapter does not declare cannot be filtered on: the request is refused with a 400.
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
    ///         The filter was <b>ignored</b> until #318, and the answer was the unfiltered page under a 200, which a
    ///         grid shows as the filtered result. Refused, the answer is neither another tenant's rows nor rows
    ///         the client did not ask for.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AColumnTheGridDoesNotDeclare_IsRefused()
    {
        var response = await PostAsync(Grid, new
        {
            loadOptions = new { filter = new object[] { "tenantId", "=", "another-tenant" }, skip = 0, take = 50 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("tenantId");
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
        // ⚠️ A filter and not a sort: an ignored sort leaves the rows in whatever order the database
        // felt like, which is not something to assert: a test that does passes until the query plan
        // changes.
        //
        // A composite one, because the old name is what is on trial inside a filter that is otherwise
        // usable: that half was skipped until #318, and the city alone answered for both.
        var response = await PostAsync(Grid, new
        {
            loadOptions = new
            {
                filter = new object[]
                {
                    new object[] { "city", "=", "Anywhere" }, "and", new object[] { "starRating", ">", 3 }
                },
                skip = 0,
                take = 50
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "'starRating' is not a name this grid knows, and half a filter is not the filter the client sent");
        (await response.Content.ReadAsStringAsync()).Should().Contain("starRating");
    }

    /// <summary>
    ///     The control on the and-branch, now that the case above refuses: both halves known, both apply.
    /// </summary>
    [Fact]
    public async Task AnAndOfTwoKnownColumns_NarrowsByBoth()
    {
        var city = $"And{Guid.NewGuid():N}"[..12];
        await SeedPropertyAsync(city, starRating: 5);
        await SeedPropertyAsync(city, starRating: 2);

        var items = await RowsAsync(new
        {
            filter = new object[]
            {
                new object[] { "city", "=", city }, "and", new object[] { "stars", ">", 3 }
            },
            skip = 0,
            take = 50
        });

        items.GetArrayLength().Should().Be(1, "the city selects two rows and the stars one of them");
        items[0].GetProperty("starRating").GetInt32().Should().Be(5);
    }

    /// <summary>An or of two clauses keeps the rows of either, and nothing else.</summary>
    [Fact]
    public async Task AnOrOfTwoCities_KeepsTheRowsOfEither()
    {
        var first = $"Or1{Guid.NewGuid():N}"[..12];
        var second = $"Or2{Guid.NewGuid():N}"[..12];
        await SeedPropertyAsync(first, starRating: 2);
        await SeedPropertyAsync(second, starRating: 4);
        await SeedPropertyAsync($"Or3{Guid.NewGuid():N}"[..12], starRating: 3);

        var items = await RowsAsync(new
        {
            filter = new object[] { new object[] { "city", "=", first }, "or", new object[] { "city", "=", second } },
            sort = new[] { new { selector = "stars", desc = false } },
            skip = 0,
            take = 50
        });

        Stars(items).Should().Be("2,4", "the two cities named, and not the third");
    }

    /// <summary>
    ///     Each comparison keeps exactly the rows it names, among three of one city with two, three and four stars.
    /// </summary>
    /// <remarks>
    ///     The rows are named by their stars, in the grid's own sort: a comparison that kept every row, or one read the other way round,
    ///     answers with another list.
    /// </remarks>
    [Fact]
    public async Task EachComparison_KeepsExactlyTheRowsItNames()
    {
        var city = $"Cmp{Guid.NewGuid():N}"[..12];
        foreach (var stars in new[] { 2, 3, 4 })
            await SeedPropertyAsync(city, starRating: stars);

        var kept = new List<string>();
        foreach (var op in new[] { ">", ">=", "<", "<=", "<>" })
        {
            var stars = Stars(await RowsAsync(new
            {
                filter = new object[] { new object[] { "city", "=", city }, "and", new object[] { "stars", op, 3 } },
                sort = new[] { new { selector = "stars", desc = false } },
                skip = 0,
                take = 50
            }));
            kept.Add($"{op} {stars}");
        }

        string.Join("; ", kept).Should().Be("> 4; >= 3,4; < 2; <= 2,3; <> 2,4");
    }

    /// <summary>
    ///     Skip and Take are the page, and the total is the filter's count without it: the second page of two holds the third row of a
    ///     descending sort, under a total of three.
    /// </summary>
    [Fact]
    public async Task TheSecondPage_HoldsTheRowsPastTheFirst_UnderTheFiltersTotal()
    {
        var city = $"Pag{Guid.NewGuid():N}"[..12];
        foreach (var stars in new[] { 2, 3, 4 })
            await SeedPropertyAsync(city, starRating: stars);

        var page = await PageAsync(new
        {
            filter = new object[] { "city", "=", city },
            sort = new[] { new { selector = "stars", desc = true } },
            skip = 2,
            take = 2
        });

        Stars(page.GetProperty("items")).Should().Be("2", "four and three are the first page of a descending sort");
        page.GetProperty("totalCount").GetInt32().Should().Be(3, "the total counts the filter's rows, not the page's");
        page.GetProperty("page").GetInt32().Should().Be(2);
    }

    /// <summary>
    ///     Rows that are not one page are refused with a 400: the page around them would answer with rows the grid did not ask for.
    /// </summary>
    /// <remarks>The control is the case above: a Skip that is a multiple of Take is read.</remarks>
    [Fact]
    public async Task ASkipThatIsNotAPage_IsRefused()
    {
        var response = await PostAsync(Grid, new { loadOptions = new { skip = 1, take = 2 } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not one page");
    }

    private static string Stars(JsonElement items)
        => string.Join(",", items.EnumerateArray().Select(i => i.GetProperty("starRating").GetInt32()));

    private async Task<JsonElement> RowsAsync(object loadOptions) => (await PageAsync(loadOptions)).GetProperty("items");

    private async Task<JsonElement> PageAsync(object loadOptions)
    {
        var response = await PostAsync(Grid, new { loadOptions });

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);

        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
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
