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
