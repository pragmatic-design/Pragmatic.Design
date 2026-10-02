using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     The scaffolded soft Delete and Restore, over HTTP.
/// </summary>
/// <remarks>
///     <para>
///         This executes the branch that answers 200 with the marked row — as opposed to the 204 a hard
///         delete answers — which needs a soft-deletable entity that is also a scaffolded resource.
///         <c>RoomType</c> is that entity, with Read, Delete and Restore only: create and update stay
///         hand-written on <c>/api/room-types</c>, and the scaffolding fills the rest.
///     </para>
///     <para>
///         Read is in the capability set because the two writes answer with the read shape, and that
///         shape is generated only when Read is asked for.
///     </para>
/// </remarks>
public class ResourceSoftDeleteEndpointTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string ScaffoldedUrl = "/api/catalog/room-types";

    /// <summary>
    ///     A soft delete answers 200 with the row it marked — it still exists, so it can still be described.
    /// </summary>
    [Fact]
    public async Task ResourceDelete_SoftDeletable_Returns200WithTheRow()
    {
        var id = await CreateRoomType();

        var response = await DeleteAsync($"{ScaffoldedUrl}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a soft delete leaves the resource there — 204 is the answer of a delete that removes it");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("id").GetGuid().Should().Be(id);
    }

    /// <summary>
    ///     And the row is then hidden from the read.
    /// </summary>
    [Fact]
    public async Task AfterASoftDelete_TheRowIsNoLongerRead()
    {
        var id = await CreateRoomType();
        await DeleteAsync($"{ScaffoldedUrl}/{id}");

        var response = await GetRawAsync($"{ScaffoldedUrl}/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    ///     Restore brings it back, and answers with it.
    /// </summary>
    /// <remarks>
    ///     The load behind it has to bypass the soft-delete filter to find the row at all, while keeping
    ///     the tenant filter on — which is the reason the invoker disables one named filter rather than
    ///     calling IgnoreQueryFilters.
    /// </remarks>
    [Fact]
    public async Task ResourceRestore_BringsTheRowBack()
    {
        var id = await CreateRoomType();
        await DeleteAsync($"{ScaffoldedUrl}/{id}");

        var response = await PostAsync($"{ScaffoldedUrl}/{id}/restore", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("id").GetGuid().Should().Be(id);

        var readBack = await GetRawAsync($"{ScaffoldedUrl}/{id}");
        readBack.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateRoomType()
    {
        var propertyBody = new
        {
            code = $"SD-{Guid.NewGuid():N}".Substring(0, 12),
            name = "Soft Delete Hotel",
            city = "Bologna",
            country = "IT",
            starRating = 3,
        };
        var propertyResponse = await PostAsync("/api/properties", propertyBody);
        var property = await propertyResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        var roomTypeBody = new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "Restorable Suite",
            code = $"R{Guid.NewGuid():N}".Substring(0, 6),
            maxOccupancy = 2,
            baseRate = 120.00m,
            currency = "EUR",
            totalRooms = 4,
        };
        var response = await PostAsync("/api/room-types", roomTypeBody);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return created.GetProperty("id").GetGuid();
    }
}
