using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Requires the user to have a specific permission.
/// </summary>
internal sealed class PermissionPolicy(string permission) : ResourcePolicy
{
    internal string Permission { get; } = permission ?? throw new ArgumentNullException(nameof(permission));

    public override bool Evaluate(ICurrentUser user) => user.Authorization.HasPermission(Permission);
}
