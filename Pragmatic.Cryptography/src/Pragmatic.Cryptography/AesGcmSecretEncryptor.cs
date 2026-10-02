using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Cryptography;

/// <summary>
///     AES-256-GCM encryption for secrets at rest with key rotation support.
/// </summary>
/// <remarks>
///     <para>
///         New values are written in a versioned, key-tagged format:
///         <c>[0x01 version][1B keyIdLen][keyId][12B nonce][16B tag][ciphertext]</c>. The embedded key id lets
///         a reader pick the exact key from the ring, so the current key can be rotated while previous keys
///         still decrypt older values.
///     </para>
///     <para>
///         Legacy values written before rotation existed have no version prefix
///         (<c>[12B nonce][16B tag][ciphertext]</c>). They are detected by falling through: GCM authentication
///         makes a mis-framed decrypt fail its tag check, so the reader safely tries each ring key under the
///         legacy framing. New writes always use the versioned format.
///     </para>
///     <para>
///         This type owns the key material: keys are held in managed memory for its lifetime and zeroed on
///         <see cref="Dispose" />. It is registered as a singleton, so keys remain resident until shutdown.
///     </para>
/// </remarks>
public sealed class AesGcmSecretEncryptor : ISecretEncryptor, IDisposable
{
    private readonly EncryptionKeyRing _ring;

    // One AesGcm per key id (both encryption of the current key and decryption of any ring key).
    private readonly Dictionary<string, AesGcm> _ciphers;

    public AesGcmSecretEncryptor(EncryptionKeyRing ring)
    {
        ArgumentNullException.ThrowIfNull(ring);

        _ring = ring;
        _ciphers = new Dictionary<string, AesGcm>(StringComparer.Ordinal);
        foreach (var key in ring.All)
            _ciphers[key.KeyId] = new AesGcm(key.Material, CiphertextHeader.TagSize);
    }

    public byte[] Encrypt(string plainText, string? associatedData = null)
        => EncryptBytes(Encoding.UTF8.GetBytes(plainText), associatedData);

    public byte[] EncryptBytes(byte[] plainBytes, string? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(plainBytes);

        var current = _ring.Current;
        var aad = associatedData is null ? [] : Encoding.UTF8.GetBytes(associatedData);

        return AesGcmPacker.Pack(_ciphers[current.KeyId], current.KeyId, plainBytes, aad);
    }

    public string Decrypt(byte[] packed, string? associatedData = null)
    {
        if (TryDecrypt(packed, out var plainText, associatedData))
            return plainText;

        throw new CryptographicException(
            "Unable to decrypt secret: authentication failed under every available key (tampered value, " +
            "wrong associated data, or a key that is no longer in the ring).");
    }

    public bool TryDecrypt(byte[] cipherText, out string plainText, string? associatedData = null)
    {
        if (TryDecryptBytes(cipherText, out var plainBytes, associatedData))
        {
            plainText = Encoding.UTF8.GetString(plainBytes);
            return true;
        }

        plainText = string.Empty;
        return false;
    }

    public bool TryDecryptBytes(byte[] cipherText, out byte[] plain, string? associatedData = null)
    {
        ArgumentNullException.ThrowIfNull(cipherText);
        var aad = associatedData is null ? [] : Encoding.UTF8.GetBytes(associatedData);

        // Versioned format: use the embedded key id to select the exact key.
        if (CiphertextHeader.TryReadVersioned(cipherText, out var keyId, out var body) &&
            _ring.FindById(keyId) is { } tagged &&
            AesGcmPacker.TryUnpackFramed(_ciphers[tagged.KeyId], body, aad, out plain))
            return true;

        // Legacy (unversioned) format, or a versioned value whose key id is unknown: try every ring key.
        foreach (var key in _ring.All)
            if (AesGcmPacker.TryUnpackFramed(_ciphers[key.KeyId], cipherText, aad, out plain))
                return true;

        plain = [];
        return false;
    }

    public void Dispose()
    {
        foreach (var cipher in _ciphers.Values)
            cipher.Dispose();
        foreach (var key in _ring.All)
            CryptographicOperations.ZeroMemory(key.Material);
    }
}
