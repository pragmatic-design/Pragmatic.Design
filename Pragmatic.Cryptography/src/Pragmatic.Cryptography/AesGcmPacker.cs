using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Cryptography;

/// <summary>
///     Packs and unpacks the versioned ciphertext format around a caller-supplied <see cref="AesGcm" />.
/// </summary>
/// <remarks>
///     Taking the cipher rather than the key material lets the two callers differ where they need to and
///     agree where it matters. <see cref="AesGcmSecretEncryptor" /> keeps one cipher per ring key for the
///     lifetime of the process; the per-subject path builds one per operation, because it cannot hold
///     millions of keys in memory. Both go through this type, so the format is defined exactly once.
/// </remarks>
internal static class AesGcmPacker
{
    /// <summary>Packs <c>[version][keyIdLen][keyId][nonce][tag][ciphertext]</c>.</summary>
    public static byte[] Pack(AesGcm cipher, string keyId, byte[] plain, byte[] aad)
    {
        var nonce = new byte[CiphertextHeader.NonceSize];
        RandomNumberGenerator.Fill(nonce);

        var cipherText = new byte[plain.Length];
        var tag = new byte[CiphertextHeader.TagSize];
        cipher.Encrypt(nonce, plain, cipherText, tag, aad);

        var keyIdBytes = Encoding.ASCII.GetBytes(keyId);

        var result = new byte[2 + keyIdBytes.Length + CiphertextHeader.NonceSize + CiphertextHeader.TagSize + cipherText.Length];
        var offset = 0;
        result[offset++] = CiphertextHeader.Version1;
        result[offset++] = (byte)keyIdBytes.Length;
        keyIdBytes.CopyTo(result, offset); offset += keyIdBytes.Length;
        nonce.CopyTo(result, offset); offset += CiphertextHeader.NonceSize;
        tag.CopyTo(result, offset); offset += CiphertextHeader.TagSize;
        cipherText.CopyTo(result, offset);

        return result;
    }

    /// <summary>Decrypts a <c>[nonce][tag][ciphertext]</c> body; false on authentication failure.</summary>
    public static bool TryUnpackFramed(AesGcm cipher, ReadOnlyMemory<byte> body, byte[] aad, out byte[] plain)
    {
        var span = body.Span;
        if (span.Length < CiphertextHeader.NonceSize + CiphertextHeader.TagSize)
        {
            plain = [];
            return false;
        }

        var nonce = span.Slice(0, CiphertextHeader.NonceSize);
        var tag = span.Slice(CiphertextHeader.NonceSize, CiphertextHeader.TagSize);
        var cipherText = span.Slice(CiphertextHeader.NonceSize + CiphertextHeader.TagSize);

        var plainBytes = new byte[cipherText.Length];
        try
        {
            cipher.Decrypt(nonce, cipherText, tag, plainBytes, aad);
            plain = plainBytes;
            return true;
        }
        catch (CryptographicException)
        {
            plain = [];
            return false;
        }
    }
}
