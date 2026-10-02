namespace Pragmatic.Cryptography;

/// <summary>
///     Owns the lifecycle of per-subject encryption keys: one key per opaque subject reference,
///     created on first use and destroyed to make that subject's data permanently unreadable.
/// </summary>
/// <remarks>
///     <para>
///         The store deliberately knows nothing about what a "subject" is. It receives an opaque
///         <c>subjectRef</c> — a pseudonym, never an identity — so this module stays out of the privacy
///         domain and remains usable by anything that needs per-entity keys.
///     </para>
///     <para>
///         Destroying a key is the point: with the key gone the ciphertext is unreadable by anyone,
///         including whoever holds the backups. That is why it cannot be undone, and why
///         <see cref="GetOrCreateAsync" /> refuses to resurrect a destroyed subject.
///     </para>
/// </remarks>
public interface ISubjectKeyStore
{
    /// <summary>
    ///     Returns the subject's key, creating it on first use.
    /// </summary>
    /// <exception cref="SubjectKeyDestroyedException">
    ///     The subject's key was destroyed. It is never recreated: doing so would let new data be written
    ///     under a reference that has already been reported as erased. A subject that returns is a new
    ///     subject, with a new reference.
    /// </exception>
    ValueTask<EncryptionKey> GetOrCreateAsync(string subjectRef, CancellationToken ct = default);

    /// <summary>
    ///     Destroys the subject's key. Idempotent: destroying an already-destroyed subject reports the
    ///     original destruction rather than failing.
    /// </summary>
    ValueTask<KeyDestructionReport> DestroyAsync(string subjectRef, CancellationToken ct = default);

    /// <summary>
    ///     Reports what is known about a key id read from a ciphertext header.
    /// </summary>
    /// <remarks>
    ///     <see cref="IKeyResolver.FindByIdAsync" /> answers "can you decrypt this" and returns nothing
    ///     for both a destroyed key and one that never existed. Telling those apart is what makes an
    ///     erased value distinguishable from a tampered one, and it is the reason the destroyed record is
    ///     kept rather than deleted.
    /// </remarks>
    ValueTask<SubjectKeyStatus> GetStatusByKeyIdAsync(string keyId, CancellationToken ct = default);
}
