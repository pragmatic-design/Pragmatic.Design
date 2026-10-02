using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Cryptography.EFCore.Tests;

/// <summary>
///     A store backed by a private in-memory SQLite database, with a real master encryptor.
/// </summary>
/// <remarks>
///     The master encryptor is deliberately real rather than a stub: wrapping and unwrapping — including
///     the associated-data binding that stops a wrapped key being moved between subjects — is the part
///     under test, and a stub would assert nothing about it.
/// </remarks>
internal sealed class SubjectKeyStoreFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AesGcmSecretEncryptor _master;

    public SubjectKeyStoreFixture()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CryptographyDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new CryptographyDbContext(options);
        Db.Database.EnsureCreated();

        _master = new AesGcmSecretEncryptor(
            new EncryptionKeyRing(EncryptionKey.FromMaterial(RandomNumberGenerator.GetBytes(32))));

        Store = new EfCoreSubjectKeyStore(Db, _master, TimeProvider.System);
    }

    public CryptographyDbContext Db { get; }

    public EfCoreSubjectKeyStore Store { get; }

    /// <summary>The master encryptor, exposed so a test can forge a validly-wrapped-but-wrong key.</summary>
    public ISecretEncryptor Master => _master;

    /// <summary>A second store over the same database — used to prove state survives the instance.</summary>
    public EfCoreSubjectKeyStore NewStoreOverSameDatabase()
    {
        var options = new DbContextOptionsBuilder<CryptographyDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new EfCoreSubjectKeyStore(new CryptographyDbContext(options), _master, TimeProvider.System);
    }

    public void Dispose()
    {
        Db.Dispose();
        _master.Dispose();
        _connection.Dispose();
    }
}
