using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     Tests endpoint-level features.
///     Covers matrix features:
///       - AllowAnonymous [AllowAnonymous]
///       - Specification Specification&lt;T&gt;
///       - PostProcessor [PostProcessor]
/// </summary>
public class AllowAnonymousTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // AllowAnonymous + Specification: /api/availability
    // =========================================================================

    [Fact]
    public async Task SearchAvailableRooms_ReturnsOk()
    {
        // Create property + room type
        var property = await CreatePropertyAsync();
        var propertyId = property.GetProperty("id").GetGuid();

        var rtBody = new
        {
            propertyId,
            name = "Anon Room",
            code = "ANR",
            baseRate = 150m,
            totalRooms = 3,
            maxOccupancy = 2
        };
        await PostAsync<JsonElement>("/api/room-types", rtBody);

        var checkIn = DateTimeOffset.UtcNow.AddDays(14).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var checkOut = DateTimeOffset.UtcNow.AddDays(17).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var url = $"/api/availability?propertyId={propertyId}&checkIn={checkIn}&checkOut={checkOut}&guests=1";

        var response = await GetRawAsync(url);

        // [AllowAnonymous] endpoint should respond OK
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "/api/availability is [AllowAnonymous] and should be accessible");
    }

    [Fact]
    public async Task SearchAvailableRooms_WithProperty_ReturnsAvailableRooms()
    {
        var property = await CreatePropertyAsync();
        var propertyId = property.GetProperty("id").GetGuid();

        var rtBody = new
        {
            propertyId,
            name = "Spec Room",
            code = "SPR",
            baseRate = 200m,
            totalRooms = 5,
            maxOccupancy = 3
        };
        await PostAsync<JsonElement>("/api/room-types", rtBody);

        var checkIn = DateTimeOffset.UtcNow.AddDays(30).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var checkOut = DateTimeOffset.UtcNow.AddDays(33).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var url = $"/api/availability?propertyId={propertyId}&checkIn={checkIn}&checkOut={checkOut}&guests=1";

        var response = await GetRawAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rooms = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        rooms.GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "Should find available rooms (Specification filters by property + occupancy)");

        var firstRoom = rooms[0];
        firstRoom.GetProperty("roomTypeName").GetString().Should().Be("Spec Room");
        firstRoom.GetProperty("availableRooms").GetInt32().Should().Be(5,
            "All rooms should be available (no bookings in that date range)");
    }

    [Fact]
    public async Task SearchAvailableRooms_FullyBooked_ReturnsEmptyArray()
    {
        var property = await CreatePropertyAsync();
        var propertyId = property.GetProperty("id").GetGuid();

        // Create room type with only 1 room
        var rtBody = new
        {
            propertyId,
            name = "Tiny Room",
            code = "TNY",
            baseRate = 100m,
            totalRooms = 1,
            maxOccupancy = 2
        };
        var roomType = await PostAsync<JsonElement>("/api/room-types", rtBody);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        // Book the only room
        var guestBody = new
        {
            firstName = "Booked",
            lastName = "Out",
            email = $"booked.{Guid.NewGuid():N}@test.com"
        };
        var guest = await PostAsync<JsonElement>("/api/guests", guestBody);
        var guestId = guest.GetProperty("id").GetGuid();

        var checkIn = DateTimeOffset.UtcNow.AddDays(50);
        var checkOut = DateTimeOffset.UtcNow.AddDays(53);

        var reservationBody = new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = checkIn.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                checkOut = checkOut.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                numberOfGuests = 1
            }
        };
        var resResponse = await PostAsync("/api/reservations?api-version=1.0", reservationBody);
        resResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Search for availability in same date range — should be empty
        var url = $"/api/availability?propertyId={propertyId}&checkIn={checkIn.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}&checkOut={checkOut.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}&guests=1";
        var response = await GetRawAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rooms = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        rooms.GetArrayLength().Should().Be(0,
            "No rooms available when all are booked (Specification Overlapping works)");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<JsonElement> CreatePropertyAsync()
    {
        var body = new
        {
            code = $"AA-{Guid.NewGuid():N}"[..12],
            name = $"AnonProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        };
        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }
}
