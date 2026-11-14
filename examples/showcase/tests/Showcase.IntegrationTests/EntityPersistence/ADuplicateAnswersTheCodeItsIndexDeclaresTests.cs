using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     A duplicate answers the code its unique index declares (#728): <c>RoomType</c>'s <c>[Unique("PropertyId", "Code",
///     ErrorCode = "ROOM_TYPE_CODE_TAKEN")]</c>, over HTTP, on the three engines (the suite is linked into the Sql and the documents one).
/// </summary>
/// <remarks>
///     Before #728 every duplicate was the generic <c>DB_CONFLICT</c>, whatever the index, so a client could tell a code taken from any
///     other conflict only by reading the field names. The generic code without a declaration is measured where an index declares none
///     (TimeOff's <c>RegisteringEmployees</c>, and the Sql and documents engines' <c>UnitOfWorkRuleTests</c>).
/// </remarks>
public class ADuplicateAnswersTheCodeItsIndexDeclaresTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ASecondRoomTypeOfTheSameCodeInAProperty_AnswersTheDeclaredCode()
    {
        var propertyId = await APropertyAsync();
        (await CreateRoomTypeAsync(propertyId, "DBL")).StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await CreateRoomTypeAsync(propertyId, "DBL");

        var body = await duplicate.Content.ReadAsStringAsync();
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().Should().Be("ROOM_TYPE_CODE_TAKEN");
    }

    /// <summary>The control: the same code in another property is not a duplicate, and is created.</summary>
    [Fact]
    public async Task TheSameCodeInAnotherProperty_IsCreated()
    {
        (await CreateRoomTypeAsync(await APropertyAsync(), "DBL")).StatusCode.Should().Be(HttpStatusCode.Created);

        (await CreateRoomTypeAsync(await APropertyAsync(), "DBL")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<Guid> APropertyAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/properties", new
        {
            code = $"UQ-{Guid.NewGuid():N}"[..12],
            name = $"UqProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 3
        }, JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> CreateRoomTypeAsync(Guid propertyId, string code)
        => Client.PostAsJsonAsync("/api/room-types", new
        {
            propertyId,
            name = "Double",
            code,
            baseRate = 100m,
            totalRooms = 5
        }, JsonOptions);
}
