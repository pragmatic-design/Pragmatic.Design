using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     A trait child is created only on a parent the caller sees: a caller who cannot see the guest or the reservation is answered 404 when
///     adding a note, a comment or a tag or uploading an attachment, and nothing is written. The reads were filtered by the parent already
///     (<see cref="ATraitChildIsReadWithItsParentsVisibilityTests" />); the creation took the route's id as it came. Each test has its control: the
///     same call by a caller who sees every parent (<c>view-all</c>) succeeds, so a 404 for everyone fails it.
/// </summary>
public class ATraitChildIsWrittenOnlyOnAParentTheCallerSeesTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly byte[] FakePdf = "%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF"u8.ToArray();

    [Fact]
    public async Task A_note_on_a_guest_the_caller_cannot_see_is_not_added()
    {
        var url = $"/api/booking/guests/{await CreateGuest()}/notes";

        (await CreateClientWithPermissions("booking.guest.notes.create").PostAsJsonAsync(url, "Written past the owner.", JsonOptions)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await CreateClientWithPermissions("booking.guest.notes.create", "booking.guest.view-all").PostAsJsonAsync(url, "Seen.", JsonOptions)).StatusCode
            .Should().Be(HttpStatusCode.Created, "the control: a caller who sees every guest adds the note");
    }

    [Fact]
    public async Task A_comment_on_a_reservation_the_caller_cannot_see_is_not_added()
    {
        var url = $"/api/booking/reservations/{await CreateReservation()}/comments";

        (await CreateClientWithPermissions("booking.reservation.comments.create").PostAsJsonAsync(url, new { content = "Written past the owner." }, JsonOptions)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await CreateClientWithPermissions("booking.reservation.comments.create", "booking.reservation.view-all").PostAsJsonAsync(url, new { content = "Seen." }, JsonOptions)).StatusCode
            .Should().Be(HttpStatusCode.Created, "the control: a caller who sees every reservation adds the comment");
    }

    [Fact]
    public async Task A_tag_on_a_reservation_the_caller_cannot_see_is_not_added()
    {
        var url = $"/api/booking/reservations/{await CreateReservation()}/tags";

        (await CreateClientWithPermissions("booking.reservation.tags.add").PostAsJsonAsync(url, "past-the-owner", JsonOptions)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await CreateClientWithPermissions("booking.reservation.tags.add", "booking.reservation.view-all").PostAsJsonAsync(url, "seen", JsonOptions)).StatusCode
            .Should().Be(HttpStatusCode.Created, "the control: a caller who sees every reservation adds the tag");
    }

    [Fact]
    public async Task An_attachment_on_a_reservation_the_caller_cannot_see_is_not_uploaded()
    {
        var url = $"/api/booking/reservations/{await CreateReservation()}/attachments";

        using (var form = Upload())
            (await CreateClientWithPermissions("booking.reservation.attachments.upload").PostAsync(url, form)).StatusCode
                .Should().Be(HttpStatusCode.NotFound);
        using (var form = Upload())
            (await CreateClientWithPermissions("booking.reservation.attachments.upload", "booking.reservation.view-all").PostAsync(url, form)).StatusCode
                .Should().Be(HttpStatusCode.Created, "the control: a caller who sees every reservation uploads the file");
    }

    private static MultipartFormDataContent Upload()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(FakePdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "contract.pdf");
        return form;
    }

    private async Task<Guid> CreateGuest()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new { firstName = "Seen", lastName = "Guest", email = $"seen.{Guid.NewGuid():N}@test.com" });
        return guest.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateReservation()
    {
        var guestId = await CreateGuest();
        var property = await PostAsync<JsonElement>("/api/properties", new { code = $"VW-{Guid.NewGuid():N}"[..12], name = "Visibility Hotel", city = "Rome", country = "IT", starRating = 3 });
        var propertyId = property.GetProperty("id").GetGuid();
        var roomType = await PostAsync<JsonElement>("/api/room-types", new { propertyId, name = "VW Room", code = "VWR", baseRate = 100m, totalRooms = 5 });
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        var response = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId, propertyId, roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 2,
            },
        });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
