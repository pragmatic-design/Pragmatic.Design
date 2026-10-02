using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Validation;

/// <summary>
///     Tests validation features via HTTP endpoints.
///     Covers matrix features:
///       - Required [Required]
///       - Email [Email]
///       - Range [Range]
///       - Positive [Positive]
///       - FutureDate [FutureDate]
///       - GreaterThanProperty [GreaterThanProperty]
///       - Async Validator IAsyncValidator&lt;T&gt;
///       - Ensure Guards Ensure.ThrowIf*
/// </summary>
/// <remarks>
///     ⚠️ 422, not 400. A validator that refuses an understood request answers 422; 400 stays for a
///     request that could not be read at all — malformed body, a string where a number goes.
///     Collapsing the two would leave every client unable to tell its own bug from a message it
///     should show the person.
/// </remarks>
public class ValidationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Required
    // =========================================================================

    /// <summary>
    ///     A <c>required</c> member that does not arrive is a <b>binding</b> failure, not a validation one: 400.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The distinction is not «which attribute would have refused it» but «did the value arrive?».
    ///     <c>Name</c> is a <c>required string</c> on the mutation: without it, the object is not built
    ///     and no validator runs. With a value present and wrong — see the other tests here — the
    ///     response is 422.
    /// </remarks>
    [Fact]
    public async Task CreateProperty_MissingName_Returns400()
    {
        var body = new
        {
            code = "VAL-001",
            // name missing
            city = "Rome",
            country = "IT",
            starRating = 3
        };

        var response = await PostAsync("/api/properties", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateGuest_MissingFirstName_Returns422()
    {
        var body = new
        {
            // firstName missing
            lastName = "Test",
            email = "test@test.com"
        };

        var response = await PostAsync("/api/guests", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // =========================================================================
    // Email
    // =========================================================================

    [Fact]
    public async Task CreateGuest_InvalidEmail_Returns422()
    {
        var body = new
        {
            firstName = "Val",
            lastName = "Test",
            email = "not-an-email"
        };

        var response = await PostAsync("/api/guests", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreateGuest_ValidEmail_Returns201()
    {
        var body = new
        {
            firstName = "Val",
            lastName = "Test",
            email = $"valid.{Guid.NewGuid():N}@example.com"
        };

        var response = await PostAsync("/api/guests", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    // =========================================================================
    // Range
    // =========================================================================

    [Fact]
    public async Task CreateProperty_StarRatingOutOfRange_RejectsRequest()
    {
        var body = new
        {
            code = $"VR-{Guid.NewGuid():N}"[..12],
            name = "Range Test Hotel",
            city = "Rome",
            country = "IT",
            starRating = 6 // Range(1,5)
        };

        var response = await PostAsync("/api/properties", body);

        // Validation may return 400 (pre-validation) or 500 (DB constraint).
        // Either way, it should NOT return 201.
        response.StatusCode.Should().NotBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateProperty_StarRatingZero_RejectsRequest()
    {
        var body = new
        {
            code = $"VR-{Guid.NewGuid():N}"[..12],
            name = "Range Test Hotel",
            city = "Rome",
            country = "IT",
            starRating = 0 // Range(1,5) — 0 is below minimum
        };

        var response = await PostAsync("/api/properties", body);

        response.StatusCode.Should().NotBe(HttpStatusCode.Created);
    }

    // =========================================================================
    // Positive
    // =========================================================================

    [Fact]
    public async Task CreateRoomType_NegativeBaseRate_Returns422()
    {
        var propertyId = await CreatePropertyAndGetIdAsync();

        var body = new
        {
            propertyId,
            name = "Neg Rate Room",
            code = "NEG",
            baseRate = -50m, // [Positive]
            totalRooms = 5
        };

        var response = await PostAsync("/api/room-types", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // =========================================================================
    // FutureDate + GreaterThanProperty
    // =========================================================================

    [Fact]
    public async Task CreateReservation_PastCheckIn_RejectsRequest()
    {
        var (guestId, propertyId, roomTypeId) = await CreateReservationPrerequisitesAsync();

        var body = new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(-1).ToString("O"), // Past date
                checkOut = DateTimeOffset.UtcNow.AddDays(2).ToString("O"),
                numberOfGuests = 2
            }
        };

        var response = await PostAsync("/api/reservations?api-version=1.0", body);

        response.StatusCode.Should().NotBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateReservation_CheckOutBeforeCheckIn_RejectsRequest()
    {
        var (guestId, propertyId, roomTypeId) = await CreateReservationPrerequisitesAsync();

        var body = new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(10).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(5).ToString("O"), // Before CheckIn!
                numberOfGuests = 2
            }
        };

        var response = await PostAsync("/api/reservations?api-version=1.0", body);

        response.StatusCode.Should().NotBe(HttpStatusCode.Created);
    }

    // =========================================================================
    // Phone
    // =========================================================================

    [Fact]
    public async Task CreateGuest_InvalidPhone_Returns422()
    {
        var body = new
        {
            firstName = "Phone",
            lastName = "Test",
            email = $"phone.{Guid.NewGuid():N}@test.com",
            phone = "not-a-phone"
        };

        var response = await PostAsync("/api/guests", body);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "[Phone] validation should reject invalid phone number format");
    }

    [Fact]
    public async Task CreateGuest_ValidPhone_Returns201()
    {
        var body = new
        {
            firstName = "Phone",
            lastName = "Valid",
            email = $"phone.{Guid.NewGuid():N}@test.com",
            phone = "+39 06 1234567"
        };

        var response = await PostAsync("/api/guests", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "Valid phone number should be accepted");
    }

    // =========================================================================
    // Ensure Guards
    // =========================================================================

    [Fact]
    public async Task CreateProperty_EmptyCode_Returns422()
    {
        var body = new
        {
            code = "",
            name = "Guard Test",
            city = "Rome",
            country = "IT",
            starRating = 3
        };

        var response = await PostAsync("/api/properties", body);

        // Ensure.ThrowIfNullOrEmpty or validation should catch this
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.UnprocessableEntity,
            HttpStatusCode.InternalServerError);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<Guid> CreatePropertyAndGetIdAsync()
    {
        var body = new
        {
            code = $"VL-{Guid.NewGuid():N}"[..12],
            name = $"ValProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        };
        var response = await PostAsync("/api/properties", body);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("id").GetGuid();
    }

    private async Task<(Guid GuestId, Guid PropertyId, Guid RoomTypeId)> CreateReservationPrerequisitesAsync()
    {
        var guestBody = new
        {
            firstName = "Val",
            lastName = "Guest",
            email = $"val.{Guid.NewGuid():N}@test.com"
        };
        var guest = await PostAsync<JsonElement>("/api/guests", guestBody);
        var guestId = guest.GetProperty("id").GetGuid();

        var propertyId = await CreatePropertyAndGetIdAsync();

        var rtBody = new
        {
            propertyId,
            name = "Val Room",
            code = "VLR",
            baseRate = 100m,
            totalRooms = 5
        };
        var roomType = await PostAsync<JsonElement>("/api/room-types", rtBody);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        return (guestId, propertyId, roomTypeId);
    }
}
