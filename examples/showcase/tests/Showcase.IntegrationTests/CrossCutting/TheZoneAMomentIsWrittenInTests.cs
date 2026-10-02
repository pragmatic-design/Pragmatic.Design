using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A wall clock somebody typed becomes an instant, and which wall clock it was read against is a
///     declaration on the property.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="TheZoneAMomentIsReadInTests"/> covers the other direction — an instant read back
///         in the caller's zone. This is the half that decides what gets stored, and it has two
///         answers, not one: a guest typing on their phone means their own zone
///         (<c>[FromClientTimezone]</c>), a clerk typing at the front desk means the hotel's
///         (<c>[FromBusinessTimezone]</c>). The host declares the hotel's zone with
///         <c>UseTemporal(t =&gt; t.UseBusinessTimeZone("Europe/Rome"))</c>, which is what
///         <c>[ToBusinessTimezone]</c> reads against here.
///     </para>
///     <para>
///         ⚠️ The endpoint deserializes a generated <c>{Mutation}Body</c> record, so the behaviour has
///         to be registered against that record. Registered only against the mutation type, which
///         nothing ever deserializes, the front desk's half would be inert; the front desk's case
///         guards that.
///     </para>
///     <para>
///         ⚠️ The request properties are <c>DateTime</c> and not <c>DateTimeOffset</c>, and that is what
///         makes these tests mean anything. A DateTimeOffset carries its own offset, so the instant is
///         already decided before any attribute runs and all six behave identically — a suite built on
///         one would be green whatever the declaration said.
///     </para>
///     <para>
///         Dates are in 2027 and away from any DST boundary on purpose: Europe/Rome is on CEST
///         (UTC+2), America/New_York on EDT (UTC−4), Asia/Tokyo on UTC+9 all year.
///     </para>
/// </remarks>
public class TheZoneAMomentIsWrittenInTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string GuestTyped = "2027-05-04T21:30:00";
    private const string FrontDeskTyped = "2027-05-04T21:45:00";

    /// <summary>
    ///     The guest is in New York and types 21:30. The hotel reads 03:30 the next morning.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         21:30 EDT = 01:30Z = 03:30 CEST. Both declarations are in this one moment:
    ///         <c>[FromClientTimezone]</c> chose New York on the way in, <c>[ToBusinessTimezone]</c>
    ///         chose Rome on the way out.
    ///     </para>
    ///     <para>
    ///         ⚠️ Which is why the offset is asserted separately. <c>DateTimeOffset</c> equality is
    ///         equality of the <b>instant</b>, so <c>01:30+00:00</c> equals <c>03:30+02:00</c> and the
    ///         first assertion alone cannot see the output attribute at all — measured by removing
    ///         <c>[ToBusinessTimezone]</c>, which left this case green.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AWallClockTheGuestTyped_IsReadAgainstTheGuestsZone()
    {
        using var client = ClientIn("America/New_York");
        var reservationId = await AReservationAsync(client, GuestTyped);

        var row = await RowAsync(client, reservationId);

        Moment(row, "expectedArrival").Should().Be(DateTimeOffset.Parse("2027-05-05T03:30:00+02:00"));
        Moment(row, "expectedArrival").Offset.Should().Be(TimeSpan.FromHours(2),
            "the instant is the guest's zone's doing, the offset is the hotel's");
    }

    /// <summary>
    ///     The control on the caller's zone: the same wall clock from Tokyo is a different instant.
    /// </summary>
    /// <remarks>
    ///     Without it, "the arrival is converted" is satisfied by a conversion against any fixed zone —
    ///     the server's included, which is the bug the attribute exists to prevent. 21:30 JST = 12:30Z
    ///     = 14:30 CEST, thirteen hours from the case above.
    /// </remarks>
    [Fact]
    public async Task TheSameWallClockFromAnotherZone_IsAnotherInstant()
    {
        using var client = ClientIn("Asia/Tokyo");
        var reservationId = await AReservationAsync(client, GuestTyped);

        var row = await RowAsync(client, reservationId);

        Moment(row, "expectedArrival").Should().Be(DateTimeOffset.Parse("2027-05-04T14:30:00+02:00"));
    }

    /// <summary>
    ///     The control on the reader: in one payload, the property that follows the caller and the
    ///     property that does not.
    /// </summary>
    /// <remarks>
    ///     <c>CheckIn</c> is <c>[ToClientTimezone]</c> and comes back at New York's offset;
    ///     <c>ExpectedArrival</c> is <c>[ToBusinessTimezone]</c> and comes back at Rome's. Without this
    ///     the two attributes are interchangeable, and a front desk reading an arrival in the guest's
    ///     zone is a real way to hold a room on the wrong night.
    /// </remarks>
    [Fact]
    public async Task ThePropertyThatFollowsTheReader_AndTheOneThatDoesNot_DisagreeInOnePayload()
    {
        using var client = ClientIn("America/New_York");
        var reservationId = await AReservationAsync(client, GuestTyped);

        var row = await RowAsync(client, reservationId);

        Moment(row, "checkIn").Offset.Should().Be(TimeSpan.FromHours(-4),
            "[ToClientTimezone] follows the caller");
        Moment(row, "expectedArrival").Offset.Should().Be(TimeSpan.FromHours(2),
            "[ToBusinessTimezone] does not");
    }

    /// <summary>
    ///     What the front desk types is what the front desk reads, whoever asks.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The caller declares Asia/Tokyo deliberately: if <c>ActualArrival</c> were
    ///         <c>[FromClientTimezone]</c>, 21:45 would be read as Tokyo's and come back as 14:45 in
    ///         Rome. Asserting that the round trip is an identity is what separates the two
    ///         declarations — nothing weaker does.
    ///     </para>
    ///     <para>
    ///         ⚠️ This is the case that guards the registration. The endpoint deserializes a generated
    ///         <c>CheckInGuestMutationBody</c>; with the behaviour registered only against
    ///         <c>CheckInGuestMutation</c> — which nothing deserializes — the wall clock would arrive
    ///         unconverted while the generated record carried the property's documentation describing
    ///         the conversion.
    ///     </para>
    ///     <para>
    ///         The tenant is <c>premium-hotel</c> because check-in before the scheduled date is behind
    ///         the <c>early-check-in</c> flag, which is targeted at that tenant.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task AWallClockTheFrontDeskTyped_IsReadAgainstTheHotelsZone()
    {
        using var client = ClientIn("Asia/Tokyo", tenantId: "premium-hotel");
        var reservationId = await AReservationAsync(client, GuestTyped);

        await PostWithClientAsync(client, $"/api/reservations/{reservationId}/confirm", new { });
        var checkedIn = await PostStatusOnlyWithClientAsync(client,
            $"/api/reservations/{reservationId}/check-in", new { actualArrival = FrontDeskTyped });
        checkedIn.Should().Be(System.Net.HttpStatusCode.NoContent);

        var row = await RowAsync(client, reservationId);

        Moment(row, "actualArrival").Should().Be(DateTimeOffset.Parse("2027-05-04T21:45:00+02:00"),
            "the clerk typed the hotel's wall clock and the hotel reads the hotel's wall clock");
    }

    private static DateTimeOffset Moment(JsonElement row, string property)
        => DateTimeOffset.Parse(row.GetProperty(property).GetString()!);

    private HttpClient ClientIn(string timeZone, string tenantId = "test-tenant")
    {
        var client = CreateClientAs("tz-writer", "Timezone Writer", tenantId);
        client.DefaultRequestHeaders.Add("X-Timezone", timeZone);
        return client;
    }

    /// <summary>
    ///     The reservation by id, which returns the same <c>ReservationSummaryDto</c> the search does.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not the paged search. This suite creates reservations in every other file too, so
    ///     <c>search?pageSize=100</c> stopped containing the row under test as the suite grew — four
    ///     tests failing with "Sequence contains no matching element", which reads like a broken
    ///     feature and is a broken query.
    /// </remarks>
    private static async Task<JsonElement> RowAsync(HttpClient client, Guid reservationId)
    {
        var response = await client.GetAsync($"api/reservations/{reservationId}");
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    /// <summary>
    ///     A reservation created through the same client the assertions read with, so the caller's
    ///     declared zone is the same on both sides of the story.
    /// </summary>
    private static async Task<Guid> AReservationAsync(HttpClient client, string expectedArrival)
    {
        var guest = await CreatedAsync(client, "/api/guests", new
        {
            firstName = "Zone",
            lastName = "Writer",
            email = $"zone.{Guid.NewGuid():N}@test.com"
        });

        var property = await CreatedAsync(client, "/api/properties", new
        {
            code = $"TW-{Guid.NewGuid():N}"[..12],
            name = "Timezone Writer Hotel",
            city = "Rome",
            country = "IT",
            starRating = 3
        });

        var roomType = await CreatedAsync(client, "/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "TW Room",
            code = "TWR",
            baseRate = 100m,
            totalRooms = 5
        });

        var created = await client.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId = guest.GetProperty("id").GetGuid(),
                propertyId = property.GetProperty("id").GetGuid(),
                roomTypeId = roomType.GetProperty("id").GetGuid(),
                checkIn = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                numberOfGuests = 1,
                expectedArrival
            }
        }, JsonOptions);

        created.EnsureSuccessStatusCode();

        return await created.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private static async Task<JsonElement> CreatedAsync(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }
}
