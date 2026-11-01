using System.Security.Cryptography;
using Pragmatic.Cryptography;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A <c>ProtectedValue</c> member of an entity the documents engine persists: written and read back, by the repository and by a chain, as
///     the bytes it was encrypted to; a nullable one absent while it holds none (#431). MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class ProtectedValueTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_protected_value_is_read_back_as_the_bytes_it_was_written_as(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<Dossier>();
        var packed = RandomNumberGenerator.GetBytes(61); // a header, a nonce, a tag and a short value: what SubjectDataProtector packs
        var dossier = Dossier.Create();
        dossier.Owner = "anna-" + Guid.NewGuid().ToString("N")[..10];
        dossier.Note = new ProtectedValue(packed);

        var writing = UnitOfWorkTests.UnitOfWork(session);
        new Dossier.DocRepository(writing).Add(dossier);
        await writing.SaveChangesAsync();

        var loaded = await new Dossier.DocRepository(UnitOfWorkTests.UnitOfWork(session)).GetByIdAsync(dossier.Id);
        Assert.NotNull(loaded);
        Assert.Equal(packed, loaded.Note.Packed);
        Assert.Null(loaded.Recap);

        // The optional one, written later, through a replace.
        var recap = RandomNumberGenerator.GetBytes(40);
        loaded.Recap = new ProtectedValue(recap);
        await session.ReplaceAsync(loaded);

        var read = Assert.Single(await session.Query<Dossier>().Where((d, owner) => d.Owner == owner, dossier.Owner).ToListAsync());
        Assert.Equal(packed, read.Note.Packed);
        Assert.Equal(recap, read.Recap!.Value.Packed);
    }
}
