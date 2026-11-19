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

    /// <summary>
    ///     A translation added in place to a loaded entity is saved by the unit of work (#883). <c>LocalizedString.Set</c> changes the instance
    ///     it is called on, so a snapshot that kept the instance would agree with it, and the save would write nothing: as EF Core did before #623.
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_translation_added_in_place_is_saved(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<Leaflet>();
        var leaflet = Leaflet.Create();
        leaflet.Code = "LF-" + Guid.NewGuid().ToString("N")[..10];
        leaflet.Title = LocalizedString.From(("en", "Welcome"));
        var writing = UnitOfWorkTests.UnitOfWork(session);
        new Leaflet.DocRepository(writing).Add(leaflet);
        await writing.SaveChangesAsync();

        var changing = UnitOfWorkTests.UnitOfWork(session);
        var repository = new Leaflet.DocRepository(changing);
        var loaded = await repository.GetByIdAsync(leaflet.Id);
        Assert.NotNull(loaded);
        loaded.Title.Set("it", "Benvenuti");
        repository.Update(loaded);
        await changing.SaveChangesAsync();

        var read = await new Leaflet.DocRepository(UnitOfWorkTests.UnitOfWork(session)).GetByIdAsync(leaflet.Id);
        Assert.NotNull(read);
        Assert.Equal("Benvenuti", read.Title.Get("it"));
        Assert.Equal("Welcome", read.Title.Get("en"));
    }
}
