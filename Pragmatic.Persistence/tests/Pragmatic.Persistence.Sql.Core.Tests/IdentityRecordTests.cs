using Pragmatic.Identity;
using Xunit;

namespace Pragmatic.Identity
{
    /// <summary>How an identity was provisioned: an enum on the base class, stored as its integer.</summary>
    public enum ProvisionSource
    {
        Manual = 0,
        Scim = 1,
    }

    /// <summary>
    ///     Pragmatic.Identity's base of every identity record, read by its name as the generator reads it (this suite does not reference the
    ///     package): no attribute marks it owned, and the members an application signs in by are declared here, not on the derived record.
    /// </summary>
    public abstract class IdentityRecord
    {
        public string ExternalIdentityKey { get; set; } = string.Empty;

        public ProvisionSource ProvisionSource { get; set; } = ProvisionSource.Manual;

        public bool IsActive { get; set; } = true;

        public DateTimeOffset? LastLoginAt { get; set; }
    }
}

namespace Pragmatic.Persistence.Sql.Core.Tests.Identities
{
    /// <summary>A provider's record, as <c>LocalIdentity</c>: its own members beside the base class's, and a constant that is not a column.</summary>
    public sealed class Credentials : IdentityRecord
    {
        public const string Provider = "local";

        public string PasswordHash { get; set; } = string.Empty;

        public int FailedLoginAttempts { get; set; }
    }

    /// <summary>An account that may have no local sign-in: the record optional, null when its columns are.</summary>
    [Entity]
    [Table("Accounts")]
    public sealed class Account
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = "";

        public Credentials? Identity { get; set; }
    }

    /// <summary>
    ///     An identity record owned by an entity, as an application owns a <c>LocalIdentity</c>: flattened into the owner's table with the
    ///     members its base class declares, read back whole, found by an inherited member, and absent when the account has none. In a
    ///     transaction, rolled back.
    /// </summary>
    [Collection("databases")]
    public class IdentityRecordTests(TestDatabases databases)
    {
        public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

        [Theory]
        [MemberData(nameof(Dialects))]
        public async Task An_identity_record_is_owned_with_its_base_class_members(SqlDialect dialect)
        {
            if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
                return;
            using var session = databases.Session(dialect);
            using var transaction = session.BeginTransaction();
            var key = "local|" + Guid.NewGuid().ToString("N")[..8] + "@example.test";
            var signedIn = new DateTimeOffset(2026, 10, 31, 9, 30, 0, TimeSpan.Zero);
            var local = new Account
            {
                Id = Guid.NewGuid(),
                Name = "Ada",
                Identity = new Credentials { ExternalIdentityKey = key, ProvisionSource = ProvisionSource.Scim, IsActive = false, LastLoginAt = signedIn, PasswordHash = "hash", FailedLoginAttempts = 2 },
            };
            var external = new Account { Id = Guid.NewGuid(), Name = "Grace" };
            await session.InsertManyAsync(new[] { local, external });

            var read = (await session.Query<Account>().Where((a, id) => a.Id == id, local.Id).FirstOrDefaultAsync())!.Identity!;
            Assert.Equal(key, read.ExternalIdentityKey); // the base class's members
            Assert.Equal(ProvisionSource.Scim, read.ProvisionSource);
            Assert.False(read.IsActive);
            Assert.Equal(signedIn, read.LastLoginAt);
            Assert.Equal("hash", read.PasswordHash); // the record's own
            Assert.Equal(2, read.FailedLoginAttempts);
            Assert.Null((await session.Query<Account>().Where((a, id) => a.Id == id, external.Id).FirstOrDefaultAsync())!.Identity);

            // A sign-in finds the account by the key its base class declares.
            var found = await session.Query<Account>().Where((a, k) => a.Identity!.ExternalIdentityKey == k, key).ToListAsync();
            Assert.Equal([local.Id], found.Select(a => a.Id));

            // A failed sign-in writes the record back.
            local.Identity.FailedLoginAttempts = 3;
            local.Identity.IsActive = true;
            await session.UpdateAsync(local);
            var updated = (await session.Query<Account>().Where((a, id) => a.Id == id, local.Id).FirstOrDefaultAsync())!.Identity!;
            Assert.Equal(3, updated.FailedLoginAttempts);
            Assert.True(updated.IsActive);
            transaction.Rollback();
        }
    }
}
