using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests the messaging pipeline end-to-end:
///       - Domain event delivery via message handlers
///       - Cross-boundary event cascade (Booking → Billing)
///       - Message audit trail (IAuditStore records handled events)
/// </summary>
public class MessagingPipelineTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Event delivery: ReservationConfirmed → Invoice created with correct fields
    // (Extends DomainEventTests by verifying the MESSAGING layer, not just the result)
    // =========================================================================

    [Fact]
    public async Task MessagingPipeline_ReservationConfirmed_InvoiceCreatedViaHandler()
    {
        var reservationId = await CreateAndConfirmReservationAsync();

        // The ReservationConfirmedHandler (with [MessageHandler] + [Retry]) should have processed
        // the ReservationConfirmed event and created an invoice via IBillingActions
        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");

        items.GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "[MessageHandler] ReservationConfirmedHandler should deliver the event and create an invoice");

        var invoice = items[0];
        invoice.GetProperty("id").GetGuid().Should().NotBeEmpty(
            "Invoice should have a non-empty ID");
        invoice.GetProperty("reservationId").GetGuid().Should().Be(reservationId,
            "Invoice created via messaging pipeline should reference the correct reservation");
        invoice.GetProperty("invoiceNumber").GetString().Should().NotBeNullOrEmpty(
            "Invoice should have an auto-generated invoice number");
        invoice.GetProperty("totalAmount").GetDecimal().Should().BeGreaterThan(0,
            "Invoice total amount should be positive (computed from reservation)");
    }

    // =========================================================================
    // Cross-boundary cascade: Confirm → Invoice → Pay → PaymentReceived
    // Tests the full Booking → Billing → Booking round-trip via messaging
    // =========================================================================

    [Fact]
    public async Task MessagingPipeline_InvoicePaid_ReservationTransitionsToPaymentReceived()
    {
        var reservationId = await CreateAndConfirmReservationAsync();

        // Get the invoice auto-created by ReservationConfirmedHandler
        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1);
        var invoiceId = items[0].GetProperty("id").GetGuid();

        // Pay the invoice — triggers InvoicePaid event → InvoicePaidHandler → MarkPaymentReceived
        var payResponse = await PostAsync($"/api/invoices/{invoiceId}/pay", new { });
        payResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.NoContent);

        // Verify the invoice itself is now Paid
        var paidInvoice = await GetAsync<JsonElement>($"/api/invoices/{invoiceId}");
        paidInvoice.GetProperty("id").GetGuid().Should().Be(invoiceId);
        paidInvoice.GetProperty("status").GetString().Should().Be("Paid",
            "Invoice status should transition to Paid after pay action");

        // Verify the reservation transitioned to PaymentReceived via the cross-boundary handler
        var reservation = await GetAsync<JsonElement>($"/api/reservations/{reservationId}");
        reservation.GetProperty("id").GetGuid().Should().Be(reservationId);
        reservation.GetProperty("status").GetString().Should().Be("PaymentReceived",
            "InvoicePaidHandler should trigger MarkPaymentReceived on the reservation via messaging");
    }

    // =========================================================================
    // Audit trail: handled events reach the framework trail, in the database
    // =========================================================================

    [Fact]
    public async Task MessageAudit_AHandledEventReachesTheTrail()
    {
        // Rewritten when messaging moved onto Pragmatic.Audit. The previous version returned early when
        // the store was missing, and again when it held nothing -- so it passed whether or not a single
        // entry was ever written. Both stores are now one trail on the same database the test can read,
        // and there is nothing left to excuse.
        using var scope = Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditTrailReader>();

        await CreateAndConfirmReservationAsync();
        await Task.Delay(1000);

        var page = await reader.QueryAsync(new AuditQuery { Category = AuditCategory.Message, Limit = 100 });

        page.Entries.Should().Contain(e => e.TargetType!.Contains("ReservationConfirmed"),
            "the confirmation publishes an event, and a handled event is what the trail is for");
    }

    [Fact]
    public async Task MessageAudit_FilteringByOutcome_ReturnsOnlyThatOutcome()
    {
        using var scope = Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditTrailReader>();

        await CreateAndConfirmReservationAsync();
        await Task.Delay(1000);

        var page = await reader.QueryAsync(new AuditQuery
        {
            Category = AuditCategory.Message,
            Outcome = AuditOutcome.Success,
            Limit = 100,
        });

        page.Entries.Should().NotBeEmpty();
        page.Entries.Should().OnlyContain(e => e.Outcome == AuditOutcome.Success);
    }

    [Fact]
    public async Task MessageAudit_TheTrailNeverHoldsTheMessageBody()
    {
        // The trail holds no message body: a PayloadJson field on the messaging entry would carry the
        // serialized message, personal data included. There is no such field, and this is what would
        // catch anyone reintroducing one through the detail.
        using var scope = Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditTrailReader>();

        await CreateAndConfirmReservationAsync();
        await Task.Delay(1000);

        var page = await reader.QueryAsync(new AuditQuery { Category = AuditCategory.Message, Limit = 100 });

        page.Entries.Should().OnlyContain(e => e.Detail == null || !e.Detail.Contains('{'),
            "a serialized body in the detail would put the payload back where it was removed from");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<Guid> CreateAndConfirmReservationAsync()
    {
        var reservationId = await CreateFullReservationAsync();
        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        return reservationId;
    }

    private async Task<Guid> CreateFullReservationAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Msg",
            lastName = "Pipeline",
            email = $"msg.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"MP-{Guid.NewGuid():N}"[..12],
            name = $"MsgProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Msg Room",
            code = $"MR{Guid.NewGuid():N}"[..3],
            baseRate = 150m,
            totalRooms = 5
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        var response = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(14).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(17).ToString("O"),
                numberOfGuests = 2
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
