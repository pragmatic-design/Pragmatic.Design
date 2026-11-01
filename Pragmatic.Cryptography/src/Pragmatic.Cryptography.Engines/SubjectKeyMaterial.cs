using System.Security.Cryptography;

namespace Pragmatic.Cryptography.Engines;

/// <summary>
///     A subject's key as both engines store it, the way EF Core's <c>EfCoreSubjectKeyStore</c> does: fresh material wrapped by the master key ring
///     with the subject reference as associated data (a wrapped key lifted from one subject cannot be planted on another), and unwrapped only
///     while it is live and its id still fingerprints its material.
/// </summary>
internal static class SubjectKeyMaterial
{
    private const int KeySizeBytes = 32;

    /// <summary>A new key for <paramref name="subjectRef" />, and its material wrapped for storage.</summary>
    public static (EncryptionKey Key, byte[] Wrapped) Create(ISecretEncryptor master, string subjectRef)
    {
        var material = RandomNumberGenerator.GetBytes(KeySizeBytes);
        return (EncryptionKey.FromMaterial(material), master.EncryptBytes(material, subjectRef));
    }

    /// <summary>The stored key, unwrapped; throws when it was destroyed, is inconsistent, or no longer authenticates.</summary>
    public static EncryptionKey Unwrap(ISecretEncryptor master, string subjectRef, string keyId, byte[]? wrapped, DateTimeOffset? destroyedAt)
    {
        if (destroyedAt is { } destroyed)
            throw new SubjectKeyDestroyedException(subjectRef, destroyed);

        if (wrapped is null)
            throw new InvalidOperationException(
                $"Subject '{subjectRef}' has no wrapped key but is not marked destroyed. The row is inconsistent and unwrapping it would be a guess.");

        if (!master.TryDecryptBytes(wrapped, out var material, subjectRef))
            throw new CryptographicException(
                $"Could not unwrap the key for subject '{subjectRef}': the master key ring no longer authenticates it. The master key changed "
                + "without re-wrapping, or the row was tampered with.");

        var key = EncryptionKey.FromMaterial(material);

        // The id is a fingerprint of the material: reads resolve by id, so an id that no longer describes its key would hand out the wrong one.
        if (!string.Equals(key.KeyId, keyId, StringComparison.Ordinal))
            throw new CryptographicException(
                $"Subject '{subjectRef}' stores key id '{keyId}' but its wrapped material fingerprints to '{key.KeyId}'. The row is inconsistent.");

        return key;
    }

    /// <summary>The report of a destruction that was asked of a subject with no key: it would report an erasure that did not happen.</summary>
    public static InvalidOperationException NoKey(string subjectRef)
        => new($"No encryption key exists for subject '{subjectRef}'. Destroying a key that was never created would report an erasure that did not happen.");
}
