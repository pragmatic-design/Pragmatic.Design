using System.Security.Cryptography;
using Pragmatic.Cryptography;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     A <c>ProtectedValue</c> member of an entity the Sql engine persists: written by the unit of work and read back, by the repository and by a
///     chain, as the bytes it was encrypted to (#431). Its column is binary, typed by the dialect. On the five databases.
/// </summary>
[Collection("pragmatic databases")]
public class ProtectedValueTests(PragmaticDatabases databases)
{
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_protected_value_is_read_back_as_the_bytes_it_was_written_as(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var packed = RandomNumberGenerator.GetBytes(61); // a header, a nonce, a tag and a short value: what SubjectDataProtector packs
        var dossier = Dossier.Create();
        dossier.Owner = "anna";
        dossier.Note = new ProtectedValue(packed);

        var writing = UnitOfWork(session);
        new Dossier.SqlRepository(writing).Add(dossier);
        Assert.Equal(1, await writing.SaveChangesAsync());

        var loaded = await new Dossier.SqlRepository(UnitOfWork(session)).GetByIdAsync(dossier.Id);
        Assert.NotNull(loaded);
        Assert.Equal(packed, loaded.Note.Packed);

        var read = await session.Query<Dossier>().FirstOrDefaultAsync((d, id) => d.Id == id, dossier.Id);
        Assert.NotNull(read);
        Assert.Equal(packed, read.Note.Packed);
    }

    private static SqlUnitOfWork UnitOfWork(SqlSession session) => new(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
}
