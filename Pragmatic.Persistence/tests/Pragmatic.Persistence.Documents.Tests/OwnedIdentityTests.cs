using Pragmatic.Identity;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     An identity record owned by a Pragmatic entity, as an application owns a <c>LocalIdentity</c>, on the documents engine: a nested document
///     with the members its base class declares, read back whole, found by an inherited member, absent when the account has none, and written
///     back on a replace. MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class OwnedIdentityTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task An_identity_record_is_a_nested_document_with_its_base_class_members(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<Account>();
        var key = "local|" + Guid.NewGuid().ToString("N")[..8] + "@example.test";
        var signedIn = new DateTimeOffset(2026, 10, 31, 9, 30, 0, TimeSpan.Zero);
        var local = new Account
        {
            Name = "Ada",
            Identity = new Credentials { ExternalIdentityKey = key, ProvisionSource = ProvisionSource.Scim, IsActive = false, LastLoginAt = signedIn, PasswordHash = "hash", FailedLoginAttempts = 2 },
        };
        var external = new Account { Name = "Grace " + key };
        await session.InsertManyAsync([local, external]);

        // A sign-in finds the account by the key its base class declares.
        var read = Assert.Single(await session.Query<Account>().Where((a, k) => a.Identity!.ExternalIdentityKey == k, key).ToListAsync());
        var identity = read.Identity!;
        Assert.Equal((key, ProvisionSource.Scim, false, (DateTimeOffset?)signedIn), (identity.ExternalIdentityKey, identity.ProvisionSource, identity.IsActive, identity.LastLoginAt)); // the base class's
        Assert.Equal(("hash", 2), (identity.PasswordHash, identity.FailedLoginAttempts)); // the record's own
        var none = Assert.Single(await session.Query<Account>().Where((a, name) => a.Name == name, external.Name).ToListAsync());
        Assert.Null(none.Identity);

        // A failed sign-in writes the record back.
        read.Identity!.FailedLoginAttempts = 3;
        read.Identity.IsActive = true;
        await session.ReplaceAsync(read);
        var updated = Assert.Single(await session.Query<Account>().Where((a, k) => a.Identity!.ExternalIdentityKey == k, key).ToListAsync()).Identity!;
        Assert.Equal((3, true), (updated.FailedLoginAttempts, updated.IsActive));
    }
}
