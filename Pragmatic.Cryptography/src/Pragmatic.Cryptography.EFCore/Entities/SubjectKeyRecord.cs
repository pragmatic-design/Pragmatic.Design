namespace Pragmatic.Cryptography.EFCore.Entities;

/// <summary>
///     A per-subject encryption key, wrapped by the master key ring.
/// </summary>
/// <remarks>
///     The row survives destruction: <see cref="WrappedKey" /> is cleared and <see cref="DestroyedAt" />
///     is stamped, but <see cref="KeyId" /> stays. That is what makes a destroyed key distinguishable
///     from an unknown one — without it, every read of erased data would look like tampering, and a real
///     tampering signal would drown in the noise.
/// </remarks>
public sealed class SubjectKeyRecord
{
    /// <summary>Opaque reference to the subject — a pseudonym, never an identity.</summary>
    public required string SubjectRef { get; set; }

    /// <summary>
    ///     Fingerprint of the key material. This is what a ciphertext header carries, so it is the
    ///     column reads look up by.
    /// </summary>
    public required string KeyId { get; set; }

    /// <summary>
    ///     The subject's key, encrypted under the master ring with the subject reference as associated
    ///     data so a wrapped key cannot be moved to another subject. <see langword="null" /> once destroyed.
    /// </summary>
    public byte[]? WrappedKey { get; set; }

    /// <summary>When the key was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the key was destroyed; <see langword="null" /> while it is live.</summary>
    public DateTimeOffset? DestroyedAt { get; set; }
}
