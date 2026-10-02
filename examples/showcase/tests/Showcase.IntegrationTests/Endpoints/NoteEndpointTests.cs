using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
/// E2E tests for the auto-generated [HasNotes] trait endpoints on <c>Guest</c>:
/// POST → GET → list → PUT → DELETE, against a real database.
/// </summary>
/// <remarks>
///     A note lives in its own table whose EF configuration and migrations schema are generated
///     independently; only a real database proves the two agree (the key column is
///     <c>PersistenceId</c>, and the abstract <c>ParentEntityId</c> from <c>NoteBase</c> is not a
///     column at all).
/// </remarks>
public class NoteEndpointTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private static string NotesUrl(Guid guestId) =>
        $"/api/booking/guests/{guestId}/notes";

    private static string NoteUrl(Guid guestId, Guid noteId) =>
        $"/api/booking/guests/{guestId}/notes/{noteId}";

    [Fact]
    public async Task AddNote_ReturnsCreated_WithNoteId()
    {
        var guestId = await CreateGuest();

        var response = await PostAsync(NotesUrl(guestId), "Allergic to peanuts");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, "add-note response was: {0}", body);
        var id = await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
        id.Should().NotBeEmpty("the key must be value-generated, not left at Guid.Empty");
    }

    [Fact]
    public async Task AddNote_Twice_ProducesTwoDistinctRows()
    {
        // A key left ValueGeneratedNever hands both rows Guid.Empty: the first insert succeeds and
        // the second dies on the primary key. One note is not enough to catch that.
        var guestId = await CreateGuest();

        var first = await AddNote(guestId, "First note");
        var second = await AddNote(guestId, "Second note");

        second.Should().NotBe(first);

        var response = await GetRawAsync($"{NotesUrl(guestId)}?page=1&pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("totalCount").GetInt32().Should().Be(2);
        json.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("content").GetString())
            .Should().BeEquivalentTo("First note", "Second note");
    }

    [Fact]
    public async Task GetNote_AfterAdd_ReturnsContentAndAuthor()
    {
        var guestId = await CreateGuest();
        var noteId = await AddNote(guestId, "Prefers a high floor");

        var response = await GetRawAsync(NoteUrl(guestId, noteId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("id").GetGuid().Should().Be(noteId);
        json.GetProperty("guestId").GetGuid().Should().Be(guestId);
        json.GetProperty("content").GetString().Should().Be("Prefers a high floor");
        json.GetProperty("authorId").GetString().Should().Be("test-user");
        json.GetProperty("isEdited").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ListNotes_OtherGuestNotes_AreNotReturned()
    {
        var mine = await CreateGuest();
        var other = await CreateGuest();
        await AddNote(mine, "MineOnly");
        await AddNote(other, "TheirsOnly");

        var response = await GetRawAsync(NotesUrl(mine));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("content").GetString())
            .Should().BeEquivalentTo("MineOnly");
    }

    [Fact]
    public async Task UpdateNote_ChangesContent_SetsIsEdited()
    {
        var guestId = await CreateGuest();
        var noteId = await AddNote(guestId, "Original");

        var update = await PutAsync(NoteUrl(guestId, noteId), "Updated");
        update.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var json = await GetJson(NoteUrl(guestId, noteId));
        json.GetProperty("content").GetString().Should().Be("Updated");
        json.GetProperty("isEdited").GetBoolean().Should().BeTrue();
        json.GetProperty("updatedAt").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task DeleteNote_SoftDeletes_AndDisappearsFromReads()
    {
        var guestId = await CreateGuest();
        var noteId = await AddNote(guestId, "To be deleted");

        var delete = await DeleteAsync(NoteUrl(guestId, noteId));
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var get = await GetRawAsync(NoteUrl(guestId, noteId));
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var list = await GetJson(NotesUrl(guestId));
        list.GetProperty("totalCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ListNotes_WithoutReadPermission_Returns403()
    {
        var client = CreateClientWithPermissions("booking.guest.notes.create");

        var response = await client.GetAsync(NotesUrl(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> CreateGuest()
    {
        var body = new { firstName = "NT", lastName = "Guest", email = $"nt.{Guid.NewGuid():N}@test.com" };
        var guest = await PostAsync<JsonElement>("/api/guests", body);
        return guest.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddNote(Guid guestId, string content)
    {
        // The generated endpoint binds [FromBody] string: the body is a bare JSON string.
        var response = await PostAsync(NotesUrl(guestId), content);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, "add-note response was: {0}", body);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private async Task<JsonElement> GetJson(string url)
    {
        var response = await GetRawAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }
}
