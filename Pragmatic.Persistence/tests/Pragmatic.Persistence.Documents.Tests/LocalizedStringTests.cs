using Pragmatic.Internationalization.Types;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A <c>LocalizedString</c> member of an entity the documents engine persists: written and read back, by the repository and by a chain, every
///     translation kept; a nullable one absent while it holds none (#475). MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class LocalizedStringTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_localized_string_is_read_back_with_every_translation(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<Leaflet>();
        var leaflet = Leaflet.Create();
        leaflet.Code = "LF-" + Guid.NewGuid().ToString("N")[..10];
        leaflet.Title = LocalizedString.From(("en", "Welcome"), ("it", "Benvenuti"), ("fr", "Bienvenue à l'hôtel"));

        var writing = UnitOfWorkTests.UnitOfWork(session);
        new Leaflet.DocRepository(writing).Add(leaflet);
        await writing.SaveChangesAsync();

        var loaded = await new Leaflet.DocRepository(UnitOfWorkTests.UnitOfWork(session)).GetByIdAsync(leaflet.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Benvenuti", loaded.Title.Get("it"));
        Assert.Equal("Bienvenue à l'hôtel", loaded.Title.Get("fr"));
        Assert.Equal(3, loaded.Title.Count);
        Assert.Null(loaded.Subtitle);

        // The optional one, written later, through a replace.
        loaded.Subtitle = LocalizedString.From(("en", "Rooms and rates"));
        await session.ReplaceAsync(loaded);

        var read = Assert.Single(await session.Query<Leaflet>().Where((l, code) => l.Code == code, leaflet.Code).ToListAsync());
        Assert.Equal("Welcome", read.Title.Get("en"));
        Assert.Equal("Rooms and rates", read.Subtitle!.Get("en"));
    }
}
