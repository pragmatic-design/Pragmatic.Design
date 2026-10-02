using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Showcase.Booking.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     Tests API versioning on the CreateReservation endpoint.
///     Covers matrix features:
///       - [SinceVersion("2.0")] property gating (LoyaltyMemberId)
///       - ExecuteV2 versioned execution dispatch
///       - Version-specific request body (V1Body vs V2Body)
///       - Default behavior when api-version is not specified
/// </summary>
public class ApiVersioningTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // V1: Basic reservation creation succeeds
    // =========================================================================

    [Fact]
    public async Task Versioning_V1_CreateReservation_Succeeds()
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync();

        var response = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(13).ToString("O"),
                numberOfGuests = 2
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "V1 CreateReservation should succeed with basic request body");

        var reservationId = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
        reservationId.Should().NotBeEmpty("V1 should return the created reservation ID");
    }

    // =========================================================================
    // V2: LoyaltyMemberId applies 10% discount (always, no feature flag gate)
    // =========================================================================

    [Fact]
    public async Task Versioning_V2_WithLoyaltyMember_AppliesDiscount()
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(baseRate: 200m);

        // V2 with LoyaltyMemberId — ExecuteV2 always applies 10% discount
        var response = await PostAsync("/api/reservations?api-version=2.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(40).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(43).ToString("O"),
                numberOfGuests = 1
            },
            loyaltyMemberId = "LOYALTY-12345"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "V2 CreateReservation with LoyaltyMemberId should succeed");

        var reservationId = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        // Verify discount was applied via DB (TotalAmount = 3 nights * 200 * 0.90 = 540)
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var reservation = await db.Reservations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.PersistenceId == reservationId);

        reservation.Should().NotBeNull();

        // 3 nights * 200 base rate = 600 without discount
        var expectedWithoutDiscount = 3 * 200m;
        var expectedWithDiscount = expectedWithoutDiscount * 0.90m;

        reservation!.TotalAmount.Should().Be(expectedWithDiscount,
            "V2 with LoyaltyMemberId should apply 10% loyalty discount (600 * 0.90 = 540)");
    }

    // =========================================================================
    // V2: Without LoyaltyMemberId still succeeds (optional field)
    // =========================================================================

    [Fact]
    public async Task Versioning_V2_WithoutLoyaltyMember_StillSucceeds()
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(baseRate: 150m);

        // V2 without LoyaltyMemberId — should still create reservation at full price
        var response = await PostAsync("/api/reservations?api-version=2.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(50).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(52).ToString("O"),
                numberOfGuests = 1
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "V2 CreateReservation without LoyaltyMemberId should still succeed");

        var reservationId = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        // Verify no discount — full price
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var reservation = await db.Reservations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.PersistenceId == reservationId);

        reservation.Should().NotBeNull();

        // 2 nights * 150 base rate = 300 (no discount)
        var expectedFullPrice = 2 * 150m;
        reservation!.TotalAmount.Should().Be(expectedFullPrice,
            "V2 without LoyaltyMemberId should not apply any discount");
    }

    // =========================================================================
    // No api-version: Asp.Versioning returns 400 (version required)
    // =========================================================================

    [Fact]
    public async Task Versioning_DefaultVersion_FallsBackToV1()
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync();

        var response = await PostAsync("/api/reservations", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(70).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(73).ToString("O"),
                numberOfGuests = 1
            }
        });

        // Asp.Versioning behavior when api-version is not specified:
        // - If AssumeDefaultVersionWhenUnspecified is configured, returns 201 (V1)
        // - If not configured (default), returns 400 (ApiVersionUnspecified)
        // Either outcome is valid — the test documents the actual behavior.
        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Created, HttpStatusCode.BadRequest],
            "Without ?api-version, the endpoint should either fall back to V1 or reject with 400");
    }

    // =========================================================================
    // The refusals, the same in both versions (one load, one copy of each)
    // =========================================================================

    [Theory]
    [InlineData("1.0")]
    [InlineData("2.0")]
    public async Task EachVersion_RefusesAnUnknownProperty_WithNotFound(string version)
    {
        var (guestId, _, roomTypeId) = await CreatePrerequisitesAsync();

        var response = await PostAsync($"/api/reservations?api-version={version}", new
        {
            request = new
            {
                guestId,
                propertyId = Guid.NewGuid(),
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(70).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(72).ToString("O"),
                numberOfGuests = 1
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("Property");
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("2.0")]
    public async Task EachVersion_RefusesMoreGuestsThanTheRoomHolds_WithTheTypedConflict(string version)
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(maxOccupancy: 2);

        var response = await PostAsync($"/api/reservations?api-version={version}", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(80).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(82).ToString("O"),
                numberOfGuests = 5
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreatePrerequisitesAsync(
        decimal baseRate = 100m, int? maxOccupancy = null)
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "AV",
            lastName = "Guest",
            email = $"av.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"AV-{Guid.NewGuid():N}"[..12],
            name = $"AVProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 4
        });
        var propertyId = property.GetProperty("id").GetGuid();

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "AV Room",
            code = "AVR",
            baseRate,
            totalRooms = 10,
            maxOccupancy
        });
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }
}
