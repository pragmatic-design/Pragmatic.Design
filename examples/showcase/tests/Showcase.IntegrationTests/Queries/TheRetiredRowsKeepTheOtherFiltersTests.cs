using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Queries;

/// <summary>
///     <c>[WithoutFilter&lt;Reservation&gt;(SoftDeleteOnly = true)]</c> reads the retired rows and keeps the ownership filter (#732):
///     the caller's own retired reservation is listed, another owner's retired one is not.
/// </summary>
/// <remarks>
///     Before #732 the only way to reach a soft-deleted row from a query was <c>[WithoutFilter&lt;T&gt;]</c>, which lifts the entity's
///     every Pragmatic filter, ownership included. The suite is linked into the Sql and documents ones, so the three engines answer.
/// </remarks>
public class TheRetiredRowsKeepTheOtherFiltersTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Route = "/api/reservations/with-retired?pageSize=200";

    [Fact]
    public async Task TheCallersRetiredReservation_IsListed_AnotherOwnersIsNot()
    {
        using var mine = Caretaker();
        using var theirs = Caretaker();
        var myRetired = await ARetiredReservationOfAsync(mine);
        var theirRetired = await ARetiredReservationOfAsync(theirs);

        var seen = await IdsAsync(mine);

        seen.Should().Contain(myRetired, "the soft-delete filter is lifted");
        seen.Should().NotContain(theirRetired, "the ownership filter is not: alive, the caller would not have seen it either");
    }

    /// <summary>The control: a live reservation of the caller's is listed too, and is hidden from the normal search only when retired.</summary>
    [Fact]
    public async Task TheCallersLiveReservation_IsListedToo()
    {
        using var mine = Caretaker();
        var live = await AReservationOfAsync(mine);

        (await IdsAsync(mine)).Should().Contain(live);
    }

    private static async Task<List<Guid>> IdsAsync(HttpClient client)
    {
        var response = await client.GetAsync(Route);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
    }

    // A caller who may create, retire and read reservations and holds no ownership bypass (booking.reservation.view-all).
    private HttpClient Caretaker()
        => CreateClientWithPermissions("booking.reservation.read", "booking.reservation.create", "booking.reservation.update", "booking.reservation.delete");

    private async Task<Guid> ARetiredReservationOfAsync(HttpClient client)
    {
        var id = await AReservationOfAsync(client);
        var retire = await client.DeleteAsync($"/api/reservations/{id}?api-version=1.0");
        retire.IsSuccessStatusCode.Should().BeTrue(await retire.Content.ReadAsStringAsync());
        return id;
    }

    private async Task<Guid> AReservationOfAsync(HttpClient client)
    {
        var guest = await (await Client.PostAsJsonAsync("/api/guests", new
        {
            firstName = "Retired",
            lastName = $"Test-{Guid.NewGuid():N}"[..20],
            email = $"ret.{Guid.NewGuid():N}@test.com"
        }, JsonOptions)).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var property = await (await Client.PostAsJsonAsync("/api/properties", new
        {
            code = $"RT-{Guid.NewGuid():N}"[..12],
            name = $"RetTest-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        }, JsonOptions)).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var roomType = await (await Client.PostAsJsonAsync("/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "Retired Room",
            code = $"RR{Guid.NewGuid():N}"[..3],
            baseRate = 100m,
            totalRooms = 5
        }, JsonOptions)).Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        var response = await client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest.GetProperty("id").GetGuid(),
                propertyId = property.GetProperty("id").GetGuid(),
                roomTypeId = roomType.GetProperty("id").GetGuid(),
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 1
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
