using Pragmatic.Authorization;

namespace Pragmatic.Persistence.Tests.Fakes;

/// <summary>
///     An <see cref="IUserAuthorization" /> holding a fixed set of permissions.
/// </summary>
/// <remarks>
///     Shared rather than private to one test class: the filter provider consults it whenever a
///     permission-based filter is in play, so more than one suite needs it, and a second hand-written
///     copy is how two fakes come to disagree about what "has no permissions" means.
/// </remarks>
internal sealed class FakeAuthorization(string[]? permissions = null) : IUserAuthorization
{
    public IReadOnlyCollection<string> Roles => [];

    public IReadOnlySet<string> Permissions => permissions?.ToHashSet() ?? new HashSet<string>();

    public IReadOnlyCollection<string> Groups => [];

    public IReadOnlyCollection<string> Scopes => [];

    public bool HasPermission(string permission) => permissions?.Contains(permission) == true;

    public bool HasAnyPermission(IEnumerable<string> perms) => perms.Any(HasPermission);

    public bool HasAllPermissions(IEnumerable<string> perms) => perms.All(HasPermission);

    public bool IsInRole(string role) => false;

    public bool IsInGroup(string group) => false;

    public bool HasScope(string scope) => false;
}
