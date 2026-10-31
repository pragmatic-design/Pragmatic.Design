using Pragmatic.Identity.Local;
using Pragmatic.Identity.Local.Services;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The <c>LocalIdentityStore</c> the generator writes for a <c>[PragmaticUser]</c> that owns a <c>LocalIdentity</c>, on the Sql engine's
///     repository and unit of work: the identity actions sign in through it. On the five databases.
/// </summary>
[Collection("pragmatic databases")]
public class LocalIdentityStoreTests(PragmaticDatabases databases)
{
    private static readonly DateTimeOffset LockedUntil = new(2026, 10, 31, 9, 30, 0, TimeSpan.Zero);

    /// <summary>
    ///     A self-registration creates the user, the finders read it by its identity's email and external key, a change to what they returned is
    ///     saved, and an email in use is known.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_generated_store_signs_in_on_the_engine(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var email = Guid.NewGuid().ToString("N")[..10] + "@example.test";
        await Store(session).CreateAsync(new LocalIdentity { Email = email, ExternalIdentityKey = "local|" + email, PasswordHash = "hash" });

        // Each step on its own unit of work, as each request has its own.
        var signIn = Store(session);
        var found = await signIn.FindByEmailAsync(email);
        Assert.NotNull(found);
        Assert.Equal("local|" + email, found.ExternalIdentityKey);
        found.FailedLoginAttempts = 1;
        found.LockoutEnd = LockedUntil;
        await signIn.UpdateAsync(found);

        var again = await Store(session).FindByExternalKeyAsync("local|" + email);
        Assert.NotNull(again);
        Assert.Equal((1, (DateTimeOffset?)LockedUntil), (again.FailedLoginAttempts, again.LockoutEnd)); // the change was saved, not answered and lost
        Assert.True(await Store(session).EmailExistsAsync(email));
        Assert.False(await Store(session).EmailExistsAsync("nobody-" + email));
        Assert.Null(await Store(session).FindByEmailAsync("nobody-" + email));
    }

    private static ILocalIdentityStore Store(SqlSession session)
    {
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
        return new Account.LocalIdentityStore(new Account.SqlRepository(unitOfWork), unitOfWork);
    }
}
