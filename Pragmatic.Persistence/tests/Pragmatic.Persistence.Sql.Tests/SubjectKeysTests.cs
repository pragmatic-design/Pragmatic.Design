using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Cryptography;
using Pragmatic.Cryptography.Engines.Sql;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The per-subject keys on the Sql engine (Pragmatic.Cryptography.Engines), on five databases: their table as the generated schema has it (EF
///     Core's <c>__SubjectKeys</c>) in the database that holds a <c>[DataSubject]</c>, migrated with the others; a <c>ProtectedValue</c> written
///     under a subject's key, read back decrypted, and once the key is destroyed read back as erased, its ciphertext untouched (#431).
/// </summary>
[Collection("pragmatic databases")]
public class SubjectKeysTests(PragmaticDatabases databases)
{
    [Fact]
    public void The_keys_table_in_the_schema_is_EF_Cores_column_for_column()
    {
        // SchemaMetadataTransform.BuildSubjectKeysTable on SQL Server, the EF Core form: a database where both write the keys holds one table.
        var keys = DialectSchemas.For(SqlDialect.SqlServer).Tables.Single(t => t.Name == "__SubjectKeys");
        Assert.Equal([("SubjectRef", "nvarchar(128)", false, true), ("KeyId", "nvarchar(64)", false, false), ("WrappedKey", "varbinary(max)", true, false),
                ("CreatedAt", "datetimeoffset", false, false), ("DestroyedAt", "datetimeoffset", true, false)],
            keys.Columns.Select(c => (c.Name, c.SqlType, c.IsNullable, c.IsPrimaryKey)));
        var index = Assert.Single(keys.Indexes);
        Assert.Equal(("IX___SubjectKeys_KeyId", true), (index.Name, index.IsUnique));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_protected_value_reads_back_decrypted_and_once_its_key_is_destroyed_as_erased(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        await using var provider = Keys(session);
        await using var scope = provider.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<ISubjectDataProtector>();
        var keys = scope.ServiceProvider.GetRequiredService<ISubjectKeyStore>();
        var subject = "subject-" + Guid.NewGuid().ToString("N");
        var plain = Encoding.UTF8.GetBytes("ada@example.com");

        var dossier = Dossier.Create();
        dossier.Owner = subject;
        dossier.Note = new ProtectedValue(await protector.ProtectAsync(subject, plain));
        var writing = UnitOfWork(session);
        new Dossier.SqlRepository(writing).Add(dossier);
        await writing.SaveChangesAsync();
        var row = await session.Query<SubjectKeyRow>().Where((k, s) => k.SubjectRef == s, subject).FirstOrDefaultAsync();
        Assert.NotNull(row);
        Assert.Equal(row.KeyId, dossier.Note.KeyId); // the value names the key kept for its subject

        var stored = (await new Dossier.SqlRepository(UnitOfWork(session)).GetByIdAsync(dossier.Id))!;
        var read = await protector.TryReadAsync(stored.Note.Packed);
        Assert.Equal(DecryptOutcome.Success, read.Outcome);
        Assert.Equal(plain, read.Plain);

        var report = await keys.DestroyAsync(subject);
        Assert.False(report.AlreadyDestroyed);
        Assert.True((await keys.DestroyAsync(subject)).AlreadyDestroyed); // idempotent: the original destruction is reported

        // The ciphertext is where it was; it is the key that is gone, and the read says erased, not tampered with.
        var after = (await new Dossier.SqlRepository(UnitOfWork(session)).GetByIdAsync(dossier.Id))!;
        Assert.Equal(dossier.Note.Packed, after.Note.Packed);
        Assert.Equal(DecryptOutcome.KeyDestroyed, (await protector.TryReadAsync(after.Note.Packed)).Outcome);
        await Assert.ThrowsAsync<SubjectKeyDestroyedException>(async () => await protector.ProtectAsync(subject, plain)); // never resurrected
        var destroyed = await session.Query<SubjectKeyRow>().Where((k, s) => k.SubjectRef == s, subject).FirstOrDefaultAsync();
        Assert.Equal((null, row.KeyId), (destroyed!.WrappedKey, destroyed.KeyId)); // the row stays, without its key
        Assert.NotNull(destroyed.DestroyedAt);
    }

    /// <summary>The keys of a boundary (this class stands for its key) on <paramref name="session" />, as the generated registration adds them.</summary>
    private static ServiceProvider Keys(SqlSession session)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<SqlSession>(typeof(SubjectKeysTests), session); // an instance: the scope does not dispose the test's session
        services.AddSingleton<ISecretEncryptor>(new AesGcmSecretEncryptor(new EncryptionKeyRing(EncryptionKey.FromMaterial(RandomNumberGenerator.GetBytes(32)))));
        services.AddSqlSubjectKeys<SubjectKeysTests>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static SqlUnitOfWork UnitOfWork(SqlSession session) => new(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
}
