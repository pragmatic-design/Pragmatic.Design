namespace Pragmatic.Cryptography;

/// <summary>Outcome of destroying a subject's key.</summary>
/// <param name="SubjectRef">The subject whose key was destroyed.</param>
/// <param name="KeyId">
///     The id of the destroyed key. It survives destruction on purpose: it is what a reader finds in a
///     ciphertext header, and matching it against a destroyed record is what tells "erased" apart from
///     "tampered".
/// </param>
/// <param name="DestroyedAt">When the key was destroyed — the original time if it already was.</param>
/// <param name="AlreadyDestroyed">
///     <see langword="true" /> when the key had already been destroyed by an earlier call. Destruction is
///     idempotent so a retried erasure does not fail.
/// </param>
public sealed record KeyDestructionReport(
    string SubjectRef,
    string KeyId,
    DateTimeOffset DestroyedAt,
    bool AlreadyDestroyed);
