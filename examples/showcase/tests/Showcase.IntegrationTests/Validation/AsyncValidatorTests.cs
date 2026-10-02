using System.Net;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Validation;

/// <summary>
///     The three refusals of a create, each from the place that owns it.
/// </summary>
/// <remarks>
///     <para>
///         The guest's own overlap is the <c>IAsyncValidator</c> on the request — rows the operation does
///         not load. An unknown room type is a <b>404 from the invoker</b>, which loads it by the key
///         inside the request. A room that is fully booked is the action's <c>ValidateLoadedAsync</c>, on
///         the room type already read. ⚠️ Not both in the validator, which would read the room type a
///         second time: a validator runs before the loads and cannot see them.
///     </para>
///     <para>
///         ⚠️ 422, not 400. A validator that refuses an understood request answers 422; 400 stays for
///         a request that could not be read at all — malformed body, a string where a number goes.
///         Collapsing the two would leave every client unable to tell its own bug from a message it
///         should show the person.
///     </para>
/// </remarks>
public class AsyncValidatorTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    ///     One guest, one stay at a time: the async validator reads the reservations of the guest and
    ///     refuses a second one over the same dates.
    /// </summary>
    /// <remarks>
    ///     A rule about rows the operation does <b>not</b> load, which is what an async validator is for —
    ///     and the room is free, so this is the guest's own overlap being refused and not the availability
    ///     rule. The first booking of the same pair is the control: it succeeds.
    /// </remarks>
    [Fact]
    public async Task AGuestWhoIsAlreadyStaying_CannotBookTheSameDatesTwice()
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(totalRooms: 5);
        var checkIn = DateTimeOffset.UtcNow.AddDays(120).ToString("O");
        var checkOut = DateTimeOffset.UtcNow.AddDays(123).ToString("O");

        object Booking() => new
        {
            request = new { guestId, propertyId, roomTypeId, checkIn, checkOut, numberOfGuests = 1 }
        };

        var first = await PostAsync("/api/reservations?api-version=1.0", Booking());
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());

        var second = await PostAsync("/api/reservations?api-version=1.0", Booking());

        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "the room is free — five of them — so what refuses is the guest's own overlap");
        (await second.Content.ReadAsStringAsync()).Should().Contain("validation.guest.already_booked");
    }

    /// <summary>
    ///     A room type that does not exist is a 404, from the load, before the body runs — which is what
    ///     this test's name has always said.
    /// </summary>
    [Fact]
    public async Task CreateReservation_InvalidRoomType_ReturnsNotFound()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "AV3",
            lastName = "Guest",
            email = $"av3.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"AV3-{Guid.NewGuid():N}"[..12],
            name = $"AVProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var response = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId = Guid.NewGuid(), // Non-existent room type
                checkIn = DateTimeOffset.UtcNow.AddDays(80).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(83).ToString("O"),
                numberOfGuests = 1
            }
        });

        // The [LoadEntity] on the action reads the room type once and answers 404 when the key names no
        // row: the refusal is the invoker's, before Execute.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "the invoker loads the room type by the key inside the request and answers 404");
    }

    /// <summary>
    ///     Tests that booking the same room/dates when fully booked returns the expected behavior.
    ///     The AllowAnonymousTests.FullyBooked test proves availability correctly returns 0 rooms.
    ///     The refusal is the action's <c>ValidateLoadedAsync</c>, which counts the overlapping
    ///     reservations against the room type the invoker loaded.
    /// </summary>
    [Fact]
    public async Task CreateReservation_OverlappingDates_FullyBooked_ValidatorShouldReject()
    {
        var (guestId, propertyId, roomTypeId) = await CreatePrerequisitesAsync(totalRooms: 1);

        var checkIn = DateTimeOffset.UtcNow.AddDays(60).ToString("O");
        var checkOut = DateTimeOffset.UtcNow.AddDays(63).ToString("O");

        // First reservation — should succeed (1 room available)
        var first = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new { guestId, propertyId, roomTypeId, checkIn, checkOut, numberOfGuests = 1 }
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second reservation for same room/dates
        var guest2 = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "AV2",
            lastName = "Guest",
            email = $"av2.{Guid.NewGuid():N}@test.com"
        });
        var guest2Id = guest2.GetProperty("id").GetGuid();

        var second = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest2Id,
                propertyId,
                roomTypeId,
                checkIn,
                checkOut,
                numberOfGuests = 1
            }
        });

        // The refusal is ValidateLoadedAsync on the action: it counts the overlapping reservations
        // against the room type the invoker loaded. A second guest, so the request validator's rule —
        // one guest, one stay — is not what answers here.
        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "the room is fully booked, and the rule runs on the row the invoker loaded");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreatePrerequisitesAsync(
        int totalRooms = 5)
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "AV",
            lastName = "Guest",
            email = $"av.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"AV-{Guid.NewGuid():N}"[..12],
            name = $"AVProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "AV Room",
            code = "AVR",
            baseRate = 100m,
            totalRooms
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }
}
