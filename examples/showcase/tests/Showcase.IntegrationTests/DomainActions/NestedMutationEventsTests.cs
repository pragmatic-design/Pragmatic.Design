using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.DomainActions;

/// <summary>
///     An action that invokes a mutation of its own boundary: the writes land, and the mutation's
///     events still reach their handlers.
/// </summary>
/// <remarks>
///     <para>
///         The second half is the one that can go wrong quietly. Once the nested mutation stops
///         committing, it also stops dispatching: its events are deferred into the batch the root
///         opened, and unless the root flushes them after its own commit they are simply dropped. The
///         operation succeeds, the rows are right, and the handler never runs.
///     </para>
///     <para>
///         <c>ReservationCancelled</c> is handled in Billing by cancelling the reservation's invoice, so
///         the invoice's state is the evidence — a consequence in another boundary rather than a
///         counter this test could have set itself. Measured before the flush existed: it stayed
///         <c>Draft</c>.
///     </para>
/// </remarks>
public class NestedMutationEventsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ClosingAProperty_CancelsEveryReservationInOneTransaction()
    {
        var (propertyId, reservationIds) = await PropertyWithReservationsAsync(3);

        var response = await Client.PostAsJsonAsync(
            $"/api/properties/{propertyId}/cancel-reservations",
            new { reason = "Refurbishment" });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<int>(JsonOptions)).Should().Be(3);

        foreach (var id in reservationIds)
        {
            var reservation = await GetAsync<JsonElement>($"/api/reservations/{id}");
            reservation.GetProperty("status").GetString().Should().Be("Cancelled");
        }
    }

    /// <summary>
    ///     The half that fails silently: deferred events that nobody flushes.
    /// </summary>
    [Fact]
    public async Task ClosingAProperty_StillDispatchesEachCancellationEvent()
    {
        var (propertyId, reservationIds) = await PropertyWithReservationsAsync(2);

        // Confirming raises ReservationConfirmed, which Billing handles by creating the invoice. That
        // chain is already covered elsewhere; here it is the setup that gives the cancellation
        // something observable to void.
        foreach (var id in reservationIds)
            (await PostAsync($"/api/reservations/{id}/confirm", new { })).StatusCode
                .Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var response = await Client.PostAsJsonAsync(
            $"/api/properties/{propertyId}/cancel-reservations",
            new { reason = "Licence withdrawn" });

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        foreach (var id in reservationIds)
        {
            var invoices = await GetAsync<JsonElement>($"/api/invoices/search?reservationId={id}");
            var items = invoices.GetProperty("items");

            items.GetArrayLength().Should().Be(1);
            items[0].GetProperty("status").GetString().Should().Be("Cancelled",
                "ReservationCancelled is handled in Billing by voiding the invoice. A nested mutation "
                + "defers its events into the root's batch; if the root does not flush them after its "
                + "commit they are dropped, and nothing anywhere says so");
        }
    }

    // =========================================================================

    private async Task<(Guid PropertyId, List<Guid> ReservationIds)> PropertyWithReservationsAsync(int count)
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Close",
            lastName = "Test",
            email = $"close.{Guid.NewGuid():N}@test.com"
        });

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"CL-{Guid.NewGuid():N}"[..12],
            name = $"CloseProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = property.GetProperty("id").GetGuid();

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Close Room",
            code = "CLR",
            baseRate = 100m,
            totalRooms = 20
        });

        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var response = await PostAsync("/api/reservations?api-version=1.0", new
            {
                request = new
                {
                    guestId = guest.GetProperty("id").GetGuid(),
                    propertyId,
                    roomTypeId = roomType.GetProperty("id").GetGuid(),
                    checkIn = DateTimeOffset.UtcNow.AddDays(7 + (i * 10)).ToString("O"),
                    checkOut = DateTimeOffset.UtcNow.AddDays(10 + (i * 10)).ToString("O"),
                    numberOfGuests = 2
                }
            });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
            ids.Add(await response.Content.ReadFromJsonAsync<Guid>(JsonOptions));
        }

        return (propertyId, ids);
    }
}
