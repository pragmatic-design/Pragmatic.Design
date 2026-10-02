using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;
using Xunit;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     [TemporalRelation&lt;Property&gt;(MaxActive = 1)] on StaffAssignment is enforced by the
///     mutation pipeline (CheckTemporalConstraints), not left to an opt-in helper. A second
///     active assignment for the same property is rejected automatically.
/// </summary>
public class TemporalEnforcementTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateStaffAssignment_SecondActiveForSameProperty_RejectedByMaxActive()
    {
        var propertyId = await CreatePropertyAsync();

        var first = await Client.PostAsJsonAsync("/api/staff-assignments", new
        {
            staffId = Guid.NewGuid(),
            propertyId,
            role = "Manager"
        }, JsonOptions);
        // The first active assignment for the property is allowed.
        first.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var second = await Client.PostAsJsonAsync("/api/staff-assignments", new
        {
            staffId = Guid.NewGuid(),
            propertyId,
            role = "Manager"
        }, JsonOptions);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "MaxActive = 1 per property is enforced by the pipeline — a second active assignment is rejected (409)");
    }

    [Fact]
    public async Task CreateStaffAssignment_DifferentProperties_BothAllowed()
    {
        var propertyA = await CreatePropertyAsync();
        var propertyB = await CreatePropertyAsync();

        var a = await Client.PostAsJsonAsync("/api/staff-assignments",
            new { staffId = Guid.NewGuid(), propertyId = propertyA, role = "Manager" }, JsonOptions);
        var b = await Client.PostAsJsonAsync("/api/staff-assignments",
            new { staffId = Guid.NewGuid(), propertyId = propertyB, role = "Manager" }, JsonOptions);

        // MaxActive is scoped per property, so a different property is unaffected.
        a.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        b.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    private async Task<Guid> CreatePropertyAsync()
    {
        var response = await Client.PostAsJsonAsync("/api/properties", new
        {
            code = $"SA-{Guid.NewGuid():N}"[..12],
            name = "Staff Test Property",
            city = "Rome",
            country = "IT",
            starRating = 3
        }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var property = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return property.GetProperty("id").GetGuid();
    }
}
