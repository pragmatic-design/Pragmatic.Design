namespace Pragmatic.Configuration.Azure;

/// <summary>
///     Key and label conventions for Azure App Configuration and Key Vault.
/// </summary>
internal static class AzureKeyConventions
{
    /// <summary>Converts a configuration key to Azure App Configuration format.</summary>
    /// <remarks>Key format: "{prefix}:{key}" — prefix defaults to "Pragmatic".</remarks>
    public static string ToAppConfigKey(string prefix, string key)
        => $"{prefix}:{key}";

    /// <summary>Extracts the original key from an Azure App Configuration key.</summary>
    public static string FromAppConfigKey(string prefix, string azureKey)
        => azureKey.StartsWith($"{prefix}:", StringComparison.Ordinal)
            ? azureKey[($"{prefix}:".Length)..]
            : azureKey;

    /// <summary>
    ///     Converts a configuration key prefix to Azure App Configuration key filter.
    ///     Returns "{prefix}:{sectionPrefix}*".
    /// </summary>
    public static string ToAppConfigKeyFilter(string prefix, string sectionPrefix)
        => $"{prefix}:{sectionPrefix}*";

    /// <summary>
    ///     Builds the label for environment-scoped configuration.
    ///     Null environment = no label (applies to all environments).
    /// </summary>
    public static string? ToEnvironmentLabel(string? environment, string? tag)
    {
        if (environment is null)
            return null;

        return tag is null
            ? environment.ToLowerInvariant()
            : $"{environment.ToLowerInvariant()}-{tag}";
    }

    /// <summary>Builds the label for tenant-scoped configuration.</summary>
    public static string ToTenantLabel(string tenantId)
        => $"tenant-{tenantId}";

    /// <summary>
    ///     Converts a configuration key to Azure Key Vault secret name.
    ///     Key Vault doesn't support ":" so we use "--" as separator.
    /// </summary>
    public static string ToKeyVaultName(string prefix, string key)
        => $"{prefix}--{key.Replace(":", "--", StringComparison.Ordinal)}";

    /// <summary>Converts a tenant secret key to Key Vault format.</summary>
    public static string ToKeyVaultTenantName(string prefix, string key, string tenantId)
        => $"{prefix}--tenant--{tenantId}--{key.Replace(":", "--", StringComparison.Ordinal)}";
}
