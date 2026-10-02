namespace Pragmatic.Configuration;

/// <summary>
///     Read-only store for secrets. Secrets are set out-of-band (vault, CI/CD, user-secrets).
///     Implementations manage encryption at rest where needed.
/// </summary>
public interface ISecretStore
{
    /// <summary>Gets a secret value by key, or <c>null</c> if not found.</summary>
    Task<string?> GetSecretAsync(string key, CancellationToken ct = default);

    /// <summary>Gets a tenant-specific secret value by key, or <c>null</c> if not found.</summary>
    Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default);

    /// <summary>
    ///     Gets a secret together with optional rotation/expiry metadata
    ///     (see <see cref="SecretEntry"/>).
    /// </summary>
    /// <remarks>
    ///     The default implementation wraps <see cref="GetSecretAsync(string, CancellationToken)"/>
    ///     and reports no expiry/rotation. Stores backed by a vault that exposes TTL or rotation
    ///     timestamps (e.g. Azure Key Vault) should override this to populate
    ///     <see cref="SecretEntry.ExpiresAt"/> / <see cref="SecretEntry.RotatedAt"/>.
    ///     <para>
    ///         Callers that cache secrets should honor <see cref="SecretEntry.ExpiresAt"/> and
    ///         re-fetch once it has passed.
    ///     </para>
    /// </remarks>
    async Task<SecretEntry> GetSecretWithMetadataAsync(string key, CancellationToken ct = default)
    {
        var value = await GetSecretAsync(key, ct).ConfigureAwait(false);
        return new SecretEntry(value);
    }

    /// <summary>
    ///     Tenant-scoped overload of
    ///     <see cref="GetSecretWithMetadataAsync(string, CancellationToken)"/>.
    /// </summary>
    /// <remarks>
    ///     The default implementation wraps
    ///     <see cref="GetSecretAsync(string, string, CancellationToken)"/> and reports no
    ///     expiry/rotation.
    /// </remarks>
    async Task<SecretEntry> GetSecretWithMetadataAsync(string key, string tenantId, CancellationToken ct = default)
    {
        var value = await GetSecretAsync(key, tenantId, ct).ConfigureAwait(false);
        return new SecretEntry(value);
    }
}
