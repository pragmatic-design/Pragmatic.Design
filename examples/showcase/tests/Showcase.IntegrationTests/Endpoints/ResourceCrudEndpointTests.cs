using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
/// E2E tests for auto-generated [Resource(Capabilities=All)] CRUD endpoints on Guest.
/// Routes: /api/booking/guests
/// </summary>
public class ResourceCrudEndpointTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string GuestsUrl = "/api/booking/guests";

    [Fact]
    public async Task ResourceCreate_Guest_Returns201()
    {
        var body = new { firstName = "Resource", lastName = "Test", email = $"rc.{Guid.NewGuid():N}@test.com", preferredLanguage = "en" };

        var response = await PostAsync(GuestsUrl, body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ResourceRead_Guest_ReturnsDto()
    {
        var id = await CreateGuest();

        var response = await GetRawAsync($"{GuestsUrl}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("firstName").GetString().Should().Be("CRUD");
    }

    /// <summary>
    ///     An update answers 200 with the resource it left behind.
    /// </summary>
    /// <remarks>
    ///     A 204 here would not be a decision: a create answering with the entity and an update with
    ///     nothing is only two defaults happening to meet. The rule is that a write answers with the
    ///     read shape while the resource still exists, and 204 once it does not —
    ///     so a caller never has to issue a GET to find out what they just wrote.
    /// </remarks>
    [Fact]
    public async Task ResourceUpdate_Guest_Returns200WithTheReadShape()
    {
        var id = await CreateGuest();

        var updateResponse = await PutAsync($"{GuestsUrl}/{id}",
            new { firstName = "Updated" });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        updated.GetProperty("firstName").GetString().Should().Be("Updated",
            "the body is the resource as it now stands, so no follow-up GET is needed");
        updated.GetProperty("id").GetGuid().Should().Be(id);

        var getResponse = await GetRawAsync($"{GuestsUrl}/{id}");
        var json = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("firstName").GetString().Should().Be("Updated");
    }

    /// <summary>
    ///     A delete that removes the row answers 204: there is nothing left to describe.
    /// </summary>
    /// <remarks>
    ///     Guest has no <c>[SoftDelete]</c>, so this is the hard case. The soft one answers 200 with the
    ///     marked row, and is now executed rather than reasoned about — see
    ///     <see cref="ResourceSoftDeleteEndpointTests"/> on RoomType.
    /// </remarks>
    [Fact]
    public async Task ResourceDelete_Guest_Returns204()
    {
        var id = await CreateGuest();

        var deleteResponse = await DeleteAsync($"{GuestsUrl}/{id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ResourceList_Guests_ReturnsPaged()
    {
        await CreateGuest();
        await CreateGuest();

        var response = await GetRawAsync($"{GuestsUrl}?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    ///     The scaffolded write requires <c>booking.guest.create</c>, and a caller without it is refused.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The other tests here run as a caller that holds everything, so they pass whether the
    ///         permission is enforced or not. That is exactly how the first version of this shipped: the
    ///         default was set on the <c>MutationModel</c>, the endpoint reads its own
    ///         <c>AuthorizationModel</c>, and the generated handler was mapped with no authorization at
    ///         all — five green tests over an endpoint anyone authenticated could call.
    ///     </para>
    ///     <para>
    ///         Before this, <c>[Resource]</c> scaffolded <c>DomainAction</c>s that required nothing by
    ///         design, so this is the behaviour changing, not a regression being caught.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ResourceCreate_WithoutThePermission_IsRefused()
    {
        var client = CreateClientWithPermissions("booking.guest.read");
        var body = new
        {
            firstName = "Denied", lastName = "Guest",
            email = $"denied.{Guid.NewGuid():N}@test.com", preferredLanguage = "en",
        };

        var response = await PostWithClientAsync(client, GuestsUrl, body);

        response.StatusCode.Should().BeOneOf([HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized],
            "the scaffolded create is fail-closed: reading a guest does not grant creating one");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    ///     Creates a guest and returns its id, read the way every mutation endpoint answers.
    /// </summary>
    /// <remarks>
    ///     A mutation endpoint returns the entity, so the id is a property of the response object —
    ///     for the scaffolded CRUD as for every other write endpoint of the framework.
    /// </remarks>
    private async Task<Guid> CreateGuest()
    {
        var body = new { firstName = "CRUD", lastName = "Guest", email = $"crud.{Guid.NewGuid():N}@test.com", preferredLanguage = "en" };
        var response = await PostAsync(GuestsUrl, body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return json.GetProperty("id").GetGuid();
    }
}
