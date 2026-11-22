using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     Tests the POST /api/properties/grid endpoint.
///     Covers matrix features:
///       - GridFilter [GridFilter&lt;T&gt;] dynamic per-field operator selection
///       - PagedResult response structure
///       - Specification integration (PropertySpecifications.IsActive base filter)
/// </summary>
public class GridSearchTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Grid Search: basic paging
    // =========================================================================

    [Fact]
    public async Task GridSearch_EmptyFilter_ReturnsPagedResult()
    {
        var body = new { filter = new { page = 1, pageSize = 10 } };

        var response = await PostAsync("/api/properties/grid", body);

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        json.TryGetProperty("items", out _).Should().BeTrue("PagedResult should have an 'items' property");
        json.TryGetProperty("totalCount", out _).Should().BeTrue("PagedResult should have a 'totalCount' property");
        json.TryGetProperty("page", out _).Should().BeTrue("PagedResult should have a 'page' property");
        json.TryGetProperty("pageSize", out _).Should().BeTrue("PagedResult should have a 'pageSize' property");
    }

    // =========================================================================
    // Grid Search: name contains filter
    // =========================================================================

    [Fact]
    public async Task GridSearch_NameContains_FiltersResults()
    {
        // Seed a property with a distinctive name
        var uniqueName = $"Grand-{Guid.NewGuid():N}"[..20];
        await CreatePropertyAsync(uniqueName, "Milan", 4);

        var body = new
        {
            filter = new
            {
                name = "Grand",
                nameOperator = "Contains",
                page = 1,
                pageSize = 10
            }
        };

        var response = await PostAsync("/api/properties/grid", body);

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var items = json.GetProperty("items");

        // At least the seeded property should match
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "Grid search with name 'Contains Grand' should return the seeded property");
    }

    // =========================================================================
    // Grid Search: city filter
    // =========================================================================

    [Fact]
    public async Task GridSearch_CityFilter_FiltersResults()
    {
        var uniqueCity = $"City{Guid.NewGuid():N}"[..12];
        await CreatePropertyAsync($"GS-City-{Guid.NewGuid():N}"[..20], uniqueCity, 3);

        var body = new
        {
            filter = new
            {
                city = uniqueCity,
                cityOperator = "Equals",
                page = 1,
                pageSize = 10
            }
        };

        var response = await PostAsync("/api/properties/grid", body);

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var items = json.GetProperty("items");

        items.GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "Grid search with exact city match should return the seeded property");
    }

    /// <summary>
    ///     An operator the property does not allow is refused with 400, through the host (#328).
    /// </summary>
    /// <remarks>
    ///     <c>Name</c> allows the text operators only. An <c>Equals</c> on it was applied as <c>Contains</c>, under a
    ///     200: the grid showed a substring match as the exact one it asked for — which is what the city case above
    ///     did, until the filter declared the operator it uses.
    /// </remarks>
    [Fact]
    public async Task GridSearch_AnOperatorThePropertyDoesNotAllow_IsRefused()
    {
        var response = await PostAsync("/api/properties/grid", new
        {
            filter = new { name = "Grand", nameOperator = "Equals", page = 1, pageSize = 10 }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Name");
        body.Should().Contain("Equals");
    }

    // =========================================================================
    // Grid Search: star rating range filter
    // =========================================================================

    /// <summary>
    ///     A minimum and a maximum keep the rows between them (#330).
    /// </summary>
    /// <remarks>
    ///     ⚠️ This asserted only that every returned row was at or above the minimum, and it passed while the bounds
    ///     filtered with equality: <c>== 4</c> and <c>== 5</c> together returned nothing, and a loop over nothing
    ///     asserts nothing. The rows are now named, the five-star one present and the three-star one absent.
    /// </remarks>
    [Fact]
    public async Task GridSearch_StarRatingRange_FiltersResults()
    {
        var tag = $"GS-R{Guid.NewGuid():N}"[..12];
        await CreatePropertyAsync($"{tag}-Star3", "Rome", 3);
        await CreatePropertyAsync($"{tag}-Star5", "Rome", 5);

        var body = new
        {
            filter = new
            {
                name = tag,
                nameOperator = "StartsWith",
                minStarRating = 4,
                maxStarRating = 5,
                page = 1,
                pageSize = 10
            }
        };

        var response = await PostAsync("/api/properties/grid", body);

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var names = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();

        names.Should().Equal([$"{tag}-Star5"], "four to five stars keeps the five-star property and not the three-star one");
    }

    // =========================================================================
    // Grid Search: response structure validation
    // =========================================================================

    [Fact]
    public async Task GridSearch_ResponseStructure_HasCorrectPagingMetadata()
    {
        // Seed multiple properties
        for (var i = 0; i < 3; i++)
            await CreatePropertyAsync($"GS-Pag{i}-{Guid.NewGuid():N}"[..20], "Berlin", 3);

        var body = new { filter = new { page = 1, pageSize = 2 } };

        var response = await PostAsync("/api/properties/grid", body);

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(2);
        json.GetProperty("items").GetArrayLength().Should().BeLessOrEqualTo(2,
            "Page size 2 should return at most 2 items");
    }

    /// <summary>
    ///     The total is every match, and a later page has its rows (#949).
    /// </summary>
    /// <remarks>
    ///     The generated <c>Apply</c> already pages: an endpoint that counted what it returned and paged it again
    ///     answered a total of one page and an empty second page. The test above, page 1 of rows it does not
    ///     name, could not see either.
    /// </remarks>
    [Fact]
    public async Task GridSearch_MoreMatchesThanOnePage_CountsThemAll_AndServesTheNextPage()
    {
        var tag = $"GS-P{Guid.NewGuid():N}"[..12];
        for (var i = 1; i <= 3; i++)
            await CreatePropertyAsync($"{tag}-{i}", "Turin", 3);

        async Task<JsonElement> Page(int page)
        {
            var response = await PostAsync("/api/properties/grid", new
            {
                filter = new { name = tag, nameOperator = "StartsWith", nameSort = "Ascending", page, pageSize = 2 }
            });
            response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);
            return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        }

        static List<string?> Names(JsonElement page) => [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString())];

        var first = await Page(1);
        var second = await Page(2);

        first.GetProperty("totalCount").GetInt32().Should().Be(3, "three properties match, though a page holds two");
        Names(first).Should().Equal([$"{tag}-1", $"{tag}-2"]);
        second.GetProperty("totalCount").GetInt32().Should().Be(3);
        Names(second).Should().Equal([$"{tag}-3"], "the second page holds the third match");
    }

    /// <summary>
    ///     Each text operator keeps exactly the rows it names, and the total counts them (#946: on every engine).
    /// </summary>
    [Fact]
    public async Task GridSearch_EachTextOperator_KeepsExactlyTheRowsItNames()
    {
        var tag = $"GS-O{Guid.NewGuid():N}"[..12];
        await CreatePropertyAsync($"{tag}-Lake-View", "Como", 3);
        await CreatePropertyAsync($"{tag}-Old-Mill", "Como", 3);
        await CreatePropertyAsync($"{tag}-Lake-Mill", "Como", 3);

        async Task<string> Search(string name, string op)
        {
            var response = await PostAsync("/api/properties/grid", new
            {
                filter = new { search = tag, name, nameOperator = op, nameSort = "Ascending", page = 1, pageSize = 10 }
            });
            response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);
            return Page(await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions));
        }

        (await Search("Lake", "Contains")).Should().Be($"{tag}-Lake-Mill, {tag}-Lake-View of 2");
        (await Search($"{tag}-Old", "StartsWith")).Should().Be($"{tag}-Old-Mill of 1");
        (await Search("Mill", "EndsWith")).Should().Be($"{tag}-Lake-Mill, {tag}-Old-Mill of 2");
    }

    /// <summary>
    ///     Two sorts: by name ascending, then by stars descending within a name (#946: on every engine).
    /// </summary>
    /// <remarks>Two properties share a name, so the second key decides between them: a second sort that was dropped would leave them in either order.</remarks>
    [Fact]
    public async Task GridSearch_TwoSorts_OrderByTheFirstThenTheSecond()
    {
        var tag = $"GS-S{Guid.NewGuid():N}"[..12];
        await CreatePropertyAsync($"{tag}-B", "Pisa", 3);
        await CreatePropertyAsync($"{tag}-A", "Pisa", 2);
        await CreatePropertyAsync($"{tag}-A", "Pisa", 5);

        var response = await PostAsync("/api/properties/grid", new
        {
            filter = new { name = tag, nameOperator = "StartsWith", nameSort = "Ascending", starRatingSort = "Descending", page = 1, pageSize = 10 }
        });

        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var rows = json.GetProperty("items").EnumerateArray().Select(i => (i.GetProperty("name").GetString(), i.GetProperty("starRating").GetInt32())).ToList();
        rows.Should().Equal([($"{tag}-A", 5), ($"{tag}-A", 2), ($"{tag}-B", 3)]);
    }

    /// <summary>
    ///     The entity's filters still apply to a grid search: another tenant's property and a deactivated one are not found, nor counted (#946).
    /// </summary>
    /// <remarks>The control is the same search by the property's own tenant, which finds it: without it, a search that found nothing would pass.</remarks>
    [Fact]
    public async Task GridSearch_ARowTheCallerMayNotSee_IsNeitherReturnedNorCounted()
    {
        var tag = $"GS-V{Guid.NewGuid():N}"[..12];
        using var owner = CreateClientAs("grid-owner", "Grid Owner", "tenant-grid-a");
        using var stranger = CreateClientAs("grid-stranger", "Grid Stranger", "tenant-grid-b");
        var created = await owner.PostAsJsonAsync("/api/properties", new { code = $"GV-{Guid.NewGuid():N}"[..12], name = $"{tag}-Theirs", city = "Bari", country = "IT", starRating = 4 }, JsonOptions);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var deactivated = await owner.PostAsJsonAsync("/api/properties", new { code = $"GV-{Guid.NewGuid():N}"[..12], name = $"{tag}-Closed", city = "Bari", country = "IT", starRating = 4 }, JsonOptions);
        deactivated.StatusCode.Should().Be(HttpStatusCode.Created);
        var closedId = (await deactivated.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
        (await owner.PutAsJsonAsync($"/api/properties/{closedId}", new { id = closedId, isActive = false }, JsonOptions)).IsSuccessStatusCode.Should().BeTrue();

        async Task<string> Search(HttpClient client)
        {
            var response = await client.PostAsJsonAsync("/api/properties/grid", new { filter = new { name = tag, nameOperator = "StartsWith", page = 1, pageSize = 10 } }, JsonOptions);
            response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);
            return Page(await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions));
        }

        (await Search(owner)).Should().Be($"{tag}-Theirs of 1", "its own tenant finds the active property, and not the deactivated one");
        (await Search(stranger)).Should().Be(" of 0", "another tenant finds neither");
    }

    /// <summary>A page as one string: its names in order, then its total.</summary>
    private static string Page(JsonElement page)
        => string.Join(", ", page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString())) + " of " + page.GetProperty("totalCount").GetInt32();

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task CreatePropertyAsync(string name, string city, int starRating)
    {
        var body = new
        {
            code = $"GS-{Guid.NewGuid():N}"[..12],
            name,
            city,
            country = "IT",
            starRating
        };
        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
