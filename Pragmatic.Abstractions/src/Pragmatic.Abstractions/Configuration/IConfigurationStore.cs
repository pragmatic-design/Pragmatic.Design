namespace Pragmatic.Configuration;

/// <summary>
///     Backend-agnostic store for configuration values.
///     Implementations: local JSON, database (ADO.NET), Azure App Configuration.
/// </summary>
public interface IConfigurationStore
{
    /// <summary>Gets a configuration value by key, or <c>null</c> if not found.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>Gets a tenant-specific configuration value by key, or <c>null</c> if not found.</summary>
    Task<string?> GetAsync(string key, string tenantId, CancellationToken ct = default);

    /// <summary>Gets all configuration values whose keys start with <paramref name="prefix"/>.</summary>
    Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, CancellationToken ct = default);

    /// <summary>Gets all tenant-specific configuration values whose keys start with <paramref name="prefix"/>.</summary>
    Task<IReadOnlyDictionary<string, string>> GetSectionAsync(string prefix, string tenantId, CancellationToken ct = default);

    /// <summary>
    ///     Sets a configuration value. Pass <paramref name="tenantId"/> for tenant-specific overrides.
    ///     <para>
    ///         <b>Security:</b> callers are responsible for validating <paramref name="key"/>
    ///         before calling this method. Keys sourced from user input or untrusted external
    ///         systems must be validated against an allow-list or pattern before use —
    ///         unvalidated keys may be interpreted as SQL column names, Redis key patterns, or
    ///         path segments by the underlying store, creating an injection risk.
    ///     </para>
    /// </summary>
    Task SetAsync(string key, string value, string? tenantId = null, CancellationToken ct = default);

    /// <summary>Deletes a configuration value. Pass <paramref name="tenantId"/> for tenant-specific entries.</summary>
    Task DeleteAsync(string key, string? tenantId = null, CancellationToken ct = default);

    /// <summary>Watches for changes to keys matching <paramref name="keyPattern"/> and streams them as they occur.</summary>
    IAsyncEnumerable<ConfigurationChange> WatchAsync(string keyPattern, CancellationToken ct = default);
}
