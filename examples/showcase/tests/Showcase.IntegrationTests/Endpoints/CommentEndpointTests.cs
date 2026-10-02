using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
/// E2E tests for auto-generated [HasComments] trait endpoints.
/// Validates the full HTTP cycle: POST → GET → PUT → GET → DELETE → verify deleted.
/// </summary>
public class CommentEndpointTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string ReservationsUrl = "/api/reservations";

    private static string CommentsUrl(Guid reservationId) =>
        $"/api/booking/reservations/{reservationId}/comments";

    private static string CommentUrl(Guid reservationId, Guid commentId) =>
        $"/api/booking/reservations/{reservationId}/comments/{commentId}";

    [Fact]
    public async Task AddComment_ReturnsCreated_WithCommentId()
    {
        var reservationId = await CreateReservation();

        var response = await PostAsync(CommentsUrl(reservationId),
            new { content = "Great hotel!" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
        id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetComment_AfterAdd_ReturnsCorrectContent()
    {
        var reservationId = await CreateReservation();
        var commentId = await AddComment(reservationId, "Test content");

        var response = await GetRawAsync(CommentUrl(reservationId, commentId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("content").GetString().Should().Be("Test content");
        json.GetProperty("authorId").GetString().Should().Be("test-user");
    }

    [Fact]
    public async Task ListComments_AfterMultipleAdds_ReturnsPaged()
    {
        var reservationId = await CreateReservation();
        await AddComment(reservationId, "Comment 1");
        await AddComment(reservationId, "Comment 2");
        await AddComment(reservationId, "Comment 3");

        var response = await GetRawAsync($"{CommentsUrl(reservationId)}?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateComment_ChangesContent_SetsIsEdited()
    {
        var reservationId = await CreateReservation();
        var commentId = await AddComment(reservationId, "Original");

        var updateResponse = await PutAsync(CommentUrl(reservationId, commentId),
            "Updated content");
        updateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the update
        var getResponse = await GetRawAsync(CommentUrl(reservationId, commentId));
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("content").GetString().Should().Be("Updated content");
        json.GetProperty("isEdited").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task DeleteComment_SoftDeletes_ReturnsNoContent()
    {
        var reservationId = await CreateReservation();
        var commentId = await AddComment(reservationId, "To be deleted");

        var deleteResponse = await DeleteAsync(CommentUrl(reservationId, commentId));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify soft-deleted (GET should return 404 due to query filter)
        var getResponse = await GetRawAsync(CommentUrl(reservationId, commentId));
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddComment_WithReply_SetsReplyToId()
    {
        var reservationId = await CreateReservation();
        var parentCommentId = await AddComment(reservationId, "Parent comment");

        var response = await PostAsync(CommentsUrl(reservationId),
            new { content = "Reply to parent", replyToId = parentCommentId });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var replyId = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        var getResponse = await GetRawAsync(CommentUrl(reservationId, replyId));
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("replyToId").GetGuid().Should().Be(parentCommentId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> CreateReservation()
    {
        var guestBody = new { firstName = "CT", lastName = "Guest", email = $"ct.{Guid.NewGuid():N}@test.com" };
        var guest = await PostAsync<JsonElement>("/api/guests", guestBody);
        var guestId = guest.GetProperty("id").GetGuid();

        var propBody = new { code = $"CT-{Guid.NewGuid():N}"[..12], name = "CommentProp", city = "Rome", country = "IT", starRating = 3 };
        var prop = await PostAsync("/api/properties", propBody);
        prop.EnsureSuccessStatusCode();
        var propJson = await prop.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var propertyId = propJson.GetProperty("id").GetGuid();

        var rtBody = new { propertyId, name = "CT Room", code = "CTR", baseRate = 100m, totalRooms = 5 };
        var roomType = await PostAsync<JsonElement>("/api/room-types", rtBody);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        var reservationBody = new
        {
            request = new
            {
                guestId, propertyId, roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 2
            }
        };
        var response = await PostAsync($"{ReservationsUrl}?api-version=1.0", reservationBody);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private async Task<Guid> AddComment(Guid reservationId, string content)
    {
        var response = await PostAsync(CommentsUrl(reservationId), new { content });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    /// <summary>
    ///     Comments were the one trait with no denial coverage: every test ran with the broad
    ///     <c>booking.*</c> grant, so nothing proved the endpoints refuse a caller without the
    ///     matching permission.
    /// </summary>
    [Fact]
    public async Task ListComments_WithoutReadPermission_Returns403()
    {
        var client = CreateClientWithPermissions("booking.reservation.comments.create");

        var response = await client.GetAsync(CommentsUrl(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddComment_WithoutCreatePermission_Returns403()
    {
        var client = CreateClientWithPermissions("booking.reservation.comments.read");

        var response = await client.PostAsJsonAsync(CommentsUrl(Guid.NewGuid()), "Not allowed");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteComment_ByAnotherUser_Returns403()
    {
        var reservationId = await CreateReservation();
        var commentId = await AddComment(reservationId, "Written by the default test user");

        // A different user, holding the delete permission but neither the authorship nor moderate.
        var other = CreateClientWithPermissions(
            "booking.reservation.comments.read", "booking.reservation.comments.delete");

        var response = await other.DeleteAsync(CommentUrl(reservationId, commentId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the delete permission alone does not authorise removing someone else's comment");
    }
}
