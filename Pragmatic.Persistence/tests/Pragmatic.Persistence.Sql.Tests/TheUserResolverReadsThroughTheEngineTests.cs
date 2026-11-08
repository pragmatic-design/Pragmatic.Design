using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Identity.Local;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The generated <c>AccountResolver</c> on the Sql engine, on the five databases (#593). A <c>[PragmaticUser]</c> got no resolver on an
///     engine: it read through EF Core's <c>IQueryable</c>, then through an <c>IUserLookup&lt;TUser&gt;</c> no engine implemented. Its read
///     repository is that lookup now, a chain this generator compiles, matching the account's <c>Identity.ExternalIdentityKey</c>.
/// </summary>
[Collection("pragmatic databases")]
public class TheUserResolverReadsThroughTheEngineTests(PragmaticDatabases databases)
{
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_signed_in_caller_is_resolved_to_their_account(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        var key = "local|" + Guid.NewGuid().ToString("N")[..10] + "@example.test";
        var id = await Register(session, key);

        var resolved = await new AccountResolver(new Account.SqlReadRepository(session), new SignedIn(key)).ResolveAsync();

        Assert.NotNull(resolved);
        Assert.Equal(id, resolved.PersistenceId);
        Assert.Equal(id, (await new AccountResolver(new Account.SqlReadRepository(session), new SignedIn("anyone")).FindByIdentityKeyAsync(key))?.PersistenceId);
    }

    /// <summary>The control: a caller whose key matches no account is resolved to nothing, not to the table's first row.</summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_caller_with_no_account_is_resolved_to_null(SqlDialect dialect)
    {
        using var session = await databases.SessionAsync(dialect, null);
        await Register(session, "local|" + Guid.NewGuid().ToString("N")[..10] + "@example.test");

        Assert.Null(await new AccountResolver(new Account.SqlReadRepository(session), new SignedIn("local|nobody@example.test")).ResolveAsync());
    }

    private static async Task<Guid> Register(SqlSession session, string key)
    {
        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());
        await new Account.LocalIdentityStore(new Account.SqlRepository(unitOfWork), unitOfWork)
            .CreateAsync(new LocalIdentity { Email = key["local|".Length..], ExternalIdentityKey = key, PasswordHash = "hash" });
        return (await new Account.SqlReadRepository(session).FindByIdentityKeyAsync(key))!.PersistenceId;
    }

    /// <summary>A caller signed in with <paramref name="externalIdentityKey" />, the key the resolver matches by default.</summary>
    private sealed class SignedIn(string externalIdentityKey) : ICurrentUser, IAuthenticationContext
    {
        public string Id => externalIdentityKey;
        public string? DisplayName => externalIdentityKey;
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; } = new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => new TestUser(externalIdentityKey).Authorization;
        public IAuthenticationContext Authentication => this;
        public string? Scheme => "local";
        public string? Protocol => null;
        public string? Issuer => "local";
        public string? Subject => externalIdentityKey;
        public bool IsMfaAuthenticated => false;
        public DateTimeOffset? AuthenticatedAt => null;
        public DateTimeOffset? ExpiresAt => null;
        public string? ExternalIdentityKey => externalIdentityKey;
    }
}
