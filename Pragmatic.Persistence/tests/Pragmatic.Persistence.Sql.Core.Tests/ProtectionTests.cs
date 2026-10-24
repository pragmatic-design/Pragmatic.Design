using System.Security.Cryptography;
using System.Text;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests.Protected;

/// <summary>The workload's Labels table with its name stored protected.</summary>
[Entity]
[Table("Labels")]
public sealed class SecretLabel : ISqlEntity
{
    public Guid Id { get; set; }

    [Convert<SqlProtected>]
    public string Name { get; set; } = "";
}

/// <summary>A deterministic test protector: AES-SIV-like enough for a test (AES-ECB of a SHA-256-padded block), keyed per tenant.</summary>
internal sealed class TenantProtector(string tenant) : ISqlProtector
{
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes("tenant:" + tenant));

    public string Protect(string value)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        return Convert.ToBase64String(aes.EncryptEcb(Encoding.UTF8.GetBytes(value), PaddingMode.PKCS7));
    }

    public string Unprotect(string stored)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        return Encoding.UTF8.GetString(aes.DecryptEcb(Convert.FromBase64String(stored), PaddingMode.PKCS7));
    }
}

/// <summary>
///     <c>[Convert&lt;SqlProtected&gt;]</c> with the ambient protector (<c>SqlProtection.Use</c>): the column holds the protected text (read raw
///     through the plain entity), the protected entity reads the value back, an equality predicate matches through the deterministic protector,
///     another tenant's key cannot read it, and no scope at all is refused. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class ProtectionTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_protected_column_is_written_and_read_through_the_ambient_protector(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var label = new SecretLabel { Id = Guid.NewGuid(), Name = "IT60X0542811101000000123456" };

        using (SqlProtection.Use(new TenantProtector("acme")))
        {
            await session.InsertAsync(label);
            var raw = await session.Query<Label>().FirstOrDefaultAsync((l, id) => l.Id == id, label.Id);
            Assert.NotEqual(label.Name, raw!.Name); // stored protected
            Assert.Equal(label.Name, new TenantProtector("acme").Unprotect(raw.Name));
            Assert.Equal(label.Name, (await session.Query<SecretLabel>().FirstOrDefaultAsync((l, id) => l.Id == id, label.Id))!.Name);
            Assert.Equal([label.Id], (await session.Query<SecretLabel>().Where((l, name) => l.Name == name, label.Name).ToListAsync()).Select(l => l.Id));
        }

        using (SqlProtection.Use(new TenantProtector("other")))
            await Assert.ThrowsAsync<CryptographicException>(async () => await session.Query<SecretLabel>().FirstOrDefaultAsync((l, id) => l.Id == id, label.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.Query<SecretLabel>().FirstOrDefaultAsync((l, id) => l.Id == id, label.Id));
        transaction.Rollback();
    }
}
