using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A Pragmatic entity's <c>Id</c> in a chain on the documents engine: a get-only property returning <c>PersistenceId</c>, read as the
///     document's id (<c>_id</c>), as the Sql engine reads it as the key column (#459). It failed the build with PRAG3101. MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class APragmaticEntitysIdTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_chain_on_the_entitys_Id_finds_the_one_document_with_that_key(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<Dossier>();
        var wanted = Dossier.Create();
        wanted.Owner = "anna";
        var other = Dossier.Create();
        other.Owner = "anna";
        await session.InsertManyAsync([wanted, other]);

        var found = Assert.Single(await session.Query<Dossier>().Where((d, id) => d.Id == id, wanted.Id).ToListAsync());
        Assert.Equal(wanted.Id, found.Id);
        Assert.Null(await session.Query<Dossier>().FirstOrDefaultAsync((d, id) => d.Id == id, Guid.NewGuid()));
    }
}
