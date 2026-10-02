using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Async policy backed by an external delegate. Not serializable.
///     Useful for checks that require I/O (external service, database).
/// </summary>
internal sealed class AsyncDelegatePolicy(
    Func<ICurrentUser, CancellationToken, ValueTask<bool>> check) : AsyncResourcePolicy
{
    internal Func<ICurrentUser, CancellationToken, ValueTask<bool>> Check { get; } =
        check ?? throw new ArgumentNullException(nameof(check));

    public override ValueTask<bool> EvaluateAsync(ICurrentUser user, CancellationToken ct = default)
        => Check(user, ct);
}
