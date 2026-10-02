using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Queries;

/// <summary>
///     A route derived from a specification answers the same rows as a hand-written query over the
///     same rule.
/// </summary>
/// <remarks>
///     <para>
///         <c>ReservationSpecifications.IsConfirmed()</c> carries <c>[Query]</c>, so a query type is derived from
///         it, and <c>[Endpoint]</c>, so that derived query has a route. The rule is written once;
///         <c>SearchReservationsQuery</c> reaches the same rows by declaring <c>Status</c> as a filter.
///     </para>
///     <para>
///         ⚠️ A generator test alone would not have caught a derived route that filters nothing: the
///         file would be generated, the assertions on its text would pass, and the route would answer
///         every reservation in the database. The two paths are compared here for that reason, and the
///         pending reservation is what makes the comparison mean something.
///     </para>
/// </remarks>
public class SpecificationPromotedToARouteTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheDerivedRoute_AnswersTheSameRowsAsTheHandWrittenQuery()
    {
        var confirmed = await AConfirmedReservationAsync();
        var pending = await AReservationAsync();

        var derived = await GetAsync<JsonElement>("/api/reservations/confirmed");
        var handWritten = await GetAsync<JsonElement>(
            "/api/reservations/search?status=Confirmed&pageSize=200");

        var fromDerived = Ids(derived.EnumerateArray());
        var fromSearch = Ids(handWritten.GetProperty("items").EnumerateArray());

        fromDerived.Should().Contain(confirmed, "the rule the route was derived from selects it");
        fromSearch.Should().Contain(confirmed, "and the hand-written query selects it too");

        fromDerived.Should().NotContain(pending,
            "a derived route that filters nothing would answer every reservation");

        // The two paths agree on the rows they both can see. Compared as sets over the rows the search
        // returned, because the derived route is not paged and the search is: an id the search has not
        // reached yet is not a disagreement.
        fromSearch.Where(id => fromDerived.Contains(id))
            .Should().BeEquivalentTo(fromSearch,
                "one rule, two ways of reading it, the same rows");
    }

    /// <summary>The derived route asks for the permission written beside the specification.</summary>
    /// <remarks>
    ///     <para>
    ///         The other half: without it "the route works" would be satisfied by a route open to anyone.
    ///         The permission is a generated constant — <c>BookingPermissions.Reservation.Read</c> — so
    ///         this also measures that a constant this generator writes reaches a route this generator
    ///         builds.
    ///     </para>
    ///     <para>
    ///         ⚠️ Measured with a caller who <b>is</b> authenticated and holds a different permission, not
    ///         with an anonymous one: an anonymous request is refused before routing, so it answers the
    ///         same whether the route exists or not. That version of this case passed with the route
    ///         removed.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheDerivedRoute_AsksForThePermission()
    {
        var wrongPermission = CreateClientWithPermissions("booking.reservation.write");

        var response = await wrongPermission.GetAsync("/api/reservations/confirmed");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the specification declares [RequirePermission(BookingPermissions.Reservation.Read)]");
    }

    /// <summary>The published document lists the derived route.</summary>
    /// <remarks>
    ///     A route that answers and is absent from the contract is half a route: a generated client does
    ///     not know it exists. The derived query never becomes a symbol anything can reflect over, so its
    ///     presence here is what proves the model reached the manifest and the document, not only the
    ///     handler.
    /// </remarks>
    [Fact]
    public async Task ThePublishedDocument_ListsTheDerivedRoute()
    {
        var document = await GetAsync<JsonElement>("/openapi/v1.json");

        document.GetProperty("paths").TryGetProperty("/api/reservations/confirmed", out var path)
            .Should().BeTrue("the derived route belongs to the published contract");

        path.TryGetProperty("get", out _).Should().BeTrue("the verb the specification declared");
    }

    private async Task<Guid> AReservationAsync()
    {
        var (guestId, propertyId, roomTypeId) = await PrerequisitesAsync();

        var response = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(60).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(63).ToString("O"),
                numberOfGuests = 2,
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private async Task<Guid> AConfirmedReservationAsync()
    {
        var id = await AReservationAsync();

        var confirm = await PostAsync($"/api/reservations/{id}/confirm", new { });
        confirm.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return id;
    }

    /// <summary>A guest, a property and a room type — what a reservation needs to exist.</summary>
    /// <remarks>
    ///     Written here rather than shared, as every other case in this suite writes it: the helper is
    ///     private per file throughout, and one test reaching into another's fixture is how a change to
    ///     one case starts failing a case nobody was touching.
    /// </remarks>
    private async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> PrerequisitesAsync()
    {
        var guest = await ReadAsync(await PostAsync("/api/guests", new
        {
            firstName = "Spec",
            lastName = "Guest",
            email = $"spec.{Guid.NewGuid():N}@test.com",
        }));

        var property = await ReadAsync(await PostAsync("/api/properties", new
        {
            code = $"SP-{Guid.NewGuid():N}"[..12],
            name = $"SpecProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3,
        }));

        var roomType = await ReadAsync(await PostAsync("/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "Spec Room",
            code = "SPR",
            baseRate = 100m,
            totalRooms = 5,
        }));

        return (guest.GetProperty("id").GetGuid(),
            property.GetProperty("id").GetGuid(),
            roomType.GetProperty("id").GetGuid());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    private static List<Guid> Ids(JsonElement.ArrayEnumerator rows)
        => rows.Select(row => row.GetProperty("id").GetGuid()).ToList();
}
