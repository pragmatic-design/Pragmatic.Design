namespace Pragmatic.Audit;

/// <summary>
///     Seals the trail's due segments, whatever store holds them: what <see cref="AuditSealingWorker" /> runs on a timer.
/// </summary>
/// <remarks>
///     Verification checks sealed segments only, so a trail nobody seals verifies as intact whatever happened to it: every store comes with
///     its sealer, and the worker asks for the contract rather than one store's class.
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IAuditSealer
{
    /// <summary>Seals every open segment past its window and grace period, oldest first.</summary>
    /// <returns>The ids of the segments sealed, in the order they were chained.</returns>
    Task<IReadOnlyList<string>> SealDueSegmentsAsync(CancellationToken ct = default);
}
