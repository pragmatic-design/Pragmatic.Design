using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     Tests state machine transitions via HTTP endpoints.
///     Covers matrix features:
///       - State Machine [StateMachine&lt;TEnum&gt;]
///       - Domain Action [DomainAction]
///       - Domain Events (ReservationConfirmed → CreateInvoice)
///       - Composite Action [CompositeAction] (tested indirectly)
/// </summary>
public class StateMachineTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Reservation_PendingToConfirmed_ValidTransition()
    {
        var reservationId = await CreateFullReservationAsync();

        // Confirm: Pending → Confirmed
        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });

        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var confirmed = await GetAsync<JsonElement>($"/api/reservations/{reservationId}");
        confirmed.GetProperty("status").GetString().Should().Be("Confirmed");
    }

    [Fact]
    public async Task Reservation_ConfirmedToPaymentReceived_ValidTransition()
    {
        var reservationId = await CreateFullReservationAsync();

        // Pending → Confirmed
        await PostAsync($"/api/reservations/{reservationId}/confirm", new { });

        // Confirmed → PaymentReceived
        var response = await PostAsync($"/api/reservations/{reservationId}/payment-received", new { });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var json = await GetAsync<JsonElement>($"/api/reservations/{reservationId}");
        json.GetProperty("status").GetString().Should().Be("PaymentReceived");
    }

    [Fact]
    public async Task Reservation_ConfirmedToCheckedIn_ValidTransition()
    {
        // Use premium-hotel tenant — early check-in flag is enabled for this tenant,
        // allowing check-in before the scheduled date (reservation is 7 days ahead).
        using var premiumClient = CreateClientAs("test-user", "Integration Test", "premium-hotel");
        var reservationId = await CreateFullReservationAsync(premiumClient);

        // Pending → Confirmed
        await PostWithClientAsync(premiumClient, $"/api/reservations/{reservationId}/confirm", new { });

        // Confirmed → CheckedIn
        var response = await PostWithClientAsync(premiumClient, $"/api/reservations/{reservationId}/check-in",
            new { roomNumber = "204" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var json = await GetWithClientAsync<JsonElement>(premiumClient, $"/api/reservations/{reservationId}");
        json.GetProperty("status").GetString().Should().Be("CheckedIn");
    }

    [Fact]
    public async Task Reservation_Cancel_FromPending_ValidTransition()
    {
        var reservationId = await CreateFullReservationAsync();

        // Pending → Cancelled
        var response = await PostAsync($"/api/reservations/{reservationId}/cancel",
            new { reason = "Changed plans" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var json = await GetAsync<JsonElement>($"/api/reservations/{reservationId}");
        json.GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Reservation_CancelAfterConfirm_ValidTransition()
    {
        var reservationId = await CreateFullReservationAsync();

        await PostAsync($"/api/reservations/{reservationId}/confirm", new { });

        var response = await PostAsync($"/api/reservations/{reservationId}/cancel",
            new { reason = "Emergency" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var json = await GetAsync<JsonElement>($"/api/reservations/{reservationId}");
        json.GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Reservation_InvalidTransition_CheckedInToPending_Returns409()
    {
        // Use premium-hotel tenant for early check-in support
        using var premiumClient = CreateClientAs("test-user", "Integration Test", "premium-hotel");
        var reservationId = await CreateFullReservationAsync(premiumClient);

        // Pending → Confirmed → CheckedIn (status-only due to circular ref in check-in response)
        await PostWithClientAsync(premiumClient, $"/api/reservations/{reservationId}/confirm", new { });
        await PostStatusOnlyWithClientAsync(premiumClient, $"/api/reservations/{reservationId}/check-in", new { });

        // CheckedIn → Confirmed (invalid)
        var response = await PostWithClientAsync(premiumClient, $"/api/reservations/{reservationId}/confirm", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private Task<Guid> CreateFullReservationAsync() => CreateFullReservationAsync(Client);

    private static async Task<Guid> CreateFullReservationAsync(HttpClient client)
    {
        // Create guest
        var guestBody = new
        {
            firstName = "Test",
            lastName = "Guest",
            email = $"state.{Guid.NewGuid():N}@test.com"
        };
        var guestResponse = await client.PostAsJsonAsync("/api/guests", guestBody, JsonOptions);
        var guest = await guestResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var guestId = guest.GetProperty("id").GetGuid();

        // Create property
        var propertyBody = new
        {
            code = $"SM-{Guid.NewGuid():N}".Substring(0, 12),
            name = "State Machine Hotel",
            city = "Rome",
            country = "IT",
            starRating = 4
        };
        var propertyResponse = await client.PostAsJsonAsync("/api/properties", propertyBody, JsonOptions);
        var property = await propertyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = property.GetProperty("id").GetGuid();

        // Create room type
        var roomTypeBody = new
        {
            propertyId,
            name = "Standard Room",
            code = "STD",
            baseRate = 100.00m,
            totalRooms = 5
        };
        var roomTypeResponse = await client.PostAsJsonAsync("/api/room-types", roomTypeBody, JsonOptions);
        var roomType = await roomTypeResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        // Create reservation (DomainAction: body requires "request" wrapper)
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
        // DomainAction with API versioning requires explicit version
        var reservationResponse = await client.PostAsJsonAsync("/api/reservations?api-version=1.0", reservationBody, JsonOptions);
        reservationResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var reservationId = await reservationResponse.Content.ReadFromJsonAsync<Guid>(JsonOptions);
        return reservationId;
    }
}
