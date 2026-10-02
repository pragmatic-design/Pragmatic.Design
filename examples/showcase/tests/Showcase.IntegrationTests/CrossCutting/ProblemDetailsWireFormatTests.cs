using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Asserts the RFC 7807 wire format of a Result failure — not just the status code. Proves the
///     end-to-end chain: a custom typed <c>Error</c> (RoomUnavailableError) → source-generated
///     <c>WriteExtensions</c> → RFC 7807 ProblemDetails body (type/title/status/code + extensions).
///     RoomUnavailableError is a <c>sealed partial record : Error</c> with four context properties;
///     because it is <c>partial</c>, the generator emits the WriteExtensions override that projects
///     those properties into ProblemDetails extensions (camelCase) on the wire.
/// </summary>
public class ProblemDetailsWireFormatTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateReservation_ExceedsMaxOccupancy_ReturnsProblemDetailsWithCustomExtensions()
    {
        // RoomType default MaxOccupancy is 2; request 5 guests (within the DTO [Range(1,20)] but over
        // occupancy) so the action returns the typed RoomUnavailableError (409).
        var (guestId, propertyId, roomTypeId) = await CreateReservationPrerequisitesAsync();

        var checkIn = DateTimeOffset.UtcNow.AddDays(30);
        var checkOut = DateTimeOffset.UtcNow.AddDays(33);

        var response = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = checkIn.ToString("O"),
                checkOut = checkOut.ToString("O"),
                numberOfGuests = 5
            }
        });

        // Status
        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "exceeding room occupancy returns the typed RoomUnavailableError (409)");

        // Content-Type: every error answers the RFC 9457 media type, the one the OpenAPI document
        // declares for error responses.
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        // Body — RFC 7807 fields + extensions
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        body.GetProperty("status").GetInt32().Should().Be(409);
        body.GetProperty("title").GetString().Should().Be("Room Unavailable");
        body.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().NotBeNullOrEmpty("RFC 7807 requires a 'type' URI");

        // The 'code' extension carries the machine-readable error code.
        body.GetProperty("code").GetString().Should().Be("ROOM_UNAVAILABLE");

        // Custom properties from the source-generated WriteExtensions (camelCase keys).
        body.GetProperty("propertyId").GetGuid().Should().Be(propertyId);
        body.GetProperty("roomTypeId").GetGuid().Should().Be(roomTypeId);
        body.TryGetProperty("checkIn", out _).Should().BeTrue("WriteExtensions writes the checkIn context property");
        body.TryGetProperty("checkOut", out _).Should().BeTrue("WriteExtensions writes the checkOut context property");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<Guid> CreatePropertyAndGetIdAsync()
    {
        var body = new
        {
            code = $"PD-{Guid.NewGuid():N}"[..12],
            name = $"PDProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        };
        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("id").GetGuid();
    }

    private async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreateReservationPrerequisitesAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "PD",
            lastName = "Guest",
            email = $"pd.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var propertyId = await CreatePropertyAndGetIdAsync();

        // Do NOT set maxOccupancy → defaults to 2. totalRooms 5 so the overlap validator passes.
        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "PD Room",
            code = "PDR",
            baseRate = 100m,
            totalRooms = 5
        });
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }
}
