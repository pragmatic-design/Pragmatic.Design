using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     A trait child's action reads the child with its parent's visibility, on EF Core as on the engines (#598): a caller who cannot see the
///     guest or the reservation is answered 404 for its note, attachment or tag, not given the row. Each test has its control: the same call
///     by a caller who sees every parent (<c>view-all</c>) succeeds, so a 404 for everyone fails it.
/// </summary>
/// <remarks>The comments, the trait the decision was measured on, are pinned in <see cref="CommentEndpointTests" /> and <see cref="CommentModerationTests" />.</remarks>
public class ATraitChildIsReadWithItsParentsVisibilityTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly byte[] FakePdf = "%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF"u8.ToArray();

    [Fact]
    public async Task A_note_on_a_guest_the_caller_cannot_see_is_not_found()
    {
        var guestId = await CreateGuest();
        var created = await PostAsync($"/api/booking/guests/{guestId}/notes", "Private note.");
        created.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await created.Content.ReadAsStringAsync());
        var noteId = await created.Content.ReadFromJsonAsync<Guid>(JsonOptions);
        var url = $"/api/booking/guests/{guestId}/notes/{noteId}";

        (await CreateClientWithPermissions("booking.guest.notes.read").GetAsync(url)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await CreateClientWithPermissions("booking.guest.notes.read", "booking.guest.view-all").GetAsync(url)).StatusCode
            .Should().Be(HttpStatusCode.OK, "the control: a caller who sees every guest reads the note");
    }

    [Fact]
    public async Task An_attachment_on_a_reservation_the_caller_cannot_see_is_not_found()
    {
        var reservationId = await CreateReservation();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(FakePdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "contract.pdf");
        var uploaded = await Client.PostAsync($"/api/booking/reservations/{reservationId}/attachments", form);
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await uploaded.Content.ReadAsStringAsync());
        var attachmentId = await uploaded.Content.ReadFromJsonAsync<Guid>(JsonOptions);
        var url = $"/api/booking/reservations/{reservationId}/attachments/{attachmentId}";

        (await CreateClientWithPermissions("booking.reservation.attachments.read").GetAsync(url)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await CreateClientWithPermissions("booking.reservation.attachments.read", "booking.reservation.view-all").GetAsync(url)).StatusCode
            .Should().Be(HttpStatusCode.OK, "the control: a caller who sees every reservation reads the attachment");
    }

    [Fact]
    public async Task A_tag_on_a_reservation_the_caller_cannot_see_is_not_found_for_its_removal()
    {
        var reservationId = await CreateReservation();
        var added = await PostAsync($"/api/booking/reservations/{reservationId}/tags", "Hidden");
        added.StatusCode.Should().Be(HttpStatusCode.Created, "Body: {0}", await added.Content.ReadAsStringAsync());
        var tagId = await added.Content.ReadFromJsonAsync<Guid>(JsonOptions);
        var url = $"/api/booking/reservations/{reservationId}/tags/{tagId}";

        (await CreateClientWithPermissions("booking.reservation.tags.remove").DeleteAsync(url)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await CreateClientWithPermissions("booking.reservation.tags.remove", "booking.reservation.view-all").DeleteAsync(url)).IsSuccessStatusCode
            .Should().BeTrue("the control: a caller who sees every reservation removes the tag");
    }

    private async Task<Guid> CreateGuest()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new { firstName = "Seen", lastName = "Guest", email = $"seen.{Guid.NewGuid():N}@test.com" });
        return guest.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateReservation()
    {
        var guestId = await CreateGuest();
        var property = await PostAsync<JsonElement>("/api/properties", new { code = $"VS-{Guid.NewGuid():N}"[..12], name = "Visibility Hotel", city = "Rome", country = "IT", starRating = 3 });
        var propertyId = property.GetProperty("id").GetGuid();
        var roomType = await PostAsync<JsonElement>("/api/room-types", new { propertyId, name = "VS Room", code = "VSR", baseRate = 100m, totalRooms = 5 });
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
