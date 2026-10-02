using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Async logical NOT of a policy. Inverts the evaluation result.
/// </summary>
internal sealed class AsyncNotPolicy(AsyncResourcePolicy inner) : AsyncResourcePolicy
{
    internal AsyncResourcePolicy Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public override async ValueTask<bool> EvaluateAsync(ICurrentUser user, CancellationToken ct = default)
        => !await Inner.EvaluateAsync(user, ct).ConfigureAwait(false);
}
