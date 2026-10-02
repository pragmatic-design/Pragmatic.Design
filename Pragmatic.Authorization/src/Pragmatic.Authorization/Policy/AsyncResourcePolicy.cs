using Pragmatic.Authorization.Policy.Policies;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy;

/// <summary>
///     Async variant of <see cref="ResourcePolicy" /> for policies that require I/O
///     (e.g., external permission service checks, database lookups).
/// </summary>
/// <remarks>
///     Sync <see cref="ResourcePolicy" /> instances are implicitly convertible to async
///     via <see cref="SyncToAsyncPolicy" />.
/// </remarks>
public abstract class AsyncResourcePolicy
{
    /// <summary>
    ///     Evaluates whether the given user satisfies this policy asynchronously.
    /// </summary>
    public abstract ValueTask<bool> EvaluateAsync(ICurrentUser user, CancellationToken ct = default);

    // =========================================================================
    // Composition
    // =========================================================================

    /// <summary>Combines this policy with another using logical AND.</summary>
    public AsyncResourcePolicy And(AsyncResourcePolicy other) => new AsyncAndPolicy(this, other);

    /// <summary>Combines this policy with another using logical OR.</summary>
    public AsyncResourcePolicy Or(AsyncResourcePolicy other) => new AsyncOrPolicy(this, other);

    /// <summary>Negates this policy.</summary>
    public AsyncResourcePolicy Not() => new AsyncNotPolicy(this);

    /// <summary>Logical AND operator for async policy composition.</summary>
    public static AsyncResourcePolicy operator &(AsyncResourcePolicy left, AsyncResourcePolicy right) => left.And(right);

    /// <summary>Logical OR operator for async policy composition.</summary>
    public static AsyncResourcePolicy operator |(AsyncResourcePolicy left, AsyncResourcePolicy right) => left.Or(right);

    /// <summary>Logical NOT operator for async policy negation.</summary>
    public static AsyncResourcePolicy operator !(AsyncResourcePolicy policy) => policy.Not();

    // =========================================================================
    // Conversion
    // =========================================================================

    /// <summary>
    ///     Implicit conversion from sync to async policy.
    /// </summary>
    public static implicit operator AsyncResourcePolicy(ResourcePolicy sync) => new SyncToAsyncPolicy(sync);

    // =========================================================================
    // Factory methods
    // =========================================================================

    /// <summary>
    ///     Creates an async policy that delegates to an external async check.
    ///     Not serializable.
    /// </summary>
    public static AsyncResourcePolicy RequireExternalPermission(
        Func<ICurrentUser, CancellationToken, ValueTask<bool>> check)
        => new AsyncDelegatePolicy(check);
}
