using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Requires the user to belong to a specific group.
/// </summary>
internal sealed class GroupPolicy(string group) : ResourcePolicy
{
    internal string Group { get; } = group ?? throw new ArgumentNullException(nameof(group));

    public override bool Evaluate(ICurrentUser user) => user.Authorization.IsInGroup(Group);
}
