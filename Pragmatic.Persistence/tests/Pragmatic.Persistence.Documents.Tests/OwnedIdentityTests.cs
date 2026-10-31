using Pragmatic.Identity;
using Pragmatic.Identity.Local;
using Pragmatic.Identity.Local.Services;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A <c>LocalIdentity</c> owned by a <c>[PragmaticUser]</c> on the documents engine: a nested document with the members its base class
///     declares, and the generated <c>LocalIdentityStore</c> that the identity actions sign in through, on the engine's repository and unit of
///     work. MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class OwnedIdentityTests(DocumentServers servers)
{
    private static readonly DateTimeOffset SignedIn = new(2026, 10, 31, 9, 30, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task An_identity_record_is_a_nested_document_with_its_base_class_members(Store store)
    {
        var session = await SessionAsync(store);
        var key = "local|" + Unique() + "@example.test";
        var local = Account.Create();
        local.Name = "Ada";
        local.Identity = new LocalIdentity { ExternalIdentityKey = key, ProvisionSource = ProvisionSource.Scim, IsActive = false, LastLoginAt = SignedIn, PasswordHash = "hash", FailedLoginAttempts = 2 };
        var external = Account.Create();
        external.Name = "Grace " + key;
        await session.InsertManyAsync([local, external]);

        // A sign-in finds the account by the key its base class declares.
        var read = Assert.Single(await session.Query<Account>().Where((a, k) => a.Identity!.ExternalIdentityKey == k, key).ToListAsync());
        var identity = read.Identity!;
        Assert.Equal((key, ProvisionSource.Scim, false, (DateTimeOffset?)SignedIn), (identity.ExternalIdentityKey, identity.ProvisionSource, identity.IsActive, identity.LastLoginAt)); // the base class's
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

    /// <summary>
    ///     The store the generator writes for the account, on the engine's repository and unit of work: a self-registration creates the user,
    ///     the finders read it by its identity's email and external key, a change to what they returned is saved, and an email in use is known.
    /// </summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_generated_store_signs_in_on_the_engine(Store store)
    {
        var session = await SessionAsync(store);
        var email = Unique() + "@example.test";
        var registered = new LocalIdentity { Email = email, ExternalIdentityKey = "local|" + email, PasswordHash = "hash" };
        await IdentityStore(session).CreateAsync(registered);

        // Each step on its own unit of work, as each request has its own.
        var signIn = IdentityStore(session);
        var found = await signIn.FindByEmailAsync(email);
        Assert.NotNull(found);
        Assert.Equal("local|" + email, found.ExternalIdentityKey);
        found.FailedLoginAttempts = 1;
        found.LockoutEnd = SignedIn;
        await signIn.UpdateAsync(found);

        var again = await IdentityStore(session).FindByExternalKeyAsync("local|" + email);
        Assert.NotNull(again);
        Assert.Equal((1, (DateTimeOffset?)SignedIn), (again.FailedLoginAttempts, again.LockoutEnd)); // the change was saved, not answered and lost
        Assert.True(await IdentityStore(session).EmailExistsAsync(email));
        Assert.False(await IdentityStore(session).EmailExistsAsync("nobody-" + email));
        Assert.Null(await IdentityStore(session).FindByEmailAsync("nobody-" + email));
    }

    /// <summary>A session without filters: the account has no tenant, owner, scope or soft delete for one to apply.</summary>
    private async Task<Pragmatic.Persistence.Documents.DocSession> SessionAsync(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<Account>();
        return session;
    }

    private static ILocalIdentityStore IdentityStore(Pragmatic.Persistence.Documents.DocSession session)
    {
        var unitOfWork = UnitOfWorkTests.UnitOfWork(session);
        return new Account.LocalIdentityStore(new Account.DocRepository(unitOfWork), unitOfWork);
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];
}
