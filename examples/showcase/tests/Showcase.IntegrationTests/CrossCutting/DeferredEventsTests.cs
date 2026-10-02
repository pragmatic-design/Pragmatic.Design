using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Domain events raised by mutations that are <b>nested</b> in an action, and therefore deferred.
/// </summary>
/// <remarks>
///     <para>
///         A mutation invoked on its own dispatches its <c>[Raises&lt;T&gt;]</c> events after its own
///         commit. Invoked from inside an action of the same boundary it does not commit at all: it
///         stages its writes and hands its events to the batch the action owns, and the action flushes
///         them after the single commit. The two paths are different code, and only the first one had
///         a test.
///     </para>
///     <para>
///         <c>CancelReservationsForPropertyAction</c> is the shape — N cancellations in one pass, each
///         raising <c>ReservationCancelled</c>, all deferred — and nothing exercised it. What makes the
///         effect visible is that the handler lives in another boundary and voids the invoice: if an
///         event is lost, an invoice stays open.
///     </para>
/// </remarks>
public class DeferredEventsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<(Guid PropertyId, Guid GuestId, Guid RoomTypeId)> ArrangeAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Deferred",
            lastName = "Events",
            email = $"def.{Guid.NewGuid():N}@test.com"
        });

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"DE-{Guid.NewGuid():N}"[..12],
            name = $"DefProp-{Guid.NewGuid():N}"[..20],
            city = "Bologna",
            country = "IT",
            starRating = 3
        });
        var propertyId = property.GetProperty("id").GetGuid();

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Def Room",
            code = $"DR{Guid.NewGuid():N}"[..3],
            baseRate = 200m,
            totalRooms = 5
        });

        return (propertyId, guest.GetProperty("id").GetGuid(), roomType.GetProperty("id").GetGuid());
    }

    /// <summary>Creates a reservation, confirms it, and returns it with the invoice the event created.</summary>
    private async Task<(Guid ReservationId, Guid InvoiceId)> ConfirmedReservationAsync(
        Guid guestId, Guid propertyId, Guid roomTypeId, int dayOffset)
    {
        // The reservation endpoint answers with the bare id, not an object: a Guid is a JSON string.
        var created = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(dayOffset).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(dayOffset + 2).ToString("O"),
                numberOfGuests = 1
            }
        });
        var reservationId = await created.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        // Confirming raises ReservationConfirmed, which Billing turns into a draft invoice.
        await PostAsync($"/api/reservations/{reservationId}/confirm", new { });

        var invoices = await GetAsync<JsonElement>($"/api/invoices/search?reservationId={reservationId}");
        var items = invoices.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0,
            "the confirmation event is what creates the invoice, and the rest of this test reads it");

        return (reservationId, items[0].GetProperty("id").GetGuid());
    }

    private async Task<string?> InvoiceStatusAsync(Guid invoiceId)
        => (await GetAsync<JsonElement>($"/api/invoices/{invoiceId}")).GetProperty("status").GetString();

    /// <summary>
    ///     Two cancellations in one pass: both events reach the other boundary.
    /// </summary>
    /// <remarks>
    ///     The count is the point. One commit for the pass means one flush, and a flush that dropped
    ///     everything but the first event would leave an invoice open while the reservation it belongs
    ///     to is cancelled — a state nobody would find until someone was billed for it.
    /// </remarks>
    [Fact]
    public async Task EveryDeferredEventOfThePassReachesItsHandler()
    {
        var (propertyId, guestId, roomTypeId) = await ArrangeAsync();

        var first = await ConfirmedReservationAsync(guestId, propertyId, roomTypeId, 60);
        var second = await ConfirmedReservationAsync(guestId, propertyId, roomTypeId, 70);

        await PostAsync($"/api/properties/{propertyId}/cancel-reservations", new
        {
            reason = "The property is closing for refurbishment."
        });

        (await InvoiceStatusAsync(first.InvoiceId)).Should().Be("Cancelled",
            "the first cancellation's event has to survive being deferred");
        (await InvoiceStatusAsync(second.InvoiceId)).Should().Be("Cancelled",
            "and so does the second — one flush must carry all of them, not the last or the first");
    }

    /// <summary>
    ///     A control: an untouched reservation of another property keeps its invoice.
    /// </summary>
    /// <remarks>
    ///     Without this, a handler that voided every invoice it could find would pass the test above.
    /// </remarks>
    [Fact]
    public async Task AnUnrelatedInvoiceIsLeftAlone()
    {
        var (propertyId, guestId, roomTypeId) = await ArrangeAsync();
        var target = await ConfirmedReservationAsync(guestId, propertyId, roomTypeId, 80);

        var (otherProperty, otherGuest, otherRoomType) = await ArrangeAsync();
        var untouched = await ConfirmedReservationAsync(otherGuest, otherProperty, otherRoomType, 90);

        await PostAsync($"/api/properties/{propertyId}/cancel-reservations", new
        {
            reason = "Only this property."
        });

        (await InvoiceStatusAsync(target.InvoiceId)).Should().Be("Cancelled");
        (await InvoiceStatusAsync(untouched.InvoiceId)).Should().NotBe("Cancelled",
            "the events carry the reservation they belong to, and nothing wider");
    }
}
