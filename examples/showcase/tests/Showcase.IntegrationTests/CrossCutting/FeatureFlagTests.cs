using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests feature flag behavior in the Showcase application.
///     Covers matrix features:
///       - Feature Flags IFeatureFlag, IFeatureFlagStore
///       - Tenant Targeting (EarlyCheckInFlag enabled for "premium-hotel")
///       - Feature-gated domain logic (check-in rejection)
/// </summary>
public class FeatureFlagTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // EarlyCheckIn: premium-hotel tenant — early check-in allowed
    // =========================================================================

    [Fact]
    public async Task CheckIn_PremiumHotelTenant_EarlyCheckInAllowed()
    {
        // "premium-hotel" tenant has EarlyCheckInFlag enabled via tenant targeting rule
        using var premiumClient = CreateClientAs("ff-user", "FF Test", "premium-hotel");

        var reservationId = await CreateAndPrepareForCheckInAsync(premiumClient);

        // Check-in before scheduled date — premium tenant should be allowed
        var checkInResponse = await PostWithClientAsync(premiumClient,
            $"/api/reservations/{reservationId}/check-in", new { });

        // Should succeed — early check-in allowed for premium-hotel
        checkInResponse.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "EarlyCheckInFlag targets 'premium-hotel' tenant, so early check-in should be allowed");
    }

    // =========================================================================
    // EarlyCheckIn: regular tenant — early check-in blocked
    // =========================================================================

    [Fact]
    public async Task CheckIn_RegularTenant_EarlyCheckInBlocked()
    {
        // "regular-tenant" is not in the EarlyCheckInFlag targeting list
        using var regularClient = CreateClientAs("ff-user", "FF Test", "regular-tenant");

        var reservationId = await CreateAndPrepareForCheckInAsync(regularClient);

        // Check-in before scheduled date — regular tenant should be blocked
        var checkInResponse = await PostWithClientAsync(regularClient,
            $"/api/reservations/{reservationId}/check-in", new { });

        // Should return Conflict (409) — early check-in not allowed
        checkInResponse.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "Regular tenant does not have EarlyCheckInFlag enabled, so early check-in should be rejected");
    }

    // =========================================================================
    // LoyaltyDiscount: flag affects pricing in CreateReservation (V1)
    // =========================================================================

    [Fact]
    public async Task CreateReservation_V2_WithLoyaltyMemberId_FlagIsEvaluated()
    {
        // The loyalty discount flag uses percentage rollout (30%).
        // We cannot deterministically test the discount, but we can verify
        // that the reservation is created successfully regardless of the flag outcome.
        // LoyaltyMemberId is [SinceVersion("2.0")], so we must use the V2 endpoint.
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(Client);

        var response = await PostAsync("/api/reservations?api-version=2.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(14).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(17).ToString("O"),
                numberOfGuests = 1
            },
            loyaltyMemberId = "LOYALTY-12345"
        });

        // Reservation should be created regardless of flag evaluation result
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "Reservation creation should succeed whether loyalty discount flag is on or off");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreatePrerequisitesAsync(HttpClient client)
    {
        var guestResponse = await client.PostAsJsonAsync("/api/guests", new
        {
            firstName = "FF",
            lastName = "Guest",
            email = $"ff.{Guid.NewGuid():N}@test.com"
        }, JsonOptions);
        guestResponse.EnsureSuccessStatusCode();
        var guest = await guestResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var guestId = guest.GetProperty("id").GetGuid();

        var propertyResponse = await client.PostAsJsonAsync("/api/properties", new
        {
            code = $"FF-{Guid.NewGuid():N}"[..12],
            name = $"FFProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        }, JsonOptions);
        propertyResponse.EnsureSuccessStatusCode();
        var property = await propertyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = property.GetProperty("id").GetGuid();

        var roomTypeResponse = await client.PostAsJsonAsync("/api/room-types", new
        {
            propertyId,
            name = "FF Room",
            code = "FFR",
            baseRate = 200m,
            totalRooms = 5
        }, JsonOptions);
        roomTypeResponse.EnsureSuccessStatusCode();
        var roomType = await roomTypeResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }

    /// <summary>
    /// Creates a reservation with a future check-in date and moves it through
    /// Pending → Confirmed → PaymentReceived to prepare for check-in.
    /// </summary>
    private static async Task<Guid> CreateAndPrepareForCheckInAsync(HttpClient client)
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(client);

        // Create reservation with check-in far in the future (triggers early check-in logic)
        var reservationResponse = await client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 2
            }
        }, JsonOptions);
        reservationResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = await reservationResponse.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        // Pending → Confirmed
        var confirmResponse = await client.PostAsJsonAsync(
            $"/api/reservations/{reservationId}/confirm", new { }, JsonOptions);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirmed → PaymentReceived
        var paymentResponse = await client.PostAsJsonAsync(
            $"/api/reservations/{reservationId}/payment-received", new { }, JsonOptions);
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return reservationId;
    }
}
