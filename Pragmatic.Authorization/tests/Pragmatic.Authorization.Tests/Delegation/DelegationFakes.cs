using Pragmatic.Authorization.Delegation;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Tests.Delegation;

internal sealed class FakeDelegation(string subjectId, string actorId, DelegationPolicy policy) : IDelegationContext
{
    public string SubjectId { get; } = subjectId;
    public string ActorId { get; } = actorId;
    public ActorKind ActorKind => ActorKind.Agent;
    public DelegationPolicy Policy { get; } = policy;
    public string? Purpose => "test";
    public string? GrantId => "g-1";
    public DateTimeOffset? ExpiresAt => Expiry;
    public IReadOnlyList<string> Chain => ActorChain;

    public DateTimeOffset? Expiry { get; init; }
    public string[] ActorChain { get; init; } = [];
}

internal sealed class FakeActorAuthority(string[] actor, string[] granted, string? actorTenant = null)
    : IActorAuthorityResolver
{
    public string? ActorTenantId(IDelegationContext delegation) => actorTenant;

    public IReadOnlySet<string> ActorPermissions(IDelegationContext delegation)
        => new HashSet<string>(actor, StringComparer.Ordinal);

    public IReadOnlySet<string> GrantedPermissions(IDelegationContext delegation)
        => new HashSet<string>(granted, StringComparer.Ordinal);
}

/// <summary>The subject's own authority — what the decorator wraps.</summary>
internal sealed class FakeAuthorization(string[] permissions) : IUserAuthorization
{
    public IReadOnlyCollection<string> Roles => ["manager"];
    public IReadOnlySet<string> Permissions { get; } = new HashSet<string>(permissions, StringComparer.Ordinal);
    public IReadOnlyCollection<string> Groups => ["team-a"];
    public IReadOnlyCollection<string> Scopes => ["api"];

    public bool HasPermission(string permission) => Permissions.Contains(permission);
    public bool HasAnyPermission(IEnumerable<string> permissions) => permissions.Any(HasPermission);
    public bool HasAllPermissions(IEnumerable<string> permissions) => permissions.All(HasPermission);
    public bool IsInRole(string role) => Roles.Contains(role);
    public bool IsInGroup(string group) => Groups.Contains(group);
    public bool HasScope(string scope) => Scopes.Contains(scope);
}

internal sealed class FakeCurrentUser(string id, IDelegationContext? delegation) : ICurrentUser
{
    public string Id { get; } = id;
    public string? DisplayName => "Test";
    public bool IsAuthenticated => true;
    public PrincipalKind Kind => PrincipalKind.User;
    public string? TenantId => "t-1";
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Claims =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    public IDelegationContext? Delegation { get; } = delegation;
    public IUserAuthorization Authorization => throw new NotSupportedException("not used by these tests");
    public IAuthenticationContext Authentication => throw new NotSupportedException("not used by these tests");
}
