using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Requires the user to have a specific <see cref="PrincipalKind" />.
/// </summary>
internal sealed class PrincipalKindPolicy(PrincipalKind kind) : ResourcePolicy
{
    internal PrincipalKind Kind { get; } = kind;

    public override bool Evaluate(ICurrentUser user) => user.Kind == Kind;
}
