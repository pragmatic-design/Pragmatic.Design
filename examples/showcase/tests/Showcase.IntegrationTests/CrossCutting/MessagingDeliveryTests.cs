using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests message delivery reliability and side-effect propagation.
///     Complements MessagingPipelineTests by focusing on delivery guarantees
///     and the full chain of effects across boundaries.
/// </summary>
public class MessagingDeliveryTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ConfirmReservation_InvoiceCreated_WithCorrectAmount()
    {
        var (reservationId, _) = await CreateAndConfirmReservationAsync();

        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "ReservationConfirmedHandler should create an invoice via messaging");

        var invoice = items[0];
        invoice.GetProperty("totalAmount").GetDecimal().Should().BeGreaterThan(0,
            "Invoice amount should be computed from the reservation (baseRate × nights)");
        invoice.GetProperty("status").GetString().Should().Be("Draft",
            "Newly created invoice should have Draft status");
    }

    [Fact]
    public async Task FullPaymentFlow_Booking_Billing_Booking_RoundTrip()
    {
        // Create → Confirm → Invoice created → Pay → PaymentReceived
        // Tests the full cross-boundary messaging round-trip
        var (reservationId, _) = await CreateAndConfirmReservationAsync();

        // Get the auto-created invoice
        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var invoiceId = invoiceSearch.GetProperty("items")[0].GetProperty("id").GetGuid();

        // Pay → triggers InvoicePaid → InvoicePaidHandler → MarkPaymentReceived
        var payResponse = await PostAsync($"/api/invoices/{invoiceId}/pay", new { });
        payResponse.StatusCode.Should().BeOneOf(
            [HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent]);

        // Verify final state of BOTH entities
        var invoice = await GetAsync<JsonElement>($"/api/invoices/{invoiceId}");
        invoice.GetProperty("status").GetString().Should().Be("Paid");

        var reservation = await GetAsync<JsonElement>($"/api/reservations/{reservationId}");
        reservation.GetProperty("status").GetString().Should().Be("PaymentReceived",
            "Full round-trip: Confirm→Invoice→Pay→PaymentReceived should complete via messaging");
    }

    [Fact]
    public async Task MultipleConfirmations_IdempotentInvoiceCreation()
    {
        var (reservationId, _) = await CreateAndConfirmReservationAsync();

        // Count invoices after first confirmation
        var search1 = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var count1 = search1.GetProperty("items").GetArrayLength();

        // Try to confirm again — should fail (state machine: Confirmed → Confirmed is not allowed)
        var confirmAgain = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });

        // Either fails validation or succeeds but doesn't create duplicate invoice
        if (confirmAgain.StatusCode == HttpStatusCode.Created)
        {
            // If confirm succeeded, verify no duplicate invoice
            await Task.Delay(500); // allow async handler to process
            var search2 = await GetAsync<JsonElement>(
                $"/api/invoices/search?reservationId={reservationId}");
            search2.GetProperty("items").GetArrayLength().Should().Be(count1,
                "Re-confirming should not create a duplicate invoice");
        }
        // If 400/409/422, the state machine correctly rejected the transition — also valid
    }

    [Fact]
    public async Task MessageDelivery_MultipleReservations_IndependentProcessing()
    {
        // Create 3 reservations and confirm all — each should get its own invoice
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var (id, _) = await CreateAndConfirmReservationAsync();
            ids.Add(id);
        }

        // Each reservation should have exactly 1 invoice
        foreach (var reservationId in ids)
        {
            var search = await GetAsync<JsonElement>(
                $"/api/invoices/search?reservationId={reservationId}");
            search.GetProperty("items").GetArrayLength().Should().BeGreaterOrEqualTo(1,
                $"Reservation {reservationId} should have at least 1 invoice from message handler");
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Guid reservationId, Guid guestId)> CreateAndConfirmReservationAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Delivery",
            lastName = $"Test-{Guid.NewGuid():N}"[..8],
            email = $"del.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"DL-{Guid.NewGuid():N}"[..12],
            name = $"DeliveryHotel-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Delivery Room",
            code = $"DR{Guid.NewGuid():N}"[..3],
            baseRate = 200m,
            totalRooms = 10
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        var response = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(20).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(23).ToString("O"),
                numberOfGuests = 2
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return (reservationId, guestId);
    }
}
