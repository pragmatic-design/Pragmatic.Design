using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     Proves that <c>MaxPerEntity</c> holds when requests arrive together.
///     <para>
///     Counting and inserting are two statements. Under the default isolation two callers both read
///     "one below the limit" and both insert, so the cap would be crossed — the check saying one thing
///     and the table another. The add path runs serializable when a limit is
///     configured, so the database refuses one of the racing transactions instead of letting both in;
///     the loser gets a 409 telling it to retry, not a 500.
///     </para>
///     <para>
///     <c>Guest</c> carries <c>[HasTags(MaxPerEntity = 3)]</c> precisely so this is testable without
///     inserting fifty rows first. It is also the second <c>[HasTags]</c> entity in the Booking
///     boundary, which pins that the shared tag type is emitted once per boundary rather than once
///     per annotated entity.
///     </para>
/// </summary>
public class TagConcurrencyTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const int MaxTagsPerGuest = 3;

    private static string TagsUrl(Guid guestId) => $"/api/booking/guests/{guestId}/tags";

    [Fact]
    public async Task ConcurrentAdds_NeverExceedTheLimit()
    {
        var guestId = await CreateGuest();

        // Fill every slot but the last, so all the racing requests compete for the same one.
        await AddTag(guestId, "first");
        await AddTag(guestId, "second");

        var racers = Enumerable.Range(0, 6)
            .Select(i => Client.PostAsJsonAsync(TagsUrl(guestId), $"racer-{i}", JsonOptions))
            .ToArray();

        var responses = await Task.WhenAll(racers);

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var refused = responses.Count(r =>
            r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Conflict);

        created.Should().Be(1, "only one of the racers can take the last slot");
        refused.Should().Be(5, "the others must be refused, by the limit or by the conflict");
        responses.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError,
            "a lost race is a conflict the caller can retry, not a server fault");

        var list = await GetAsync<JsonElement>($"{TagsUrl(guestId)}?page=1&pageSize=20");
        list.GetProperty("totalCount").GetInt32().Should().Be(MaxTagsPerGuest,
            "the table must agree with the limit the check enforced");
    }

    [Fact]
    public async Task SequentialAdds_StopAtTheLimit()
    {
        var guestId = await CreateGuest();

        for (var i = 0; i < MaxTagsPerGuest; i++)
            await AddTag(guestId, $"tag-{i}");

        var overflow = await Client.PostAsJsonAsync(TagsUrl(guestId), "one-too-many", JsonOptions);

        overflow.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var list = await GetAsync<JsonElement>($"{TagsUrl(guestId)}?page=1&pageSize=20");
        list.GetProperty("totalCount").GetInt32().Should().Be(MaxTagsPerGuest);
    }

    [Fact]
    public async Task ReAddingAnExistingTagAtTheLimit_IsStillIdempotent()
    {
        var guestId = await CreateGuest();
        var firstTagId = await AddTag(guestId, "vip");
        await AddTag(guestId, "returning");
        await AddTag(guestId, "corporate");

        // At the limit, but this adds nothing: it must answer with the tag it already has.
        var again = await Client.PostAsJsonAsync(TagsUrl(guestId), "vip", JsonOptions);

        again.StatusCode.Should().Be(HttpStatusCode.Created);
        (await again.Content.ReadFromJsonAsync<Guid>(JsonOptions)).Should().Be(firstTagId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<Guid> CreateGuest()
    {
        var body = new { firstName = "TC", lastName = "Guest", email = $"tc.{Guid.NewGuid():N}@test.com" };
        var guest = await PostAsync<JsonElement>("/api/guests", body);
        return guest.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddTag(Guid guestId, string tagValue)
    {
        var response = await PostAsync(TagsUrl(guestId), tagValue);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, "add-tag response was: {0}", body);
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }
}
