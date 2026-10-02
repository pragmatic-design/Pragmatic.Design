using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A read that maps in memory still gets the navigations it declared.
/// </summary>
/// <remarks>
///     <para>
///         "The result is the entity" is what "the query does not project" means for every shape but
///         one, so it cannot be the whole gate for <c>NeedsEagerLoading</c>. <c>MapInMemory</c> is the
///         exception by definition: it answers with a DTO and does not project, because the executor
///         materialises the page and runs the generated <c>Selector</c> over it. Gated on the entity
///         alone, every path such a query declares would be dropped, and the mapper would read an empty
///         navigation.
///     </para>
///     <para>
///         ⚠️ The shape is invisible from the generated file alone — <c>IncludePaths</c> can be
///         emitted perfectly and read by nobody. This runs the executor against a real database, which
///         is the only place the two halves meet: <c>PrepareSource</c> applies the paths, then
///         <c>MapEach</c> reads them.
///     </para>
///     <para>
///         Measured by removal: putting the gate back to <c>IsSameEntityAndResult</c> alone turned
///         <b>2</b> tests of this suite red when measured, and both reds are the cases below. ⚠️ They fail with a <b>500</b>, not with a blank field — the generated
///         <c>Selector</c> reads <c>entity.Guest.FirstName</c> and the navigation is null. A DTO whose
///         flattened property is nullable would have got the quiet version of the same defect.
///     </para>
/// </remarks>
public sealed class AReadThatMapsInMemoryStillLoadsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ACardMappedInMemory_CarriesTheGuestReachedThroughTheNavigation()
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync("Hopper", "Grace");
        await CreateReservationAsync(guestId, propertyId, roomTypeId);

        var cards = await GetAsync<List<JsonElement>>($"api/reservations/cards?propertyId={propertyId}");

        cards.Should().ContainSingle("the filter runs on the database, before anything is mapped");

        cards[0].GetProperty("guestLastName").GetString().Should().Be("Hopper",
            "the mapper reads Reservation.Guest in memory, so this is blank unless the declared "
            + "[EagerLoad(\"Guest\")] reached the executor");
        cards[0].GetProperty("card").GetString().Should().StartWith("Hopper, Grace · ",
            "the card is assembled after the row arrives, which is why this read maps instead of "
            + "projecting");
    }

    /// <summary>
    ///     The control: the read really does map in memory rather than project.
    /// </summary>
    /// <remarks>
    ///     Without it the case above is satisfied by a query that projects in SQL, where the include
    ///     would be irrelevant and the in-memory gate would never matter. <c>card</c> is
    ///     <c>[MapIgnore]</c>d and computed from the DTO's own fields — a projecting read would have
    ///     to build that string in SQL, and the generated projection contains no such expression.
    /// </remarks>
    [Fact]
    public async Task TheCardIsBuiltAfterTheRowArrives()
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync("Lovelace", "Ada");
        await CreateReservationAsync(guestId, propertyId, roomTypeId);

        var cards = await GetAsync<List<JsonElement>>($"api/reservations/cards?propertyId={propertyId}");

        var card = cards.Single().GetProperty("card").GetString();

        card.Should().StartWith("Lovelace, Ada · RES-").And.EndWith(" · 3 nights",
            "three fields joined with separators and a word — the shape no Expression carries into "
            + "SQL, which is the whole reason this query declares MapInMemory");
    }

    private async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreatePrerequisitesAsync(
        string lastName, string firstName)
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName,
            lastName,
            email = $"card.{Guid.NewGuid():N}@test.com",
        });

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"MC-{Guid.NewGuid():N}"[..12],
            name = $"Card-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3,
        });

        var propertyId = property.GetProperty("id").GetGuid();

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "Card Room",
            code = "CRD",
            baseRate = 100m,
            totalRooms = 5,
        });

        return (guest.GetProperty("id").GetGuid(), propertyId, roomType.GetProperty("id").GetGuid());
    }

    private async Task CreateReservationAsync(Guid guestId, Guid propertyId, Guid roomTypeId)
    {
        var response = await Client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                numberOfGuests = 2,
            },
        }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
