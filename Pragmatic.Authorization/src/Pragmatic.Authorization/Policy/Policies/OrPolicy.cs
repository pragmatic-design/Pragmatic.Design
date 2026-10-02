using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Logical OR combination of two policies. At least one must evaluate to true.
/// </summary>
internal sealed class OrPolicy(ResourcePolicy left, ResourcePolicy right) : ResourcePolicy
{
    internal ResourcePolicy Left { get; } = left ?? throw new ArgumentNullException(nameof(left));
    internal ResourcePolicy Right { get; } = right ?? throw new ArgumentNullException(nameof(right));

    public override bool Evaluate(ICurrentUser user) => Left.Evaluate(user) || Right.Evaluate(user);
}
