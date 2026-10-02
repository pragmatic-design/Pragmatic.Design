using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     <c>[HasComments(SupportInternalNotes = true)]</c> on <c>Guest</c>: a comment marked
///     <c>Internal</c> is for staff, and only a caller holding <c>booking.guest.comments.view-internal</c>
///     reads it — or writes one.
/// </summary>
/// <remarks>
///     The attribute promises this, and three things have to hold for it: the permission is generated,
///     the list looks at the visibility, and marking a comment internal requires the permission. If any of
///     them fails, a guest reading their own thread sees the staff's notes about them.
/// </remarks>
public class InternalCommentsTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Read = "booking.guest.comments.read";
    private const string Create = "booking.guest.comments.create";
    private const string ViewInternal = "booking.guest.comments.view-internal";

    // Every reader here may see the guest itself: the thread of a guest one cannot see is hidden by the
    // parent's own filter, and a test without this would pass on that instead of on the visibility.
    private const string SeeTheGuest = "booking.guest.view-all";

    private static string CommentsUrl(Guid guestId) => $"/api/booking/guests/{guestId}/comments";

    [Fact]
    public async Task AnInternalComment_IsNotListedForACallerWithoutViewInternal()
    {
        var guestId = await CreateGuest();
        await AddApprovedComment(guestId, "Asked twice for a refund, be careful.", "Internal");

        var reader = CreateClientWithPermissions(Read, SeeTheGuest);
        var list = await reader.GetFromJsonAsync<JsonElement>(CommentsUrl(guestId), JsonOptions);

        list.GetProperty("items").GetArrayLength().Should().Be(0,
            "an internal note is for whoever holds view-internal, not for every reader of the thread");
    }

    /// <summary>The control: the same note, read by someone who may read it.</summary>
    [Fact]
    public async Task AnInternalComment_IsListedForACallerWithViewInternal()
    {
        var guestId = await CreateGuest();
        await AddApprovedComment(guestId, "Prefers a quiet room.", "Internal");

        var staff = CreateClientWithPermissions(Read, SeeTheGuest, ViewInternal);
        var list = await staff.GetFromJsonAsync<JsonElement>(CommentsUrl(guestId), JsonOptions);

        list.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task AnInternalComment_IsNotFoundById_ForACallerWithoutViewInternal()
    {
        var guestId = await CreateGuest();
        var commentId = await AddApprovedComment(guestId, "Flagged by the front desk.", "Internal");

        var reader = CreateClientWithPermissions(Read, SeeTheGuest);
        var response = await reader.GetAsync($"{CommentsUrl(guestId)}/{commentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "reading it by id must not be the way around the list");
    }

    [Fact]
    public async Task WritingAnInternalComment_WithoutViewInternal_IsForbidden()
    {
        var guestId = await CreateGuest();

        var author = CreateClientWithPermissions(Create, Read);
        var response = await author.PostAsJsonAsync(
            CommentsUrl(guestId), new { content = "I am staff, trust me.", visibility = "Internal" }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>The control: a public comment needs nothing more than the create permission.</summary>
    [Fact]
    public async Task WritingAPublicComment_NeedsOnlyCreate()
    {
        var guestId = await CreateGuest();

        var author = CreateClientWithPermissions(Create, Read);
        var response = await author.PostAsJsonAsync(
            CommentsUrl(guestId), new { content = "Lovely stay." }, JsonOptions);

        response.EnsureSuccessStatusCode();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> CreateGuest()
    {
        var body = new { firstName = "Internal", lastName = "Guest", email = $"internal.{Guid.NewGuid():N}@test.com" };
        var guest = await PostAsync<JsonElement>("/api/guests", body);
        return guest.GetProperty("id").GetGuid();
    }

    /// <summary>Written and approved by the default client, which holds every booking permission.</summary>
    private async Task<Guid> AddApprovedComment(Guid guestId, string content, string visibility)
    {
        var response = await PostAsync(CommentsUrl(guestId), new { content, visibility });
        response.EnsureSuccessStatusCode();
        var commentId = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        (await PutAsync($"{CommentsUrl(guestId)}/{commentId}/moderation", "Visible"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        return commentId;
    }
}
