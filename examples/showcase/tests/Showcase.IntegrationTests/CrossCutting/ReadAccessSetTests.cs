using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;
using Showcase.Booking;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     The set EF Core gives a boundary that reads another one's entity with <c>[ReadAccess]</c>.
/// </summary>
/// <remarks>
///     EF Core's own: on EF Core every boundary has a context of its own, and <c>[ReadAccess]</c> adds the entity to the reader's. The
///     engines have no such set, since the reader reads through the owning boundary's repository, so this file runs on EF Core only (#873).
/// </remarks>
public class ReadAccessSetTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // ReadAccess and the tenant: reading somebody else's rows is not reading every tenant's
    // =========================================================================

    /// <summary>
    ///     The <c>DbSet</c> that <c>[ReadAccess]</c> adds is tenant-filtered exactly like the owner's.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Property</c> is <c>ITenantEntity</c>, and <c>BookingBoundary</c> declares
    ///         <c>[ReadAccess&lt;Property&gt;]</c>. The two named EF filters an entity can carry come
    ///         from two different places: <c>"SoftDelete"</c> from the shared per-entity config, which
    ///         a read-access entity does get, and <c>"Tenant"</c> from <c>OnModelCreating</c> of each
    ///         boundary context — which only ever iterated that context's <b>own</b> entities. So a raw
    ///         <c>BookingDbContext.Set&lt;Property&gt;()</c> carried <c>!IsDeleted</c> and nothing else,
    ///         and read every tenant's rows.
    ///     </para>
    ///     <para>
    ///         Measured from outside first, on a consumer application, where the reading boundary of one
    ///         organisation returned a row that the owning boundary refused. Here the second tenant is
    ///         an ambient <c>TenantScope</c>, which is what a background job or a job runner has.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheReadAccessSet_IsTenantFilteredLikeTheOwnersSet()
    {
        using var tenantA = CreateClientAs("read-access-tenant-a", "Tenant A", "ra-tenant-a");

        var created = await tenantA.PostAsJsonAsync("/api/properties", new
        {
            code = $"RA-{Guid.NewGuid():N}"[..12],
            name = $"ReadAccessProp-{Guid.NewGuid():N}"[..20],
            city = "Rome",
            country = "IT",
            starRating = 4
        }, JsonOptions);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var propertyId = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetGuid();

        (await CountPropertyAsync(typeof(BookingBoundary), "ra-tenant-a", propertyId))
            .Should().Be(1, "the row belongs to this tenant, and the reader may read it");
        (await CountPropertyAsync(typeof(CatalogBoundary), "ra-tenant-a", propertyId))
            .Should().Be(1, "the owner sees it too — the control that the row is really there");

        (await CountPropertyAsync(typeof(CatalogBoundary), "ra-tenant-b", propertyId))
            .Should().Be(0, "the owner's context is tenant-filtered, and always was");
        (await CountPropertyAsync(typeof(BookingBoundary), "ra-tenant-b", propertyId))
            .Should().Be(0, "and so is the DbSet [ReadAccess] adds to the reader");
    }

    /// <summary>
    ///     Counts a property through one boundary's raw <c>DbSet</c>, under an ambient tenant.
    /// </summary>
    /// <remarks>
    ///     Raw <c>Set&lt;T&gt;()</c> on purpose: it is the path the Pragmatic <c>IQueryFilter</c>
    ///     pipeline does not cover, which is the whole reason the named EF filter exists.
    /// </remarks>
    private async Task<int> CountPropertyAsync(Type boundary, string tenantId, Guid propertyId)
    {
        using var tenant = TenantScope.BeginScope(tenantId);
        using var scope = Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(boundary);

        return await db.Set<Property>()
            .Where(p => p.PersistenceId == propertyId)
            .CountAsync();
    }
}
