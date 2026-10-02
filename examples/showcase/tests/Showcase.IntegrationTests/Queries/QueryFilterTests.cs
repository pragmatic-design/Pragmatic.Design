using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Queries;

/// <summary>
///     Tests query features via HTTP endpoints.
///     Covers matrix features:
///       - Paged Query [Query&lt;T,R&gt;]
///       - Filter [Filter], [Filter(Contains)], [Filter(GreaterOrEqual)]
///       - Complex Filter [ComplexFilter]
///       - Sort [Sort]
///       - Projection (DTO) [GenerateProjection]
/// </summary>
public class QueryFilterTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Pagination
    // =========================================================================

    [Fact]
    public async Task SearchProperties_DefaultPagination_ReturnsPagedResult()
    {
        // Seed 3 properties
        for (var i = 0; i < 3; i++)
            await CreatePropertyAsync($"PgProp{i}");

        var response = await GetRawAsync("/api/properties/search?pageSize=2&page=1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        // Paged result structure
        json.TryGetProperty("items", out _).Should().BeTrue();
        json.TryGetProperty("totalCount", out _).Should().BeTrue();
        json.TryGetProperty("page", out _).Should().BeTrue();
        json.TryGetProperty("pageSize", out _).Should().BeTrue();

        json.GetProperty("items").GetArrayLength().Should().BeLessOrEqualTo(2);
        json.GetProperty("page").GetInt32().Should().Be(1);
        json.GetProperty("pageSize").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task SearchProperties_Page2_ReturnsDifferentResults()
    {
        // Seed enough properties
        for (var i = 0; i < 4; i++)
            await CreatePropertyAsync($"Pg2Prop{i}");

        var page1 = await GetAsync<JsonElement>("/api/properties/search?pageSize=2&page=1");
        var page2 = await GetAsync<JsonElement>("/api/properties/search?pageSize=2&page=2");

        var page1Ids = page1.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();
        var page2Ids = page2.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid()).ToList();

        // Pages should not overlap
        page1Ids.Intersect(page2Ids).Should().BeEmpty();
    }

    // =========================================================================
    // Filter: Contains
    // SG-generated handler binds only page/pageSize — filter params not yet wired.
    // These tests verify the endpoint accepts the query params without error.
    // =========================================================================

    [Fact]
    public async Task SearchProperties_FilterByNameContains_ReturnsMatching()
    {
        var uniqueTag = Guid.NewGuid().ToString("N")[..8];
        await CreatePropertyAsync($"Luxury{uniqueTag}");
        await CreatePropertyAsync($"Budget{Guid.NewGuid():N}"[..15]);

        var response = await GetAsync<JsonElement>($"/api/properties/search?name={uniqueTag}");
        var items = response.GetProperty("items");

        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);
        items.EnumerateArray().All(x =>
            x.GetProperty("name").GetString()!.Contains(uniqueTag, StringComparison.OrdinalIgnoreCase)
        ).Should().BeTrue();
    }

    [Fact]
    public async Task SearchGuests_FilterByEmailContains_ReturnsMatching()
    {
        var uniqueTag = Guid.NewGuid().ToString("N")[..8];
        var email = $"qf.{uniqueTag}@test.com";
        await CreateGuestAsync("Query", "Filter", email);
        await CreateGuestAsync("Other", "Guest", $"other.{Guid.NewGuid():N}@test.com");

        var response = await GetAsync<JsonElement>($"/api/guests/search?email={uniqueTag}");
        var items = response.GetProperty("items");

        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);
        items.EnumerateArray().All(x =>
            x.GetProperty("email").GetString()!.Contains(uniqueTag, StringComparison.OrdinalIgnoreCase)
        ).Should().BeTrue();
    }

    // =========================================================================
    // Filter: GreaterOrEqual
    // =========================================================================

    [Fact]
    public async Task SearchProperties_FilterByMinStarRating_ReturnsOnlyHighRated()
    {
        await CreatePropertyAsync("LowStar", starRating: 1);
        await CreatePropertyAsync("HighStar", starRating: 5);

        var response = await GetAsync<JsonElement>("/api/properties/search?minStarRating=4");
        var items = response.GetProperty("items");

        items.EnumerateArray().All(x =>
            x.GetProperty("starRating").GetInt32() >= 4
        ).Should().BeTrue();
    }

    // =========================================================================
    // Filter: Exact match
    // =========================================================================

    [Fact]
    public async Task SearchProperties_FilterByCity_ReturnsOnlyMatchingCity()
    {
        var uniqueCity = $"City{Guid.NewGuid():N}"[..12];
        await CreatePropertyAsync("CityProp", city: uniqueCity);
        await CreatePropertyAsync("OtherProp", city: "OtherCity");

        var response = await GetAsync<JsonElement>($"/api/properties/search?city={uniqueCity}");
        var items = response.GetProperty("items");

        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);
        items.EnumerateArray().All(x =>
            x.GetProperty("city").GetString() == uniqueCity
        ).Should().BeTrue();
    }

    // =========================================================================
    // Filter: RoomTypes with LessOrEqual
    // =========================================================================

    [Fact]
    public async Task SearchRoomTypes_FilterByMaxBaseRate_ReturnsOnlyCheaper()
    {
        var propertyId = await CreatePropertyAndGetIdAsync();

        await CreateRoomTypeAsync(propertyId, "Cheap Room", 50m);
        await CreateRoomTypeAsync(propertyId, "Expensive Room", 500m);

        var response = await GetAsync<JsonElement>(
            $"/api/room-types/search?propertyId={propertyId}&maxBaseRate=100");
        var items = response.GetProperty("items");

        items.EnumerateArray().All(x =>
            x.GetProperty("baseRate").GetDecimal() <= 100m
        ).Should().BeTrue();
    }

    // =========================================================================
    // Sort
    // =========================================================================

    [Fact]
    public async Task SearchRoomTypes_SortByBaseRateAsc_ReturnsOrdered()
    {
        var propertyId = await CreatePropertyAndGetIdAsync();

        await CreateRoomTypeAsync(propertyId, "Mid Room", 200m);
        await CreateRoomTypeAsync(propertyId, "Low Room", 50m);
        await CreateRoomTypeAsync(propertyId, "High Room", 400m);

        var response = await GetAsync<JsonElement>(
            $"/api/room-types/search?propertyId={propertyId}&baseRateSort=0");
        var items = response.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("baseRate").GetDecimal()).ToList();

        items.Should().BeInAscendingOrder();
    }

    // =========================================================================
    // Projection (DTO)
    // =========================================================================

    [Fact]
    public async Task SearchReservations_ReturnsProjectedDto()
    {
        var reservationId = await CreateFullReservationAsync();

        var response = await GetAsync<JsonElement>(
            $"/api/reservations/search?page=1&pageSize=50");
        var items = response.GetProperty("items");

        // Find our reservation
        var reservation = items.EnumerateArray()
            .FirstOrDefault(x => x.GetProperty("id").GetGuid() == reservationId);

        // ReservationSummaryDto should have projected fields
        reservation.GetProperty("status").GetString().Should().Be("Pending");
        reservation.TryGetProperty("checkIn", out _).Should().BeTrue();
        reservation.TryGetProperty("checkOut", out _).Should().BeTrue();
    }

    // =========================================================================
    // ComplexFilter: nested JSON filter with FilterGroup
    // =========================================================================

    [Fact]
    public async Task SearchProperties_ComplexFilter_LocationCityGroup_FiltersResults()
    {
        var uniqueCity = $"Complex{Guid.NewGuid():N}"[..12];
        await CreatePropertyAsync("ComplexProp", city: uniqueCity, starRating: 3);
        await CreatePropertyAsync("OtherProp", city: "Nowhere", starRating: 5);

        // [ComplexFilter] PropertyLocationFilter — sent as JSON query string
        // CityGroup uses OR logic: match city OR country
        var locationJson = System.Net.WebUtility.UrlEncode(
            JsonSerializer.Serialize(new { cityGroup = new { city = uniqueCity } }, JsonOptions));

        var response = await GetAsync<JsonElement>(
            $"/api/properties/search?location={locationJson}");
        var items = response.GetProperty("items");

        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);
        items.EnumerateArray().All(x =>
            x.GetProperty("city").GetString()!.Contains(uniqueCity, StringComparison.OrdinalIgnoreCase)
        ).Should().BeTrue("ComplexFilter CityGroup should filter by city");
    }

    [Fact]
    public async Task SearchProperties_ComplexFilter_MaxStarRating_FiltersResults()
    {
        await CreatePropertyAsync("LowStarComplex", starRating: 2);
        await CreatePropertyAsync("HighStarComplex", starRating: 5);

        var locationJson = System.Net.WebUtility.UrlEncode(
            JsonSerializer.Serialize(new { maxStarRating = 3 }, JsonOptions));

        var response = await GetAsync<JsonElement>(
            $"/api/properties/search?location={locationJson}");
        var items = response.GetProperty("items");

        items.EnumerateArray().All(x =>
            x.GetProperty("starRating").GetInt32() <= 3
        ).Should().BeTrue("ComplexFilter MaxStarRating should cap results at 3 stars");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<JsonElement> CreatePropertyAsync(string namePrefix, int starRating = 3, string city = "Rome")
    {
        var body = new
        {
            code = $"QF-{Guid.NewGuid():N}"[..12],
            name = $"{namePrefix}-{Guid.NewGuid():N}"[..20],
            city,
            country = "IT",
            starRating
        };
        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<Guid> CreatePropertyAndGetIdAsync()
    {
        var prop = await CreatePropertyAsync("QFProp");
        return prop.GetProperty("id").GetGuid();
    }

    private async Task CreateGuestAsync(string firstName, string lastName, string email)
    {
        var body = new { firstName, lastName, email };
        var response = await PostAsync("/api/guests", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task CreateRoomTypeAsync(Guid propertyId, string name, decimal baseRate)
    {
        var body = new
        {
            propertyId,
            name,
            code = $"RT{Guid.NewGuid():N}"[..4],
            baseRate,
            totalRooms = 5
        };
        var response = await PostAsync("/api/room-types", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<Guid> CreateFullReservationAsync()
    {
        var guestBody = new
        {
            firstName = "QF",
            lastName = "Guest",
            email = $"qf.{Guid.NewGuid():N}@test.com"
        };
        var guest = await PostAsync<JsonElement>("/api/guests", guestBody);
        var guestId = guest.GetProperty("id").GetGuid();

        var propertyId = await CreatePropertyAndGetIdAsync();

        var rtBody = new
        {
            propertyId,
            name = "QF Room",
            code = "QFR",
            baseRate = 100m,
            totalRooms = 5
        };
        var roomType = await PostAsync<JsonElement>("/api/room-types", rtBody);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        var reservationBody = new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                numberOfGuests = 2
            }
        };
        var response = await PostAsync("/api/reservations?api-version=1.0", reservationBody);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
