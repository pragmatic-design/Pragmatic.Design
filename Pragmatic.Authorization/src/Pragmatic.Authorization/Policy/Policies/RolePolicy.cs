using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Requires the user to belong to a specific role.
/// </summary>
internal sealed class RolePolicy(string role) : ResourcePolicy
{
    internal string Role { get; } = role ?? throw new ArgumentNullException(nameof(role));

    public override bool Evaluate(ICurrentUser user) => user.Authorization.IsInRole(Role);
}
