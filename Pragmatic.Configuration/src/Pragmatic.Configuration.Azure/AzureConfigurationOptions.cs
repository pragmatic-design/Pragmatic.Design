namespace Pragmatic.Configuration.Azure;

/// <summary>
///     Options for the Azure configuration backends.
/// </summary>
public sealed class AzureConfigurationOptions
{
    /// <summary>Azure App Configuration endpoint URL (e.g. "https://myapp.azconfig.io").</summary>
    public string? AppConfigurationEndpoint { get; set; }

    /// <summary>
    ///     Azure App Configuration connection string (alternative to endpoint + managed identity).
    /// </summary>
    /// <remarks>
    ///     WARNING: The connection string contains a storage account key in plaintext.
    ///     Prefer using <see cref="AppConfigurationEndpoint"/> with Managed Identity or a
    ///     credential from the Azure.Identity package to avoid storing secrets in config files.
    ///     If you must use a connection string, load it from a secret store or environment variable —
    ///     never hard-code it in source or appsettings.json.
    /// </remarks>
    public string? AppConfigurationConnectionString { get; set; }

    /// <summary>Azure Key Vault URI (e.g. "https://myapp.vault.azure.net").</summary>
    public string? KeyVaultUri { get; set; }

    /// <summary>Key prefix for all configuration values. Default: "Pragmatic".</summary>
    public string KeyPrefix { get; set; } = "Pragmatic";

    /// <summary>
    ///     Sentinel key name for change notification.
    ///     When this key changes, all configuration is refreshed.
    ///     Default: "Pragmatic:Sentinel".
    /// </summary>
    public string SentinelKey { get; set; } = "Pragmatic:Sentinel";

    /// <summary>Cache duration for configuration values before checking for changes. Default: 30 seconds.</summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Cache duration for secrets from Key Vault. Default: 5 minutes.</summary>
    public TimeSpan SecretCacheExpiration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Cache duration for negative lookups (secret not found) from Key Vault. Default: 30 seconds.
    ///     A short TTL prevents repeated vault round-trips for missing secrets while still allowing a
    ///     newly-created secret to be picked up quickly.
    /// </summary>
    public TimeSpan NegativeCacheExpiration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Validates that cache durations are strictly positive.
    ///     Returns <c>null</c> when valid, otherwise an error message describing the first violation.
    /// </summary>
    /// <remarks>
    ///     Implemented as a code guard rather than a <c>[Range(typeof(TimeSpan), ...)]</c> attribute,
    ///     which emits IL2026/IL3050 trim/AOT warnings.
    /// </remarks>
    internal string? Validate()
    {
        if (CacheExpiration <= TimeSpan.Zero)
            return $"{nameof(CacheExpiration)} must be greater than zero.";

        if (SecretCacheExpiration <= TimeSpan.Zero)
            return $"{nameof(SecretCacheExpiration)} must be greater than zero.";

        if (NegativeCacheExpiration <= TimeSpan.Zero)
            return $"{nameof(NegativeCacheExpiration)} must be greater than zero.";

        return null;
    }
}
