using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Pragmatic.Configuration.Azure;

/// <summary>
///     ISecretStore backed by Azure Key Vault. Supports versioning and soft-delete natively via Key Vault
///     and surfaces the vault's expiry/rotation metadata as <see cref="SecretEntry" />.
/// </summary>
/// <remarks>
///     This store is a thin client: caching is provided uniformly by the shared
///     <c>CachingSecretStore</c> decorator (rotation-aware, honoring <see cref="SecretEntry.ExpiresAt" />),
///     not by the store itself.
/// </remarks>
internal sealed partial class AzureKeyVaultSecretStore(
    SecretClient client,
    IOptions<AzureConfigurationOptions> options,
    ILogger<AzureKeyVaultSecretStore> logger)
    : ISecretStore
{
    private readonly AzureConfigurationOptions _options = options.Value;

    public async Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
    {
        var vaultName = AzureKeyConventions.ToKeyVaultName(_options.KeyPrefix, key);
        return (await GetEntryFromVaultAsync(vaultName, ct).ConfigureAwait(false)).Value;
    }

    public async Task<string?> GetSecretAsync(string key, string tenantId, CancellationToken ct = default)
        => (await GetSecretWithMetadataAsync(key, tenantId, ct).ConfigureAwait(false)).Value;

    /// <summary>
    ///     Fetches the secret and surfaces Key Vault's native expiry (<c>ExpiresOn</c>) and last
    ///     rotation (<c>UpdatedOn</c>) as <see cref="SecretEntry"/> metadata so the caching layer can honor it.
    /// </summary>
    public async Task<SecretEntry> GetSecretWithMetadataAsync(string key, CancellationToken ct = default)
    {
        var vaultName = AzureKeyConventions.ToKeyVaultName(_options.KeyPrefix, key);
        return await GetEntryFromVaultAsync(vaultName, ct).ConfigureAwait(false);
    }

    /// <inheritdoc cref="GetSecretWithMetadataAsync(string, CancellationToken)" />
    public async Task<SecretEntry> GetSecretWithMetadataAsync(string key, string tenantId, CancellationToken ct = default)
    {
        // Try tenant-specific secret first, fall back to global.
        var tenantVaultName = AzureKeyConventions.ToKeyVaultTenantName(_options.KeyPrefix, key, tenantId);
        var tenantEntry = await GetEntryFromVaultAsync(tenantVaultName, ct).ConfigureAwait(false);
        if (tenantEntry.Found)
            return tenantEntry;

        var globalVaultName = AzureKeyConventions.ToKeyVaultName(_options.KeyPrefix, key);
        return await GetEntryFromVaultAsync(globalVaultName, ct).ConfigureAwait(false);
    }

    private async Task<SecretEntry> GetEntryFromVaultAsync(string secretName, CancellationToken ct)
    {
        try
        {
            var response = await client.GetSecretAsync(secretName, cancellationToken: ct).ConfigureAwait(false);
            var secret = response.Value;

            return new SecretEntry(
                secret.Value,
                ExpiresAt: secret.Properties.ExpiresOn,
                RotatedAt: secret.Properties.UpdatedOn);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            LogSecretNotFound(secretName);
            return SecretEntry.NotFound;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Secret not found in Key Vault: {SecretName}")]
    private partial void LogSecretNotFound(string secretName);
}
