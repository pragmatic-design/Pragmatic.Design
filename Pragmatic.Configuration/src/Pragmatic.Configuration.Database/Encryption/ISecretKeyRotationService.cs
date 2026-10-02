namespace Pragmatic.Configuration.Database.Encryption;

/// <summary>
///     Re-encrypts every stored secret under the current encryption key. Run after rotating the key (new key
///     as current, old key moved to <see cref="DatabaseConfigurationOptions.PreviousEncryptionKeys" />): once
///     the pass completes, the old key can be dropped from the ring.
/// </summary>
public interface ISecretKeyRotationService
{
    /// <summary>
    ///     Reads every secret, decrypts it with whichever ring key authenticates, and rewrites it with the
    ///     current key. Secrets that decrypt under no ring key are left untouched and counted as failed.
    /// </summary>
    Task<SecretRotationReport> ReEncryptAllAsync(CancellationToken ct = default);
}
