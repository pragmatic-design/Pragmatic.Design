using System.Net;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     E2E for the SG [ValueObject] complex-type mapping on real PostgreSQL: Amenity.Support
///     (ContactInfo value object) is persisted as Support_Email / Support_Phone columns (created by
///     Pragmatic.Migrations from SchemaMetadata) and materialized back as a complex type by EF Core
///     (configured via the generated builder.ComplexProperty). Validates the dual-source mapping
///     (EntityConfig + SchemaMetadata) round-trips at runtime.
/// </summary>
public class ValueObjectE2ETests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValueObject_PersistsAndMaterializesAsComplexType()
    {
        var name = $"VO-{Guid.NewGuid():N}"[..18];
        var create = await PostAsync("/api/amenities", new { name, iconName = "wifi", keywords = new[] { "x" } });
        create.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Reload through the boundary DbContext: the complex type must round-trip from its columns.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var amenity = await db.Amenities.AsNoTracking().FirstAsync(a => a.Name == name);

        amenity.Support.Should().NotBeNull("the [ValueObject] complex type must materialize from its columns");
        amenity.Support.Email.Should().Be("", "the default ContactInfo persisted and read back");
        amenity.Support.Phone.Should().Be("");
    }
}
