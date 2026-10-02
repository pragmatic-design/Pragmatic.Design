using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Custom policy backed by a delegate. Not serializable.
/// </summary>
internal sealed class CustomPolicy(Func<ICurrentUser, bool> predicate) : ResourcePolicy
{
    internal Func<ICurrentUser, bool> Predicate { get; } = predicate ?? throw new ArgumentNullException(nameof(predicate));

    public override bool Evaluate(ICurrentUser user) => Predicate(user);
}
