using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Bridges a sync <see cref="ResourcePolicy" /> into the async policy tree.
/// </summary>
internal sealed class SyncToAsyncPolicy(ResourcePolicy inner) : AsyncResourcePolicy
{
    internal ResourcePolicy Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public override ValueTask<bool> EvaluateAsync(ICurrentUser user, CancellationToken ct = default)
        => ValueTask.FromResult(Inner.Evaluate(user));
}
