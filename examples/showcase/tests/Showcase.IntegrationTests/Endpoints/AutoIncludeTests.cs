using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     A write whose response shape reaches through a navigation gets that navigation loaded for it.
/// </summary>
/// <remarks>
///     <para>
///         <c>UpdateRoomTypeMutation</c> declares <c>[ReturnsDto&lt;RoomTypeSummaryDto&gt;]</c> and
///         nothing else. That DTO flattens <c>Property.Name</c>, so the generator adds
///         <c>Include("Property")</c> to the load on its own — the mutation says no
///         <c>[EagerLoad]</c> anywhere.
///     </para>
///     <para>
///         Without it the generated <c>FromEntity</c> walks <c>entity.Property.Name</c> on an entity
///         loaded by id, whose navigations EF never populated. That is not a hypothetical: the same
///         shape answered 500 on <c>POST /api/room-types</c> before any of this existed.
///     </para>
/// </remarks>
public class AutoIncludeTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AnUpdate_WhoseResponseFlattensANavigation_LoadsIt()
    {
        var propertyName = $"AutoInc {Guid.NewGuid():N}"[..20];
        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"AI-{Guid.NewGuid():N}"[..12],
            name = propertyName,
            city = "Rome",
            country = "IT",
            starRating = 4,
        });

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId = property.GetProperty("id").GetGuid(),
            name = "Auto Include Suite",
            code = $"AI{Guid.NewGuid():N}"[..6],
            baseRate = 250m,
            totalRooms = 3,
        });

        var response = await PutAsync($"/api/room-types/{roomType.GetProperty("id").GetGuid()}",
            new { name = "Auto Include Suite Renamed" });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the update answered {0}", await response.Content.ReadAsStringAsync());

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        updated.GetProperty("name").GetString().Should().Be("Auto Include Suite Renamed");
        updated.GetProperty("propertyName").GetString().Should().Be(propertyName,
            "PropertyName is flattened from Property.Name, so the navigation had to be loaded — "
            + "and nothing on the mutation asks for it");
    }
}
