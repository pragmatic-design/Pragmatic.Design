using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog;
using Showcase.Catalog.Dtos;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A localized field read through the projection a DTO declares, translated to SQL by EF Core.
/// </summary>
/// <remarks>
///     EF Core's own: the projection composes onto an <c>IQueryable</c> and reads the JSON column through EF Core's value converter. The
///     engines read through their own query model. The rest of the localized field's round trip is <see cref="LocalizedFieldTests" />,
///     which runs on every engine (#873).
/// </remarks>
public class LocalizedFieldProjectionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<Guid> APropertyDescribedAsync(object description)
    {
        var created = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"LOC{Guid.NewGuid():N}"[..12],
            name = "Localized Property",
            city = "Rimini",
            country = "IT",
            starRating = 4,
            description
        });

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>
    ///     And the <b>SQL projection</b> carries it too, which is the half nobody had measured.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>PropertyDetailDto</c> declares <c>[GenerateProjection]</c>, and the column is JSON behind
    ///         a value converter — so whether a localized field survives translation to SQL was the open
    ///         question that kept this field <c>[MapIgnore]</c>. The read endpoint uses <c>FromEntity</c>,
    ///         so no route exercises the projection: this case runs it directly.
    ///     </para>
    ///     <para>
    ///         ⚠️ <c>IgnoreQueryFilters</c> because outside a request there is no ambient tenant and
    ///         <c>Property</c> is an <c>ITenantEntity</c>. The subject is the projection, not the filters.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheProjection_CarriesTheLocalizedFieldToo()
    {
        var id = await APropertyDescribedAsync(new { en = "By the sea", it = "Sul mare" });

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));

        var dto = await db.Set<Property>()
            .IgnoreQueryFilters()
            .Where(p => p.PersistenceId == id)
            .Select(PropertyDetailDto.Projection)
            .SingleAsync();

        dto.Descriptions.Should().NotBeNull("the projection reads the JSON column through its converter");
        dto.Descriptions!.GetExact("en").Should().Be("By the sea");
        dto.Descriptions.GetExact("it").Should().Be("Sul mare");
    }
}
