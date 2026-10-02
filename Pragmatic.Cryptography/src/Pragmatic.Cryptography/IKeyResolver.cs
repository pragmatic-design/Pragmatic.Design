namespace Pragmatic.Cryptography;

/// <summary>
///     Resolves an encryption key by the id embedded in a ciphertext header.
/// </summary>
/// <remarks>
///     <para>
///         This is all the decryption path needs. A reader holding a packed value knows the key id but
///         not who the value belongs to, so resolution is by id and nothing else — which is exactly what
///         lets an EF value converter decrypt a column without knowing anything about the row.
///     </para>
///     <para>
///         <see cref="EncryptionKeyRing" /> is the in-memory implementation, for the handful of keys that
///         come from configuration. Per-subject keys are far too many to hold in memory and are resolved
///         from a store on demand.
///     </para>
/// </remarks>
public interface IKeyResolver
{
    /// <summary>
    ///     Finds the key with the given id, or <see langword="null" /> when this resolver does not have it.
    /// </summary>
    /// <remarks>
    ///     A <see langword="null" /> result means "unknown here" — it does not distinguish a key that
    ///     never existed from one that was destroyed. Callers that need that distinction ask the store.
    /// </remarks>
    ValueTask<EncryptionKey?> FindByIdAsync(string keyId, CancellationToken ct = default);
}
