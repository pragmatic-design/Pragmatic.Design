using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Async logical AND combination of two policies. Both must evaluate to true.
/// </summary>
internal sealed class AsyncAndPolicy(AsyncResourcePolicy left, AsyncResourcePolicy right) : AsyncResourcePolicy
{
    internal AsyncResourcePolicy Left { get; } = left ?? throw new ArgumentNullException(nameof(left));
    internal AsyncResourcePolicy Right { get; } = right ?? throw new ArgumentNullException(nameof(right));

    public override async ValueTask<bool> EvaluateAsync(ICurrentUser user, CancellationToken ct = default)
    {
        // Short-circuit: if left is false, skip right
        if (!await Left.EvaluateAsync(user, ct).ConfigureAwait(false))
            return false;

        return await Right.EvaluateAsync(user, ct).ConfigureAwait(false);
    }
}
