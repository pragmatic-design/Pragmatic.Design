namespace Pragmatic.Agent.KV;

/// <summary>
///     Protects secret values stored in the KV store. Values under the <c>secret/</c> namespace are
///     encrypted at rest.
/// </summary>
/// <remarks>
///     This is the KV store's own port, deliberately string-in/string-out: the store persists JSON and
///     gossips values verbatim, so it needs a text representation. The actual cryptography lives in
///     <c>Pragmatic.Cryptography</c> — see <see cref="KvSecretProtector" />, which adapts it.
/// </remarks>
internal interface IKvSecretProtector
{
    /// <summary>Encrypts a plaintext value for storage.</summary>
    string Encrypt(string plaintext);

    /// <summary>Decrypts a stored value back to plaintext.</summary>
    string Decrypt(string ciphertext);
}
