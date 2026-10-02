namespace Pragmatic.Cryptography;

/// <summary>
///     Resolves the AES-256 key used by <see cref="AesGcmSecretEncryptor" /> to encrypt secrets at rest.
/// </summary>
/// <remarks>
///     The key still has to come from somewhere, but <em>where</em> is pluggable: an inline base64 option,
///     an environment variable, a file, or an external secret store. Implementations decode/resolve the
///     raw key material; the caller validates the length (32 bytes for AES-256). Which provider is the
///     default is decided by the host that registers one — this package ships
///     <see cref="EnvironmentEncryptionKeyProvider" /> and leaves config-bound providers to the module
///     that owns the configuration.
/// </remarks>
public interface IEncryptionKeyProvider
{
    /// <summary>
    ///     Resolves the 32-byte AES-256 key. May read from inline config, an environment variable, a file,
    ///     or an external secret store. Resolution may be asynchronous (e.g. a secret-store round trip).
    /// </summary>
    ValueTask<byte[]> GetKeyAsync(CancellationToken ct = default);
}
