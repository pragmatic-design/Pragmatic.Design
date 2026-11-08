using Pragmatic.Authorization;
using Pragmatic.Identity;
using Pragmatic.Persistence.Documents;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The generated <c>AccountResolver</c> on the documents engine (#594). A <c>[PragmaticUser]</c> got no resolver on an engine: it read through
///     an <c>IUserLookup&lt;TUser&gt;</c> no engine implemented. Its read repository is that lookup now, a chain this generator compiles, matching
///     the account's <c>Identity.ExternalIdentityKey</c> in the nested document. MongoDB and Cosmos DB.
/// </summary>
[Collection("document servers")]
public class TheUserResolverReadsThroughTheEngineTests(DocumentServers servers)
{
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_signed_in_caller_is_resolved_to_their_account(Store store)
    {
        var session = await SessionAsync(store);
        var key = "local|" + Guid.NewGuid().ToString("N")[..10] + "@example.test";
        var account = await Register(session, key);

        var resolved = await new AccountResolver(new Account.DocReadRepository(session), new SignedIn(key)).ResolveAsync();

        Assert.NotNull(resolved);
        Assert.Equal(account, resolved.PersistenceId);
        Assert.Equal(account, (await new AccountResolver(new Account.DocReadRepository(session), new SignedIn("anyone")).FindByIdentityKeyAsync(key))?.PersistenceId);
    }

    /// <summary>The control: a caller whose key matches no account is resolved to nothing, not to the collection's first document.</summary>
    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_caller_with_no_account_is_resolved_to_null(Store store)
    {
        var session = await SessionAsync(store);
        await Register(session, "local|" + Guid.NewGuid().ToString("N")[..10] + "@example.test");

        Assert.Null(await new AccountResolver(new Account.DocReadRepository(session), new SignedIn("local|nobody@example.test")).ResolveAsync());
    }

    private static async Task<Guid> Register(DocSession session, string key)
    {
        var account = Account.Create();
        account.Name = key;
        account.Identity = new Pragmatic.Identity.Local.LocalIdentity { Email = key["local|".Length..], ExternalIdentityKey = key, PasswordHash = "hash" };
        await session.InsertAsync(account);
        return account.PersistenceId;
    }

    /// <summary>A session without filters: the account has no tenant, owner, scope or soft delete for one to apply.</summary>
    private async Task<DocSession> SessionAsync(Store store)
    {
        var session = servers.Session(store);
        if (store == Store.Cosmos)
            await session.EnsureContainerAsync<Account>();
        return session;
    }

    /// <summary>A caller signed in with <paramref name="externalIdentityKey" />, the key the resolver matches by default.</summary>
    private sealed class SignedIn(string externalIdentityKey) : ICurrentUser, IAuthenticationContext, IUserAuthorization
    {
        public string Id => externalIdentityKey;
        public string? DisplayName => externalIdentityKey;
        public bool IsAuthenticated => true;
        public PrincipalKind Kind => PrincipalKind.User;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; } = new Dictionary<string, IReadOnlyList<string>>();
        public IUserAuthorization Authorization => this;
        public IAuthenticationContext Authentication => this;
        public string? Scheme => "local";
        public string? Protocol => null;
        public string? Issuer => "local";
        public string? Subject => externalIdentityKey;
        public bool IsMfaAuthenticated => false;
        public DateTimeOffset? AuthenticatedAt => null;
        public DateTimeOffset? ExpiresAt => null;
        public string? ExternalIdentityKey => externalIdentityKey;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
        public bool IsResolvingPermissions => false;
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasPermission(string permission) => false;
        public bool HasAnyPermission(IEnumerable<string> names) => false;
        public bool HasAllPermissions(IEnumerable<string> names) => false;
        public bool IsInRole(string role) => false;
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }
}
