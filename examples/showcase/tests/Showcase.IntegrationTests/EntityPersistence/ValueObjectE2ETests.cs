using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Repository;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     E2E for the SG [ValueObject] mapping: Amenity.Support (a ContactInfo value object) is written with the amenity and read
///     back as the value object, through the boundary's repository: Support_Email / Support_Phone columns on EF Core and the
///     Sql engine, a nested value on the documents engine.
/// </summary>
public class ValueObjectE2ETests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValueObject_PersistsAndMaterializesAsComplexType()
    {
        var name = $"VO-{Guid.NewGuid():N}"[..18];
        var create = await PostAsync("/api/amenities", new { name, iconName = "wifi", keywords = new[] { "x" } });
        create.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("id").GetGuid();

        // By id: a specification written here would not be compiled by an engine's generator, which runs on the module.
        using var scope = Services.CreateScope();
        var amenity = await scope.ServiceProvider.GetRequiredService<IReadRepository<Amenity>>().GetByIdAsync(id);

        amenity.Should().NotBeNull();
        amenity!.Support.Should().NotBeNull("the [ValueObject] must be read back with the entity");
        amenity.Support.Email.Should().Be("", "the default ContactInfo persisted and read back");
        amenity.Support.Phone.Should().Be("");
    }
}
