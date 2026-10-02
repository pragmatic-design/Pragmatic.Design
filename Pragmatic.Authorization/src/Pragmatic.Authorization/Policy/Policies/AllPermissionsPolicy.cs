using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Requires the user to have all of the specified permissions (AND logic).
/// </summary>
internal sealed class AllPermissionsPolicy : ResourcePolicy
{
    internal string[] Permissions { get; }

    public AllPermissionsPolicy(string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        // An empty set would make HasAllPermissions vacuously true (`.All` over an empty sequence),
        // silently granting access to any authenticated user. Reject it as a misconfiguration — matches
        // PragmaticPermissionRequirement and closes the PolicySerializer(Values: []) fail-open path.
        if (permissions.Length == 0)
            throw new ArgumentException(
                "At least one permission must be specified; an empty set would vacuously grant access.",
                nameof(permissions));
        Permissions = permissions;
    }

    public override bool Evaluate(ICurrentUser user) => user.Authorization.HasAllPermissions(Permissions);
}
