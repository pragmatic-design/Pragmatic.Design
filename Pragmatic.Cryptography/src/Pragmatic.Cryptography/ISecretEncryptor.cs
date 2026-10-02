namespace Pragmatic.Cryptography;

/// <summary>
///     Encryption at rest for sensitive values, whatever stores them.
///     The encryption key is resolved by an <see cref="IEncryptionKeyProvider" /> — from an environment
///     variable, local config, or a secret store — and never from the same store as the ciphertext.
/// </summary>
public interface ISecretEncryptor
{
    /// <summary>
    ///     Encrypts <paramref name="plainText"/>. When <paramref name="associatedData"/> is supplied it
    ///     is authenticated (but not encrypted) and must be provided again to decrypt — bind it to the
    ///     secret's identity (key/tenant) so ciphertext cannot be moved between secrets.
    /// </summary>
    byte[] Encrypt(string plainText, string? associatedData = null);

    /// <summary>Decrypts data produced by <see cref="Encrypt"/> with the same associated data.</summary>
    string Decrypt(byte[] cipherText, string? associatedData = null);

    /// <summary>
    ///     Attempts to decrypt the supplied ciphertext without throwing on failure.
    /// </summary>
    /// <param name="cipherText">The packed encrypted data to decrypt.</param>
    /// <param name="plainText">The decrypted plaintext on success; otherwise an empty string.</param>
    /// <param name="associatedData">The associated data the ciphertext was authenticated with, if any.</param>
    /// <returns>
    ///     <see langword="true" /> if decryption succeeded; <see langword="false" /> if the data is tampered,
    ///     malformed, or was encrypted with a different key/associated data (authentication failed).
    /// </returns>
    bool TryDecrypt(byte[] cipherText, out string plainText, string? associatedData = null);

    /// <summary>
    ///     Encrypts raw bytes. Use this rather than the string overload for material that is not text —
    ///     above all for key material, which must never sit in a managed <see cref="string" />, since a
    ///     string cannot be zeroed once written.
    /// </summary>
    byte[] EncryptBytes(byte[] plain, string? associatedData = null);

    /// <summary>
    ///     Attempts to decrypt data produced by <see cref="EncryptBytes" /> without throwing on failure.
    /// </summary>
    /// <param name="cipherText">The packed encrypted data to decrypt.</param>
    /// <param name="plain">The decrypted bytes on success; otherwise empty.</param>
    /// <param name="associatedData">The associated data the ciphertext was authenticated with, if any.</param>
    /// <returns>
    ///     <see langword="true" /> if decryption succeeded; <see langword="false" /> if the data is tampered,
    ///     malformed, or was encrypted with a different key/associated data (authentication failed).
    /// </returns>
    bool TryDecryptBytes(byte[] cipherText, out byte[] plain, string? associatedData = null);
}
