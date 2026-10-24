using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace PragmaticEntities.Tests;

/// <summary>The scopes a resolver hands back for any user: the caller's, as Pragmatic's <c>IUserScopeResolver</c> answers them.</summary>
internal sealed class TestScopes(params string[] scopes) : IUserScopeResolver
{
    public Task<IReadOnlySet<string>> ResolveAccessScopesAsync(ICurrentUser user, CancellationToken ct = default) => Task.FromResult(ResolveAccessScopes(user));

    public IReadOnlySet<string> ResolveAccessScopes(ICurrentUser user) => scopes.ToHashSet();
}

/// <summary>An authenticated user with the permissions given; <paramref name="resolving" /> while their permissions are being resolved.</summary>
internal sealed class TestUser(string id, string[]? permissions = null, bool resolving = false) : ICurrentUser
{
    private readonly TestPermissions _authorization = new([.. permissions ?? []], resolving);

    public string Id => id;
    public string? DisplayName => id;
    public bool IsAuthenticated => true;
    public PrincipalKind Kind => PrincipalKind.User;
    public string? TenantId => null;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims { get; } = new Dictionary<string, IReadOnlyList<string>>();
    public IUserAuthorization Authorization => _authorization;
    public IAuthenticationContext Authentication => NullAuthenticationContext.Instance;

    private sealed class TestPermissions(HashSet<string> permissions, bool resolving) : IUserAuthorization
    {
        public IReadOnlyCollection<string> Roles => [];
        IReadOnlySet<string> IUserAuthorization.Permissions => permissions;
        public bool IsResolvingPermissions => resolving;
        public IReadOnlyCollection<string> Groups => [];
        public IReadOnlyCollection<string> Scopes => [];
        public bool HasPermission(string permission) => permissions.Contains(permission);
        public bool HasAnyPermission(IEnumerable<string> names) => names.Any(permissions.Contains);
        public bool HasAllPermissions(IEnumerable<string> names) => names.All(permissions.Contains);
        public bool IsInRole(string role) => false;
        public bool IsInGroup(string group) => false;
        public bool HasScope(string scope) => false;
    }
}
