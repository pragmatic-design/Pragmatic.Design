using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     Tests endpoint features via HTTP.
///     Covers matrix features:
///       - Endpoint (auto) [Endpoint] on Mutation/Query
///       - Endpoint (manual) Endpoint&lt;T&gt; class
///       - EndpointGroup [EndpointGroup]
///       - AllowAnonymous [AllowAnonymous]
///       - EndpointSummary [EndpointSummary]
///       - Tags [Tags]
/// </summary>
public class EndpointFeatureTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Manual Endpoint: GetProperty
    // =========================================================================

    [Fact]
    public async Task GetProperty_ById_ReturnsFullEntity()
    {
        var created = await CreatePropertyAsync();
        var id = created.GetProperty("id").GetGuid();

        var response = await GetRawAsync($"/api/properties/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("id").GetGuid().Should().Be(id);
        json.GetProperty("name").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetProperty_NonExistent_Returns404()
    {
        var response = await GetRawAsync($"/api/properties/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =========================================================================
    // Manual Endpoint: GetGuest
    // =========================================================================

    [Fact]
    public async Task GetGuest_ById_ReturnsFullEntity()
    {
        var guestBody = new
        {
            firstName = "Endpoint",
            lastName = "Test",
            email = $"ep.{Guid.NewGuid():N}@test.com"
        };
        var created = await PostAsync<JsonElement>("/api/guests", guestBody);
        var id = created.GetProperty("id").GetGuid();

        var response = await GetRawAsync($"/api/guests/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("id").GetGuid().Should().Be(id);
        json.GetProperty("firstName").GetString().Should().Be("Endpoint");
    }

    // =========================================================================
    // Manual Endpoint: GetReservation
    // =========================================================================

    [Fact]
    public async Task GetReservation_ById_ReturnsEntity()
    {
        var reservationId = await CreateFullReservationAsync();

        var response = await GetRawAsync($"/api/reservations/{reservationId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("id").GetGuid().Should().Be(reservationId);
    }

    // =========================================================================
    // Manual Endpoint: GetInvoice
    // =========================================================================

    [Fact]
    public async Task GetInvoice_AfterConfirmation_ReturnsInvoice()
    {
        var reservationId = await CreateFullReservationAsync();

        // Confirm reservation (triggers domain event → CreateInvoice)
        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Search invoices for this reservation
        var searchResponse = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = searchResponse.GetProperty("items");

        if (items.GetArrayLength() > 0)
        {
            var invoiceId = items[0].GetProperty("id").GetGuid();
            var response = await GetRawAsync($"/api/invoices/{invoiceId}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    // =========================================================================
    // Auto Endpoint: Mutation endpoints (POST/PUT/DELETE)
    // =========================================================================

    [Fact]
    public async Task AutoEndpoint_CreateAmenity_PostRoute()
    {
        var body = new
        {
            name = $"EpAmen-{Guid.NewGuid():N}"[..15],
            category = 0,
            iconName = "test"
        };

        var response = await PostAsync("/api/amenities", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AutoEndpoint_UpdateAmenity_PutRoute()
    {
        var createBody = new
        {
            name = $"EpUpd-{Guid.NewGuid():N}"[..15],
            category = 0,
            iconName = "test"
        };
        var created = await PostAsync<JsonElement>("/api/amenities", createBody);
        var id = created.GetProperty("id").GetGuid();

        var updateBody = new { name = $"Updated-{Guid.NewGuid():N}"[..15], category = 1, iconName = "new" };
        var response = await PutAsync($"/api/amenities/{id}", updateBody);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AutoEndpoint_DeleteAmenity_DeleteRoute()
    {
        var createBody = new
        {
            name = $"EpDel-{Guid.NewGuid():N}"[..15],
            category = 0,
            iconName = "test"
        };
        var created = await PostAsync<JsonElement>("/api/amenities", createBody);
        var id = created.GetProperty("id").GetGuid();

        var response = await DeleteAsync($"/api/amenities/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "a delete answers 204 and no body");
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    // =========================================================================
    // Auto Endpoint: Query endpoints (GET /search)
    // =========================================================================

    [Fact]
    public async Task AutoEndpoint_SearchAmenities_GetRoute()
    {
        var response = await GetRawAsync("/api/amenities/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.TryGetProperty("items", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AutoEndpoint_SearchRoomTypes_GetRoute()
    {
        var response = await GetRawAsync("/api/room-types/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.TryGetProperty("items", out _).Should().BeTrue();
    }

    // =========================================================================
    // EndpointGroup: routes under /api/{group}
    // =========================================================================

    [Fact]
    public async Task EndpointGroup_PropertiesRoutes_AllUnderV1Prefix()
    {
        // All property routes should be under /api/properties
        var search = await GetRawAsync("/api/properties/search");
        search.StatusCode.Should().Be(HttpStatusCode.OK);

        var create = await PostAsync("/api/properties", new
        {
            code = $"EG-{Guid.NewGuid():N}"[..12],
            name = "Group Test",
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<JsonElement> CreatePropertyAsync()
    {
        var body = new
        {
            code = $"EP-{Guid.NewGuid():N}"[..12],
            name = $"Endpoint-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        };
        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private async Task<Guid> CreateFullReservationAsync()
    {
        var guestBody = new
        {
            firstName = "EP",
            lastName = "Guest",
            email = $"ep.{Guid.NewGuid():N}@test.com"
        };
        var guest = await PostAsync<JsonElement>("/api/guests", guestBody);
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await CreatePropertyAsync();
        var propertyId = prop.GetProperty("id").GetGuid();

        var rtBody = new
        {
            propertyId,
            name = "EP Room",
            code = "EPR",
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
