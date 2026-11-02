using Pragmatic.Internationalization.Types;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A <c>LocalizedString</c> member of an entity the Sql engine persists: written by the unit of work and read back, by the repository and by a
///     chain, every translation kept (#475). Its column holds the JSON text EF Core's converter writes for the same value. On the five databases.
/// </summary>
[Collection("pragmatic databases")]
public class LocalizedStringTests(PragmaticDatabases databases)
{
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_localized_string_is_read_back_with_every_translation(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var leaflet = Leaflet.Create();
        leaflet.Code = "LF-" + Guid.NewGuid().ToString("N")[..6];
        leaflet.Title = LocalizedString.From(("en", "Welcome"), ("it", "Benvenuti"), ("fr", "Bienvenue à l'hôtel"));

        var writing = UnitOfWork(session);
        new Leaflet.SqlRepository(writing).Add(leaflet);
        Assert.Equal(1, await writing.SaveChangesAsync());

        var loaded = await new Leaflet.SqlRepository(UnitOfWork(session)).GetByIdAsync(leaflet.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Benvenuti", loaded.Title.Get("it"));
        Assert.Equal("Bienvenue à l'hôtel", loaded.Title.Get("fr"));
        Assert.Equal(3, loaded.Title.Count);
        Assert.Null(loaded.Subtitle);

        // The optional one, written later through an update.
        var updating = UnitOfWork(session);
        var tracked = await new Leaflet.SqlRepository(updating).GetByIdAsync(leaflet.Id);
        tracked!.Subtitle = LocalizedString.From(("en", "Rooms and rates"));
        Assert.Equal(1, await updating.SaveChangesAsync());

        var read = await session.Query<Leaflet>().FirstOrDefaultAsync((l, id) => l.Id == id, leaflet.Id);
        Assert.NotNull(read);
        Assert.Equal("Welcome", read.Title.Get("en"));
        Assert.Equal("Rooms and rates", read.Subtitle!.Get("en"));
    }

    private static SqlUnitOfWork UnitOfWork(SqlSession session) => new(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
}
