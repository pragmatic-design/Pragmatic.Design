namespace Pragmatic.Actions.Compensation;

/// <summary>
///     Holds the undos of steps that have already committed in this request, so a later failure can
///     roll them back in reverse order.
/// </summary>
/// <remarks>
///     Scoped to the request. Nothing here survives the process: see
///     <c>[UndoWith&lt;T&gt;]</c> for what that rules out.
/// </remarks>
public interface ICompensationScope
{
    /// <summary>
    ///     Records where the current invocation starts, so it compensates only what it caused.
    /// </summary>
    /// <returns>An opaque position to pass back to <see cref="CompensateFromAsync" />.</returns>
    int Mark();

    /// <summary>Adds an undo for a step that has just committed.</summary>
    /// <param name="entry">The committed step and its undo.</param>
    void Register(CompensationEntry entry);

    /// <summary>
    ///     Runs every undo registered after <paramref name="mark" />, most recent first, and removes them.
    /// </summary>
    /// <param name="mark">The position returned by <see cref="Mark" /> at the start of the invocation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     <c>null</c> when every undo succeeded, including when there was nothing to undo. Otherwise the
    ///     first failure — and the remaining undos still run: leaving them queued behind a failure would
    ///     strand work the caller has no other way to reach.
    /// </returns>
    Task<CompensationFailure?> CompensateFromAsync(int mark, CancellationToken ct = default);
}
