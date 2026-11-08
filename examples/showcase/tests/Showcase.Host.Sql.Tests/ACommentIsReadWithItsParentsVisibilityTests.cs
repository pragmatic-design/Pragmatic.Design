using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Showcase.Host.Sql.Tests;

/// <summary>
///     A comment is read with its parent's visibility, on the Sql engine as on EF Core (#598): deleting the comment of a reservation the caller
///     cannot see answers 404, so the caller does not learn it exists. The control is the author rule, for a caller who sees every
///     reservation: 403, which only a comment the caller reached can answer.
/// </summary>
[Collection(SqlHostCollection.Name)]
public sealed class ACommentIsReadWithItsParentsVisibilityTests(SqlHostFixture fixture)
{
    [Fact]
    public async Task Deleting_the_comment_of_a_reservation_the_caller_cannot_see_is_not_found()
    {
        var (reservationId, commentId) = await ReservationWithAComment();
        var url = new Uri($"/api/booking/reservations/{reservationId}/comments/{commentId}", UriKind.Relative);

        var blind = await Caller("booking.reservation.comments.read,booking.reservation.comments.delete").DeleteAsync(url);
        var seeing = await Caller("booking.reservation.comments.read,booking.reservation.comments.delete,booking.reservation.view-all").DeleteAsync(url);

        blind.StatusCode.Should().Be(HttpStatusCode.NotFound, "Body: {0}", await blind.Content.ReadAsStringAsync());
        seeing.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the control: the comment is reached, and it is not theirs. Body: {0}", await seeing.Content.ReadAsStringAsync());
    }

    /// <summary>A reservation and a comment on it, both by the host's owner of record (<c>engine-user</c>).</summary>
    private async Task<(Guid Reservation, Guid Comment)> ReservationWithAComment()
    {
        var owner = Caller("*", "engine-user");
        var guest = await owner.PostAsJsonAsync("/api/guests", new { firstName = "Seen", lastName = "Guest", email = $"seen.{Guid.NewGuid():N}@test.com" });
        var guestId = (await guest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var property = await owner.PostAsJsonAsync("/api/properties", new { code = $"VS-{Guid.NewGuid():N}"[..12], name = "Visibility Hotel", city = "Rome", country = "IT", starRating = 3 });
        var propertyId = (await property.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var roomType = await owner.PostAsJsonAsync("/api/room-types", new { propertyId, name = "VS Room", code = "VSR", baseRate = 100m, totalRooms = 5 });
        var roomTypeId = (await roomType.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var reservation = await owner.PostAsJsonAsync("/api/reservations?api-version=1.0", new
        {
            request = new { guestId, propertyId, roomTypeId, checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"), checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"), numberOfGuests = 2 },
        });
        reservation.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await reservation.Content.ReadAsStringAsync());
        var reservationId = await reservation.Content.ReadFromJsonAsync<Guid>();

        var comment = await owner.PostAsJsonAsync($"/api/booking/reservations/{reservationId}/comments", new { content = "Written by the owner" });
        comment.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await comment.Content.ReadAsStringAsync());
        return (reservationId, await comment.Content.ReadFromJsonAsync<Guid>());
    }

    /// <summary>A caller with <paramref name="permissions" />, as <paramref name="userId" />: by default a user of their own, who owns nothing.</summary>
    private HttpClient Caller(string permissions, string? userId = null)
    {
        var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "engine-tenant");
        client.DefaultRequestHeaders.Add("X-User-Id", userId ?? $"other-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add("X-User-Name", "Sql host test");
        client.DefaultRequestHeaders.Add("X-User-Permissions", permissions);
        return client;
    }
}
