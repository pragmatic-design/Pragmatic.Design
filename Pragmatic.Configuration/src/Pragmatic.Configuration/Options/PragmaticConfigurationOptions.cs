namespace Pragmatic.Configuration.Options;

/// <summary>
///     Configuration options for the Pragmatic configuration system itself.
/// </summary>
public sealed class PragmaticConfigurationOptions
{
    /// <summary>Optional sub-environment tag (e.g. "eu-west", "canary").</summary>
    public string? EnvironmentTag { get; set; }

    /// <summary>
    ///     Whether configuration reads are served through a read-through cache (default <c>true</c>).
    ///     Set to <c>false</c> to bypass caching and read directly from the store on every call.
    ///     Also gates the secret read-through cache.
    /// </summary>
    public bool EnableReadCaching { get; set; } = true;

    /// <summary>
    ///     Default TTL for the secret read-through cache (default 60s). Deliberately shorter than the
    ///     configuration cache — a revoked secret should not linger in memory. Individual entries are
    ///     capped further by the secret's advertised expiry (<see cref="SecretEntry.ExpiresAt" />).
    /// </summary>
    public TimeSpan SecretCacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Multi-tenant configuration settings.</summary>
    public MultiTenantOptions MultiTenant { get; } = new();
}
