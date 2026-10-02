namespace Pragmatic.Privacy;

/// <summary>
///     Erases everything held about a subject that is not under an obligation to keep.
/// </summary>
/// <remarks>
///     The interface exists so an operation can depend on erasure: the generator does not inject a
///     concrete type, because it cannot tell an injected service from plain state (<c>PRAG0419</c>).
///     Implemented by <see cref="ErasureOrchestrator" />, which owns the order the steps run in.
/// </remarks>
public interface ISubjectErasure
{
    /// <inheritdoc cref="ErasureOrchestrator.EraseAsync" />
    ValueTask<ErasureOutcome> EraseAsync(
        string subjectRef,
        Func<string, CancellationToken, ValueTask<bool>>? destroyKeyAsync = null,
        CancellationToken ct = default);
}
