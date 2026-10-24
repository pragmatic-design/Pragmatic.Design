using Microsoft.Data.Sqlite;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Pragmatic;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     Pragmatic's entities written and read back by the Sql engine on SQLite, with no EF Core: the key Pragmatic assigns (PersistenceId,
///     Id its alias), the members with a private setter (OwnerId, PropertyId, AccessScopes) read through their accessors, a chain through
///     a navigation the shape declares, the one-to-many and many-to-many collections (ICollection) included, the soft delete.
/// </summary>
public class ShapeRoundTripTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 2, 14, 0, 0, TimeSpan.Zero);

    /// <summary>A session on a fresh in-memory database with the generated schema (as Pragmatic.Migrations writes it), for the caller <paramref name="filters" /> describes.</summary>
    internal static SqlSession Open(ISqlFilters? filters)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "PRAGMA foreign_keys = ON;\n" + PragmaticMigrations.SqliteScript;
            create.ExecuteNonQuery();
        }

        return new SqlSession(connection, SqlDialect.Sqlite) { Filters = filters };
    }

    [Fact]
    public async Task Pragmatics_entities_round_trip_through_the_shape()
    {
        // The caller: anna, of the tenant acme, holding the scope desk-north (what the rows below are written for).
        using var tenant = TenantScope.BeginScope("acme");
        using var session = Open(new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north")));
        var riva = new Property { Name = "Hotel RIVA", TenantId = "acme" };
        riva.GrantScope("desk-north");
        var wifi = new Amenity { Name = "WiFi" };
        var standard = new RoomType { Code = "STD" };
        standard.SetPropertyId(riva.Id);
        var stay = new Reservation { CheckIn = Day };
        stay.SetPropertyId(riva.Id);
        stay.SetOwnerId("anna");
        await session.InsertAsync(riva);
        await session.InsertAsync(wifi);
        await session.InsertAsync(standard);
        await session.InsertAsync(stay);
        await session.LinkAsync(riva, nameof(Property.Amenities), wifi);

        // The key Pragmatic assigns, Id its alias in a chain; the private setters read back through their accessors.
        var read = await session.Query<Reservation>().FirstOrDefaultAsync((r, id) => r.Id == id, stay.Id);
        Assert.Equal((stay.PersistenceId, riva.Id, "anna", Day), (read!.PersistenceId, read.PropertyId, read.OwnerId, read.CheckIn));
        var scoped = await session.Query<Property>().FirstOrDefaultAsync((p, id) => p.Id == id, riva.Id);
        Assert.Equal(["desk-north"], scoped!.AccessScopes);

        // A chain through the navigation the shape declares (an INNER JOIN on PersistenceId), and the owner it declares.
        var names = await session.Query<Reservation>()
            .Where((r, a) => r.Property.Name == a.Name && r.OwnerId == a.User, (Name: "Hotel RIVA", User: "anna"))
            .Select(r => r.Property.Name)
            .ToListAsync();
        Assert.Equal(["Hotel RIVA"], names);

        // The collections (ICollection), the one-to-many and the many-to-many through PropertyAmenities.
        var graph = await session.Query<Property>().Include(p => p.RoomTypes).Include(p => p.Amenities).FirstOrDefaultAsync((p, id) => p.Id == id, riva.Id);
        Assert.Equal(["STD"], graph!.RoomTypes.Select(t => t.Code));
        Assert.Equal(["WiFi"], graph.Amenities.Select(a => a.Name));
        var linked = await session.Query<Amenity>().Include(a => a.Properties).FirstOrDefaultAsync((a, id) => a.Id == id, wifi.Id);
        Assert.Equal(["Hotel RIVA"], linked!.Properties.Select(p => p.Name));

        // [SoftDelete]: the delete marks the row, which the queries no longer read.
        await session.DeleteAsync(standard);
        Assert.Equal(0, await session.Query<RoomType>().CountAsync((t, id) => t.PropertyId == id, riva.Id));
        Assert.Equal(1, await session.Query<RoomType>().IncludeDeleted().CountAsync((t, id) => t.PropertyId == id, riva.Id));
    }
}
