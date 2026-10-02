using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Requires the user to have at least one of the specified permissions (OR logic).
/// </summary>
internal sealed class AnyPermissionPolicy : ResourcePolicy
{
    internal string[] Permissions { get; }

    public AnyPermissionPolicy(string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        // An empty set would make HasAnyPermission vacuously false, silently denying everyone with no
        // diagnostic. Reject it as a misconfiguration for symmetry with AllPermissionsPolicy.
        if (permissions.Length == 0)
            throw new ArgumentException(
                "At least one permission must be specified.", nameof(permissions));
        Permissions = permissions;
    }

    public override bool Evaluate(ICurrentUser user) => user.Authorization.HasAnyPermission(Permissions);
}
