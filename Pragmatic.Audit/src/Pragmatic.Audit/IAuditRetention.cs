namespace Pragmatic.Audit;

/// <summary>
///     Bounds the trail's growth by discarding whole sealed segments, and declaring the gap.
/// </summary>
/// <remarks>
///     <para>
///         The interface exists so an operation can depend on retention without depending on the store
///         that implements it — and because a concrete type is not injected: the generator cannot tell
///         an injected service from plain state, and says so with <c>PRAG0419</c>.
///     </para>
///     <para>
///         <b>Whole segments, never individual entries.</b> Removing an entry from a sealed segment
///         changes its Merkle root, so the segment would fail verification for ever afterwards and
///         retention would be indistinguishable from tampering — which destroys the one property the
///         trail is for.
///     </para>
/// </remarks>
public interface IAuditRetention
{
    /// <summary>
    ///     Discards sealed segments that opened longer ago than <paramref name="retention" />.
    /// </summary>
    /// <param name="retention">How much history to keep.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The declared gap, or <see langword="null" /> when nothing was old enough.</returns>
    Task<PrunedRange?> PruneOlderThanAsync(TimeSpan retention, CancellationToken ct = default);
}
