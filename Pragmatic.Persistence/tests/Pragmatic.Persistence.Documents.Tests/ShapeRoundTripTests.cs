using Pragmatic.Persistence.Documents;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     Pragmatic's entities written and read back as documents, with no EF Core: the key Pragmatic assigns (PersistenceId as <c>_id</c>), the
///     members with a private setter (OwnerId, PropertyId, AccessScopes) read back into the type's own partial, the relation as its key, a chain
///     reading members only the shape declares, the version as <c>_v</c>. MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class ShapeRoundTripTests(DocumentServers servers)
{
    private static readonly DateTimeOffset Day = new(2026, 11, 2, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task Pragmatics_entities_round_trip_through_the_shape(Store store)
    {
        // Raw: the shape alone, whoever's rows (the families have their own tests).
        var toggle = new Pragmatic.Persistence.Query.Filters.QueryFilterToggle();
        using var raw = toggle.UseMode(Pragmatic.Persistence.Query.Filters.FilterMode.Raw);
        var session = servers.Session(store, new Pragmatic.Persistence.Documents.PragmaticDocFilters(toggle: toggle));
        if (store == Store.Cosmos)
        {
            await session.EnsureContainerAsync<Property>();
            await session.EnsureContainerAsync<Reservation>();
            await session.EnsureContainerAsync<Amenity>();
        }

        var riva = new Property { Name = "RIVA", TenantId = "acme", CreatedAt = Day, CreatedBy = "anna" };
        riva.GrantScope("desk-north");
        var stay = new Reservation { CheckIn = Day, Nights = 3 };
        stay.SetPropertyId(riva.Id);
        stay.SetOwnerId("anna");
        var other = new Reservation { CheckIn = Day, Nights = 1 };
        other.SetPropertyId(riva.Id);
        other.SetOwnerId("bruno");
        await session.InsertAsync(riva);
        await session.InsertManyAsync([stay, other]);
        await session.InsertAsync(new Amenity { Name = "WiFi" });

        // The members only the shape declares, in a chain: bound on the shape and translated.
        var annas = await session.Query<Reservation>().Where((r, owner) => r.OwnerId == owner && !r.IsDeleted, "anna").ToListAsync();
        var read = Assert.Single(annas);
        Assert.Equal((stay.PersistenceId, riva.Id, "anna", Day, 3), (read.PersistenceId, read.PropertyId, read.OwnerId, read.CheckIn, read.Nights));

        var property = (await session.Query<Property>().Where((p, name) => p.Name == name, "RIVA").ToListAsync()).Single();
        Assert.Equal((riva.Id, "acme", "anna", Day), (property.Id, property.TenantId, property.CreatedBy, property.CreatedAt));
        Assert.Equal(["desk-north"], property.AccessScopes);
        // A count in the tenant's partition: a tenant's entity is partitioned by it, so Cosmos DB's gateway serves it (across partitions it does not).
        Assert.Equal(1L, await session.Query<Property>().CountAsync((p, tenant) => p.TenantId == tenant, "acme"));
        var wifi = (await session.Query<Amenity>().Where((a, name) => a.Name == name, "WiFi").ToListAsync()).Single();
        Assert.Equal(0UL, wifi.RowVersion);
    }
}
