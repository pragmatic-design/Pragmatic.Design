using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Async logical OR combination of two policies. At least one must evaluate to true.
/// </summary>
internal sealed class AsyncOrPolicy(AsyncResourcePolicy left, AsyncResourcePolicy right) : AsyncResourcePolicy
{
    internal AsyncResourcePolicy Left { get; } = left ?? throw new ArgumentNullException(nameof(left));
    internal AsyncResourcePolicy Right { get; } = right ?? throw new ArgumentNullException(nameof(right));

    public override async ValueTask<bool> EvaluateAsync(ICurrentUser user, CancellationToken ct = default)
    {
        // Short-circuit: if left is true, skip right
        if (await Left.EvaluateAsync(user, ct).ConfigureAwait(false))
            return true;

        return await Right.EvaluateAsync(user, ct).ConfigureAwait(false);
    }
}
