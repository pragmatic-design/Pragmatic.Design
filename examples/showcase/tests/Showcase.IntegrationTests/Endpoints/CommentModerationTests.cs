using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     E2E tests for <c>[HasComments(RequireApproval = true)]</c> on <c>Guest</c>.
///     <para>
///     Moderation has to be more than decorative. A new comment is created as <c>PendingApproval</c>,
///     and a query filter that excluded only <c>Rejected</c> would leave a comment awaiting approval
///     readable by anyone — the opposite of what the attribute promises. The moderate action needs an
///     endpoint, or nothing could ever be approved over HTTP, and it must see past the global filter,
///     or a rejected comment would be hidden from the moderate action itself.
///     </para>
///     <para>
///     <c>Reservation</c> keeps the unmoderated variant, so both configurations run side by side.
///     </para>
/// </summary>
public class CommentModerationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private static string CommentsUrl(Guid guestId) =>
        $"/api/booking/guests/{guestId}/comments";

    private static string ModerationUrl(Guid guestId, Guid commentId) =>
        $"/api/booking/guests/{guestId}/comments/{commentId}/moderation";

    [Fact]
    public async Task AddComment_WhenApprovalIsRequired_IsNotReadableUntilApproved()
    {
        var guestId = await CreateGuest();
        await AddComment(guestId, "Is this hotel pet friendly?");

        var list = await GetAsync<JsonElement>(CommentsUrl(guestId));

        list.GetProperty("items").GetArrayLength().Should().Be(0,
            "a comment awaiting approval must not be readable — that is what RequireApproval means");
    }

    [Fact]
    public async Task ApproveComment_MakesItReadable()
    {
        var guestId = await CreateGuest();
        var commentId = await AddComment(guestId, "Lovely staff, thank you.");

        var moderation = await PutAsync(ModerationUrl(guestId, commentId), "Visible");
        moderation.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await GetAsync<JsonElement>(CommentsUrl(guestId));
        list.GetProperty("items").GetArrayLength().Should().Be(1);
        list.GetProperty("items")[0].GetProperty("content").GetString()
            .Should().Be("Lovely staff, thank you.");
    }

    [Fact]
    public async Task RejectedComment_CanStillBeReinstated()
    {
        var guestId = await CreateGuest();
        var commentId = await AddComment(guestId, "Borderline content.");

        (await PutAsync(ModerationUrl(guestId, commentId), "Rejected"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The rejected comment is hidden by the global filter; the moderate action must still reach it.
        var reinstate = await PutAsync(ModerationUrl(guestId, commentId), "Visible");

        reinstate.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "a moderator must be able to undo a rejection — the action bypasses the query filter");

        var list = await GetAsync<JsonElement>(CommentsUrl(guestId));
        list.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task HiddenComment_DisappearsFromReads()
    {
        var guestId = await CreateGuest();
        var commentId = await AddComment(guestId, "Visible for now.");
        await PutAsync(ModerationUrl(guestId, commentId), "Visible");

        await PutAsync(ModerationUrl(guestId, commentId), "Hidden");

        var list = await GetAsync<JsonElement>(CommentsUrl(guestId));
        list.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ModerateComment_WithoutModeratePermission_Returns403()
    {
        var guestId = await CreateGuest();
        var commentId = await AddComment(guestId, "Needs a moderator.");

        var client = CreateClientWithPermissions(
            "booking.guest.comments.create", "booking.guest.comments.read");

        var response = await client.PutAsJsonAsync(
            ModerationUrl(guestId, commentId), "Visible");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "moderation is gated on booking.guest.comments.moderate, which this client does not hold");
    }

    /// <summary>
    ///     The moderation queue: what awaits approval, and only that. The filter that hides a pending
    ///     comment from readers hid it from the moderator too, so the approve route existed and nothing
    ///     listed what to approve.
    /// </summary>
    [Fact]
    public async Task ThePendingQueue_ListsWhatAwaitsApproval()
    {
        var guestId = await CreateGuest();
        var approved = await AddComment(guestId, "Already fine.");
        await PutAsync(ModerationUrl(guestId, approved), "Visible");
        var pending = await AddComment(guestId, "Waiting for a moderator.");

        var queue = await GetAsync<JsonElement>(PendingUrl(guestId));

        queue.GetArrayLength().Should().Be(1);
        queue[0].GetProperty("id").GetGuid().Should().Be(pending);
    }

    [Fact]
    public async Task AnApprovedComment_LeavesTheQueue()
    {
        var guestId = await CreateGuest();
        var commentId = await AddComment(guestId, "Approve me.");
        await PutAsync(ModerationUrl(guestId, commentId), "Visible");

        var queue = await GetAsync<JsonElement>(PendingUrl(guestId));

        queue.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ThePendingQueue_WithoutModeratePermission_Returns403()
    {
        var guestId = await CreateGuest();
        await AddComment(guestId, "Not for readers.");

        var client = CreateClientWithPermissions("booking.guest.comments.read");
        var response = await client.GetAsync(PendingUrl(guestId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the queue shows what readers are not allowed to see, so it is a moderator's");
    }

    /// <summary>
    ///     The queue reads the table directly, past the repository's row filters, so the internal-visibility
    ///     rule is applied in the action: a moderator without <c>view-internal</c> does not see a pending
    ///     internal note, one with it does.
    /// </summary>
    [Fact]
    public async Task APendingInternalNote_IsQueuedOnlyForWhoCanViewInternal()
    {
        var guestId = await CreateGuest();
        var response = await PostAsync(CommentsUrl(guestId), new { content = "Staff only.", visibility = "Internal" });
        response.EnsureSuccessStatusCode();

        var moderator = CreateClientWithPermissions("booking.guest.comments.moderate");
        var staff = CreateClientWithPermissions(
            "booking.guest.comments.moderate", "booking.guest.comments.view-internal");

        var forModerator = await moderator.GetFromJsonAsync<JsonElement>(PendingUrl(guestId), JsonOptions);
        var forStaff = await staff.GetFromJsonAsync<JsonElement>(PendingUrl(guestId), JsonOptions);

        forModerator.GetArrayLength().Should().Be(0);
        forStaff.GetArrayLength().Should().Be(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string PendingUrl(Guid guestId) =>
        $"/api/booking/guests/{guestId}/comments/pending";

    private async Task<Guid> CreateGuest()
    {
        var body = new { firstName = "Mod", lastName = "Guest", email = $"mod.{Guid.NewGuid():N}@test.com" };
        var guest = await PostAsync<JsonElement>("/api/guests", body);
        return guest.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddComment(Guid guestId, string content)
    {
        var response = await PostAsync(CommentsUrl(guestId), new { content });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
