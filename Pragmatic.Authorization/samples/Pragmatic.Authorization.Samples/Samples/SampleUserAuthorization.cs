using Pragmatic.Identity;

namespace Pragmatic.Authorization.Samples.Samples;

/// <summary>
///     Minimal <see cref="IUserAuthorization"/> implementation for the authorization samples.
///     Supports prefix wildcard permission matching (e.g., "booking.*" and "*").
/// </summary>
internal sealed class SampleUserAuthorization(
    IEnumerable<string> roles,
    IEnumerable<string> permissions,
    IEnumerable<string> groups,
    IEnumerable<string> scopes)
    : IUserAuthorization
{
    private readonly HashSet<string> _roles = new(roles, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _permissions = new(permissions, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _groups = new(groups, StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _scopes = new(scopes, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Roles => _roles;
    public IReadOnlySet<string> Permissions => _permissions;
    public IReadOnlyCollection<string> Groups => _groups;
    public IReadOnlyCollection<string> Scopes => _scopes;

    public bool HasPermission(string permission)
    {
        if (_permissions.Contains(permission))
            return true;

        foreach (var p in _permissions)
        {
            if (p == "*")
                return true;

            if (p.EndsWith(".*", StringComparison.Ordinal)
                && permission.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public bool HasAnyPermission(IEnumerable<string> permissions) => permissions.Any(HasPermission);

    public bool HasAllPermissions(IEnumerable<string> permissions) => permissions.All(HasPermission);

    public bool IsInRole(string role) => _roles.Contains(role);

    public bool IsInGroup(string group) => _groups.Contains(group);

    public bool HasScope(string scope) => _scopes.Contains(scope);
}
