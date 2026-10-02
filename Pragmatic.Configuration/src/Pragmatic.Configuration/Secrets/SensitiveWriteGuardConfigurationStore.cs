namespace Pragmatic.Configuration.Secrets;

using Microsoft.Extensions.Logging;

/// <summary>
///     Decorates an <see cref="IConfigurationStore"/> so that writing a <c>[Sensitive]</c> value as
///     plaintext (i.e. not a <see cref="SecretReference"/>) emits a warning steering the caller to a
///     secret reference. Complements the Agent config store's inline guard: this covers
///     <b>every</b> plaintext backend (Redis, database-without-encryption, in-memory), not just the Agent.
/// </summary>
/// <remarks>
///     Advisory only — it never blocks the write (a backend may legitimately encrypt at rest); it makes
///     the exposure discoverable rather than silent. Reads/watch/delete pass straight through.
/// </remarks>
public sealed class SensitiveWriteGuardConfigurationStore(
    IConfigurationStore inner,
    ISensitiveKeyClassifier classifier,
    ILogger<SensitiveWriteGuardConfigurationStore> logger) : IConfigurationStore
{
    public Task<string?> GetAsync(string key, CancellationToken ct = default)
        => inner.GetAsync(key, ct);

    public Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default)
        => inner.GetAsync(key, tenantId, ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default)
        => inner.GetSectionAsync(prefix, ct);

    public Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, string tenantId, CancellationToken ct = default)
        => inner.GetSectionAsync(prefix, tenantId, ct);

    public Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(value) && classifier.IsSensitive(key) && !SecretReference.IsReference(value))
        {
            logger.LogWarning(
                "Configuration key '{Key}' is marked [Sensitive] but is being written as a plaintext value. "
                + "Store the secret in an ISecretStore (e.g. Key Vault) and put only a reference in "
                + "configuration ('{Scheme}{Key}') so the secret material never enters the store.",
                key, SecretReference.Scheme, key);
        }

        return inner.SetAsync(key, value, tenantId, ct);
    }

    public Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default)
        => inner.DeleteAsync(key, tenantId, ct);

    public IAsyncEnumerable<ConfigurationChange> WatchAsync(string keyPattern, CancellationToken ct = default)
        => inner.WatchAsync(keyPattern, ct);
}
