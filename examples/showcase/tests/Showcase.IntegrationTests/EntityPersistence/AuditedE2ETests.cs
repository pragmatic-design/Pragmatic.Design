using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Audit;
using Pragmatic.Persistence.Repository;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     E2E for the SG [Audited] feature: Amenity is [Audited], so creating one appends an entry to the audit trail
///     (Data.EntityCreated, target type and id, timestamp) in the same transaction. Read back through the trail's reader,
///     which every host registers with its trail, so the same test measures EF Core's interceptor and the engines' save phases.
/// </summary>
public class AuditedE2ETests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Audited_OnCreate_AppendsAuditLogRow()
    {
        var name = $"AUD-{Guid.NewGuid():N}"[..18];
        var create = await PostAsync("/api/amenities", new { name, iconName = "wifi", keywords = new[] { "x" } });
        create.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();

        // By id: a specification written here would not be compiled by an engine's generator, which runs on the module.
        using var scope = Services.CreateScope();
        var amenity = await scope.ServiceProvider.GetRequiredService<IReadRepository<Amenity>>().GetByIdAsync(id);
        amenity.Should().NotBeNull();

        var audits = (await scope.ServiceProvider.GetRequiredService<IAuditTrailReader>()
            .QueryAsync(new AuditQuery { TargetType = "Amenity", TargetId = amenity!.Id.ToString() })).Entries;

        audits.Should().ContainSingle(a => a.Operation == "Data.EntityCreated",
            "an [Audited] entity insert appends exactly one Data.EntityCreated row to the trail");
        audits[0].OccurredAt.Should().NotBe(default);
    }
}
