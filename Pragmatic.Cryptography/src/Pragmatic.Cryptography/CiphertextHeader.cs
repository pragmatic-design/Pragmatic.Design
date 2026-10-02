namespace Pragmatic.Cryptography;

/// <summary>
///     Reads the header of a packed ciphertext: <c>[0x01][keyIdLen][keyId][nonce][tag][ciphertext]</c>.
/// </summary>
/// <remarks>
///     The key id is public on purpose. A reader holding a value needs to know which key produced it
///     before it can resolve that key — and, once keys can be destroyed, before it can tell an erased
///     value apart from a tampered one. Keeping the parsing here rather than duplicating it at each call
///     site is what stops the format from quietly diverging.
/// </remarks>
public static class CiphertextHeader
{
    internal const byte Version1 = 0x01;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;

    /// <summary>
    ///     Reads the key id from a versioned ciphertext.
    /// </summary>
    /// <returns>
    ///     <see langword="false" /> when the value carries no version header — either a value written
    ///     before the format was versioned, or not a ciphertext at all.
    /// </returns>
    public static bool TryReadKeyId(byte[] packed, out string keyId)
        => TryReadVersioned(packed, out keyId, out _);

    /// <summary>
    ///     Parses the <c>[0x01][keyIdLen][keyId][…]</c> header, returning the key id and the framed body
    ///     (<c>[nonce][tag][ciphertext]</c>).
    /// </summary>
    internal static bool TryReadVersioned(byte[] packed, out string keyId, out ReadOnlyMemory<byte> body)
    {
        keyId = string.Empty;
        body = default;

        if (packed is null || packed.Length < 2 || packed[0] != Version1)
            return false;

        int keyIdLen = packed[1];
        var headerLen = 2 + keyIdLen;
        if (keyIdLen == 0 || packed.Length < headerLen + NonceSize + TagSize)
            return false;

        keyId = System.Text.Encoding.ASCII.GetString(packed, 2, keyIdLen);
        body = packed.AsMemory(headerLen);
        return true;
    }
}
