using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests domain event features via HTTP endpoints.
///     Covers matrix features:
///       - Domain Events RaiseEvent()
///       - Composite Action [CompositeAction] (indirectly via event handler)
///       - Internal Action [DomainAction(Internal=true)]
/// </summary>
public class DomainEventTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Domain Event: ReservationConfirmed → Invoice auto-created with fields
    // =========================================================================

    [Fact]
    public async Task DomainEvent_ReservationConfirmed_InvoiceHasCorrectFields()
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync();
        var reservationId = await CreateReservationAsync(guestId, propertyId, roomTypeId);

        // Confirm triggers ReservationConfirmed event → event handler creates invoice
        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify invoice was created with correct data from the event
        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "ReservationConfirmed event handler should create an invoice");

        var invoice = items[0];
        invoice.GetProperty("reservationId").GetGuid().Should().Be(reservationId,
            "Invoice should reference the confirmed reservation");
        invoice.GetProperty("invoiceNumber").GetString().Should().StartWith("INV-",
            "InvoiceNumber should be auto-generated");
        invoice.GetProperty("totalAmount").GetDecimal().Should().BeGreaterThan(0,
            "Invoice should have a positive total amount");
    }

    // =========================================================================
    // Domain Event: InvoicePaid → Status changes
    // =========================================================================

    [Fact]
    public async Task DomainEvent_MarkInvoicePaid_StatusChanges()
    {
        var reservationId = await CreateAndConfirmReservationAsync();

        // Get invoice
        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);
        var invoiceId = items[0].GetProperty("id").GetGuid();

        // Mark as paid
        var payResponse = await PostAsync($"/api/invoices/{invoiceId}/pay", new { });
        payResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent);

        // Verify status changed
        var updatedInvoice = await GetAsync<JsonElement>($"/api/invoices/{invoiceId}");
        updatedInvoice.GetProperty("status").GetString().Should().Be("Paid",
            "Invoice status should be Paid after MarkInvoicePaid action");
    }

    // =========================================================================
    // Domain Event: State Machine transitions trigger events
    // =========================================================================

    [Fact]
    public async Task StateMachine_ConfirmReservation_StatusChangesToConfirmed()
    {
        var reservationId = await CreateFullReservationAsync();

        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify state changed
        var reservation = await GetAsync<JsonElement>($"/api/reservations/{reservationId}");
        reservation.GetProperty("status").GetString().Should().Be("Confirmed",
            "Reservation status should be Confirmed after confirm action");
    }

    [Fact]
    public async Task StateMachine_FullLifecycle_PendingToConfirmedToCheckedIn()
    {
        // Use premium-hotel tenant — early check-in flag allows check-in before scheduled date
        using var premiumClient = CreateClientAs("test-user", "Integration Test", "premium-hotel");
        var reservationId = await CreateFullReservationAsync(premiumClient);

        // Pending → Confirmed
        var confirmResponse = await PostWithClientAsync(premiumClient,
            $"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirmed → PaymentReceived
        var paymentResponse = await PostWithClientAsync(premiumClient,
            $"/api/reservations/{reservationId}/payment-received", new { });
        paymentResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // PaymentReceived → CheckedIn
        var checkInResponse = await PostWithClientAsync(premiumClient,
            $"/api/reservations/{reservationId}/check-in", new { });
        checkInResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify final state — through the same client that wrote it. Reading through the base Client
        // is reading from another tenant, and it answered only because the read-access Property was
        // unfiltered there.
        var reservation = await GetWithClientAsync<JsonElement>(
            premiumClient, $"/api/reservations/{reservationId}");
        reservation.GetProperty("status").GetString().Should().Be("CheckedIn",
            "Reservation should be CheckedIn after full lifecycle");
    }

    // =========================================================================
    // Domain Event: Invoice Refunded — full lifecycle through refund
    // =========================================================================

    [Fact]
    public async Task DomainEvent_RefundInvoice_StatusChangesToRefunded()
    {
        var reservationId = await CreateAndConfirmReservationAsync();

        // Get the invoice created by the ReservationConfirmed event
        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);
        var invoiceId = items[0].GetProperty("id").GetGuid();

        // Mark as paid first (Refund requires Paid status)
        var payResponse = await PostAsync($"/api/invoices/{invoiceId}/pay", new { });
        payResponse.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent],
            "Invoice must be paid before it can be refunded");

        // Refund requires "billing.invoice.refund" permission
        using var refundClient = CreateClientAs("refund-user", "Refund User");
        refundClient.DefaultRequestHeaders.Add("X-User-Permissions", "billing.invoice.refund");

        var refundResponse = await PostWithClientAsync(refundClient,
            $"/api/invoices/{invoiceId}/refund",
            new { id = invoiceId, originalTransactionId = "txn-test-refund-001" });

        refundResponse.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent],
            "Refund should succeed for a paid invoice with billing.refund permission");

        // Verify invoice status is now Refunded
        var updatedInvoice = await GetAsync<JsonElement>($"/api/invoices/{invoiceId}");
        updatedInvoice.GetProperty("status").GetString().Should().Be("Refunded",
            "Invoice status should transition to Refunded after successful refund");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreatePrerequisitesAsync()
        => CreatePrerequisitesAsync(Client);

    private static async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreatePrerequisitesAsync(HttpClient client)
    {
        var guestResponse = await client.PostAsJsonAsync("/api/guests", new
        {
            firstName = "DE",
            lastName = "Guest",
            email = $"de.{Guid.NewGuid():N}@test.com"
        }, JsonOptions);
        guestResponse.EnsureSuccessStatusCode();
        var guest = await guestResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var guestId = guest.GetProperty("id").GetGuid();

        var propertyResponse = await client.PostAsJsonAsync("/api/properties", new
        {
            code = $"DE-{Guid.NewGuid():N}"[..12],
            name = $"DEProp-{Guid.NewGuid():N}"[..20],
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
            name = "DE Room",
            code = "DER",
            baseRate = 100m,
            totalRooms = 5
        }, JsonOptions);
        roomTypeResponse.EnsureSuccessStatusCode();
        var roomType = await roomTypeResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }

    private Task<Guid> CreateReservationAsync(Guid guestId, Guid propertyId, Guid roomTypeId)
        => CreateReservationAsync(Client, guestId, propertyId, roomTypeId);

    private static async Task<Guid> CreateReservationAsync(HttpClient client, Guid guestId, Guid propertyId, Guid roomTypeId)
    {
        var response = await client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
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
        }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private Task<Guid> CreateFullReservationAsync() => CreateFullReservationAsync(Client);

    private static async Task<Guid> CreateFullReservationAsync(HttpClient client)
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(client);
        return await CreateReservationAsync(client, guestId, propertyId, roomTypeId);
    }

    private async Task<Guid> CreateAndConfirmReservationAsync()
    {
        var reservationId = await CreateFullReservationAsync();
        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return reservationId;
    }
}
