using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Logical NOT of a policy. Inverts the evaluation result.
/// </summary>
internal sealed class NotPolicy(ResourcePolicy inner) : ResourcePolicy
{
    internal ResourcePolicy Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public override bool Evaluate(ICurrentUser user) => !Inner.Evaluate(user);
}
