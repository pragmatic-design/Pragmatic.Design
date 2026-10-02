using Microsoft.Extensions.Configuration;

namespace Pragmatic.Configuration.Bridge;

/// <summary>
///     Extension methods to add the Pragmatic configuration bridge
///     to the Microsoft.Extensions.Configuration pipeline.
/// </summary>
public static class ConfigurationBridgeExtensions
{
    /// <summary>
    ///     Adds the Pragmatic configuration store as a configuration source.
    ///     This enables IOptions{T} and IOptionsMonitor{T} to read from the
    ///     Pragmatic store with cascade resolution and hot-reload support.
    /// </summary>
    /// <param name="builder">The configuration builder.</param>
    /// <param name="store">The backing configuration store.</param>
    /// <param name="environment">Environment profile for cascade resolution.</param>
    /// <param name="keyPrefix">Optional key prefix filter for section-scoped loading.</param>
    public static IConfigurationBuilder AddPragmaticStore(
        this IConfigurationBuilder builder,
        IConfigurationStore store,
        EnvironmentProfile environment,
        string? keyPrefix = null)
    {
        builder.Add(new PragmaticConfigurationSource(store, environment, keyPrefix));
        return builder;
    }
}
