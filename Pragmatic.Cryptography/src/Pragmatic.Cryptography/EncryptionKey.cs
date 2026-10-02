using System.Security.Cryptography;

namespace Pragmatic.Cryptography;

/// <summary>
///     A single AES-256 key together with a short, deterministic <see cref="KeyId" /> derived from the key
///     material itself (no manual naming). Ciphertext written in the versioned format embeds this id so a
///     reader can pick the right key from the <see cref="EncryptionKeyRing" /> — enabling key rotation.
/// </summary>
/// <param name="KeyId">Stable 8-hex-character fingerprint of <paramref name="Material" />.</param>
/// <param name="Material">The 32-byte AES-256 key. Owned by the encryptor and zeroed on dispose.</param>
public sealed record EncryptionKey(string KeyId, byte[] Material)
{
    /// <summary>Number of ASCII characters in a key id (first 4 fingerprint bytes, hex-encoded).</summary>
    internal const int KeyIdLength = 8;

    /// <summary>
    ///     Builds a key from raw material, computing its <see cref="KeyId" /> fingerprint. Validates the
    ///     AES-256 length (32 bytes).
    /// </summary>
    public static EncryptionKey FromMaterial(byte[] material)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (material.Length != 32)
            throw new ArgumentException(
                $"Secret encryption key must be exactly 32 bytes (256 bits) for AES-256-GCM, but was {material.Length}.",
                nameof(material));

        return new EncryptionKey(Fingerprint(material), material);
    }

    /// <summary>Deterministic id = first 4 bytes of SHA-256(material), lowercase hex (8 chars).</summary>
    private static string Fingerprint(byte[] material)
    {
        var hash = SHA256.HashData(material);
        return Convert.ToHexString(hash, 0, KeyIdLength / 2).ToLowerInvariant();
    }
}
