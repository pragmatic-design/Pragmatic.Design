using Pragmatic.Identity;

namespace Pragmatic.Authorization.Tests.Unit;

/// <summary>
///     Shared test doubles for ResourcePolicy tests.
/// </summary>
internal static class PolicyTestHelpers
{
    internal static ICurrentUser CreateUser(
        bool isAuthenticated = true,
        PrincipalKind kind = PrincipalKind.User,
        IUserAuthorization? authorization = null,
        Dictionary<string, IReadOnlyList<string>>? claims = null,
        bool mfaAuthenticated = false)
    {
        return new PolicyTestUser(
            isAuthenticated,
            kind,
            authorization ?? new FakeUserAuthorization(),
            claims ?? new Dictionary<string, IReadOnlyList<string>>(),
            mfaAuthenticated ? MfaAuthenticationContext.Instance : NullAuthenticationContext.Instance);
    }

    /// <summary>A context that reports MFA as completed; everything else matches the null context.</summary>
    private sealed class MfaAuthenticationContext : IAuthenticationContext
    {
        internal static readonly MfaAuthenticationContext Instance = new();

        public string? Scheme => null;
        public string? Protocol => null;
        public string? Issuer => null;
        public string? Subject => null;
        public bool IsMfaAuthenticated => true;
        public DateTimeOffset? AuthenticatedAt => null;
        public DateTimeOffset? ExpiresAt => null;
        public string? ExternalIdentityKey => null;
    }

    private sealed class PolicyTestUser(
        bool isAuthenticated,
        PrincipalKind kind,
        IUserAuthorization authorization,
        Dictionary<string, IReadOnlyList<string>> claims,
        IAuthenticationContext authentication) : ICurrentUser
    {
        public string Id => "test-user";
        public string? DisplayName => "Test User";
        public bool IsAuthenticated => isAuthenticated;
        public PrincipalKind Kind => kind;
        public string? TenantId => null;
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims => claims;
        public IUserAuthorization Authorization => authorization;
        public IAuthenticationContext Authentication => authentication;
    }

    internal sealed class FakeUserAuthorization(
        IEnumerable<string>? permissions = null,
        IEnumerable<string>? roles = null,
        IEnumerable<string>? groups = null)
        : IUserAuthorization
    {
        private readonly HashSet<string> _permissions = permissions is not null
            ? new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _roles = roles is not null
            ? new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _groups = groups is not null
            ? new HashSet<string>(groups, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> Roles => _roles;
        public IReadOnlySet<string> Permissions => _permissions;
        public IReadOnlyCollection<string> Groups => _groups;
        public IReadOnlyCollection<string> Scopes => Array.Empty<string>();
        public bool HasPermission(string permission) => _permissions.Contains(permission);
        public bool HasAnyPermission(IEnumerable<string> permissions) => permissions.Any(_permissions.Contains);
        public bool HasAllPermissions(IEnumerable<string> permissions) => permissions.All(_permissions.Contains);
        public bool IsInRole(string role) => _roles.Contains(role);
        public bool IsInGroup(string group) => _groups.Contains(group);
        public bool HasScope(string scope) => false;
    }
}
