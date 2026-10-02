using Pragmatic.Authorization;

namespace Pragmatic.Integration.Tests.Infrastructure;

/// <summary>The permissions of a <see cref="TestCaller" />, and no roles, groups or scopes.</summary>
public sealed class TestCallerAuthorization(HashSet<string> permissions) : IUserAuthorization
{
    /// <inheritdoc />
    public IReadOnlyCollection<string> Roles => [];

    /// <inheritdoc />
    public IReadOnlySet<string> Permissions => permissions;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Groups => [];

    /// <inheritdoc />
    public IReadOnlyCollection<string> Scopes => [];

    /// <inheritdoc />
    public bool HasPermission(string permission) => permissions.Contains(permission);

    /// <inheritdoc />
    public bool HasAnyPermission(IEnumerable<string> perms) => perms.Any(permissions.Contains);

    /// <inheritdoc />
    public bool HasAllPermissions(IEnumerable<string> perms) => perms.All(permissions.Contains);

    /// <inheritdoc />
    public bool IsInRole(string role) => false;

    /// <inheritdoc />
    public bool IsInGroup(string group) => false;

    /// <inheritdoc />
    public bool HasScope(string scope) => false;
}
