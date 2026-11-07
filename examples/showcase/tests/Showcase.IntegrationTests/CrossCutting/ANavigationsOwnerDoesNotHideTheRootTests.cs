using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A reservation reads its guest whoever owns the guest: Pragmatic's owner and scope filters apply to a read's root, not to a navigation it
///     reaches (#580). This is EF Core's behaviour, which the Sql and documents engines now share (their hosts' suites have the same cases). Here it
///     pins the reference: the guest is created by one user, the reservation by another who cannot read the guest.
/// </summary>
public class ANavigationsOwnerDoesNotHideTheRootTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Permissions = "booking.reservation.read";

    [Fact]
    public async Task AReservation_IsReadWithItsGuest_ByACallerWhoCannotReadTheGuest()
    {
        var owner = "nav-owner-" + Guid.NewGuid().ToString("N")[..8];
        var reservationId = await CreateReservationAs(owner);

        using var caller = CreateClientAsWithPermissions(owner, "Navigation Owner", Permissions, "booking.reservation.create");
        var read = await caller.GetAsync($"/api/reservations/{reservationId}");

        read.StatusCode.Should().Be(HttpStatusCode.OK, "Body: {0}", await read.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ASearch_ListsTheReservationItCounts_WithItsGuestsName()
    {
        var owner = "nav-owner-" + Guid.NewGuid().ToString("N")[..8];
        await CreateReservationAs(owner);

        using var caller = CreateClientAsWithPermissions(owner, "Navigation Owner", Permissions, "booking.reservation.create");
        var search = await caller.GetAsync("/api/reservations/search");

        search.StatusCode.Should().Be(HttpStatusCode.OK, "Body: {0}", await search.Content.ReadAsStringAsync());
        var page = await search.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        page.GetProperty("totalCount").GetInt32().Should().Be(1, "the control: the caller owns one reservation");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(1, "the items are the rows the count counts");
        items[0].GetProperty("guestFirstName").GetString().Should().Be("Navigated");
    }

    /// <summary>The guest, property and room type as the default user; the reservation as <paramref name="owner" />.</summary>
    private async Task<Guid> CreateReservationAs(string owner)
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new { firstName = "Navigated", lastName = "Guest", email = $"nav.{Guid.NewGuid():N}@test.com" });
        var guestId = guest.GetProperty("id").GetGuid();
        var property = await PostAsync<JsonElement>("/api/properties", new { code = $"NV-{Guid.NewGuid():N}"[..12], name = "Navigation Hotel", city = "Rome", country = "IT", starRating = 3 });
        var propertyId = property.GetProperty("id").GetGuid();
        var roomType = await PostAsync<JsonElement>("/api/room-types", new { propertyId, name = "Navigation Room", code = $"NR{Guid.NewGuid():N}"[..3], baseRate = 100m, totalRooms = 5 });
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        using var client = CreateClientAsWithPermissions(owner, "Navigation Owner", Permissions, "booking.reservation.create");
        var response = await client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 1
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
