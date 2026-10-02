namespace Pragmatic.Cryptography;

/// <summary>
///     Resolves the <see cref="EncryptionKeyRing" /> (current + previous keys) used to encrypt and decrypt
///     secrets at rest. The default implementation adapts the single-key <see cref="IEncryptionKeyProvider" />
///     and reads any previous keys from options, so existing hosts keep working with zero changes; hosts that
///     rotate keys supply a ring with previous keys still present for decryption.
/// </summary>
public interface IEncryptionKeyRingProvider
{
    /// <summary>Resolves the key ring. Resolution may be asynchronous (e.g. a secret-store round trip).</summary>
    ValueTask<EncryptionKeyRing> GetKeyRingAsync(CancellationToken ct = default);
}
