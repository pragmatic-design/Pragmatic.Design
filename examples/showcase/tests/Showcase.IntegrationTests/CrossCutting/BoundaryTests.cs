using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Booking;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests boundary-level features:
///       - [ReadAccess] — Booking reads Catalog entities (Property, RoomType) via SQL join
///       - Boundary sub-interfaces — IBookingActions.Reservations, IBillingActions
///       - Cross-boundary event-driven communication via boundary facades
/// </summary>
public class BoundaryTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // ReadAccess: Booking can read Catalog entities (same database)
    // =========================================================================

    [Fact]
    public async Task ReadAccess_BookingReservation_IncludesCatalogPropertyName()
    {
        // Booking declares [ReadAccess<Property>] and [ReadAccess<RoomType>]
        // This allows Booking endpoints to Include/Join Catalog entities
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync();

        var reservationResponse = await PostAsync("/api/reservations?api-version=1.0", new
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
        });
        reservationResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = await reservationResponse.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        // GetReservation endpoint eagerly loads Property (via ReadAccess cross-boundary join)
        var reservation = await GetAsync<JsonElement>($"/api/reservations/{reservationId}");

        // ReservationSummaryDto has [MapProperty("Property.Name")] — flattened from Catalog.Property
        reservation.TryGetProperty("propertyName", out var propName).Should().BeTrue(
            "[ReadAccess<Property>] should allow Booking to join and flatten Property.Name");
        propName.GetString().Should().NotBeNullOrEmpty();
    }

    // =========================================================================
    // ReadAccess: Available rooms query crosses Booking → Catalog
    // =========================================================================

    [Fact]
    public async Task ReadAccess_SearchAvailableRooms_ReturnsCatalogRoomTypes()
    {
        var (_, propertyId, _) = await CreatePrerequisitesAsync();

        // SearchAvailableRooms is in Booking but queries Catalog RoomTypes
        var response = await GetRawAsync($"/api/rooms/available?propertyId={propertyId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "[ReadAccess<RoomType>] should allow Booking to query Catalog room types");
    }

    // =========================================================================
    // Boundary sub-interface: cross-boundary mutation via typed facade
    // =========================================================================

    [Fact]
    public async Task BoundaryFacade_InvoicePaid_TriggersBookingPaymentReceived()
    {
        // This tests the full cross-boundary flow:
        // 1. Booking: create reservation + confirm (triggers Billing invoice creation)
        // 2. Billing: mark invoice paid (raises InvoicePaid event)
        // 3. InvoicePaidHandler uses IBookingActions.Reservations.MarkPaymentReceived()
        // 4. Booking reservation transitions to PaymentReceived
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync();

        // Create and confirm reservation
        var createResponse = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(14).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(17).ToString("O"),
                numberOfGuests = 1
            }
        });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = await createResponse.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        // Confirm → triggers ReservationConfirmed → creates Invoice in Billing
        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.EnsureSuccessStatusCode();

        // Find the invoice created by ReservationConfirmedHandler
        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "ReservationConfirmedHandler should create an invoice via IBillingActions");
        var invoiceId = items[0].GetProperty("id").GetGuid();

        // Pay the invoice → raises InvoicePaid → InvoicePaidHandler calls
        // IBookingActions.Reservations.MarkPaymentReceived (boundary sub-interface)
        var payResponse = await PostAsync($"/api/invoices/{invoiceId}/pay", new { });
        payResponse.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent]);

        // Verify reservation transitioned to PaymentReceived via boundary facade
        var reservation = await GetAsync<JsonElement>($"/api/reservations/{reservationId}");
        reservation.GetProperty("status").GetString().Should().Be("PaymentReceived",
            "IBookingActions.Reservations.MarkPaymentReceived should transition reservation status");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreatePrerequisitesAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Boundary",
            lastName = "Test",
            email = $"boundary.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"BT-{Guid.NewGuid():N}"[..12],
            name = $"BoundaryProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 4
        });
        var propertyId = property.GetProperty("id").GetGuid();

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Boundary Room",
            code = "BDR",
            baseRate = 150m,
            totalRooms = 3
        });
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }
}
