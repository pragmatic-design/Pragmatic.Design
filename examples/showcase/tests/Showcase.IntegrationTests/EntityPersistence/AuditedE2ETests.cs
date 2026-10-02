using System.Net;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.EFCore.Auditing;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     E2E for the SG [Audited] feature on real PostgreSQL: Amenity is [Audited], so creating one writes
///     an append-only __AuditEntries row (Data.EntityCreated, target type+id, actor, timestamp) in the same
///     transaction. Validates the full wiring — IAuditedEntity marker, AuditLogInterceptor registration,
///     EF mapping and the migration-created __AuditEntries table (dual-source).
/// </summary>
public class AuditedE2ETests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Audited_OnCreate_AppendsAuditLogRow()
    {
        var name = $"AUD-{Guid.NewGuid():N}"[..18];
        var create = await PostAsync("/api/amenities", new { name, iconName = "wifi", keywords = new[] { "x" } });
        create.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var amenity = await db.Amenities.AsNoTracking().FirstAsync(a => a.Name == name);

        var audits = await db.Set<Pragmatic.Audit.AuditEntry>()
            .Where(a => a.TargetType == "Amenity" && a.TargetId == amenity.Id.ToString())
            .ToListAsync();

        audits.Should().ContainSingle(a => a.Operation == "Data.EntityCreated",
            "an [Audited] entity insert appends exactly one Data.EntityCreated row to the trail");
        audits[0].OccurredAt.Should().NotBe(default);
    }
}
