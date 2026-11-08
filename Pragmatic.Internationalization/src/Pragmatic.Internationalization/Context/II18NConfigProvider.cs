namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Provides internationalization configuration.
/// </summary>
/// <remarks>
///     <para>
///         Multiple providers can be registered with different priorities.
///         The <see cref="I18NConfigResolver"/> merges configurations from all providers,
///         with higher-priority providers overriding lower-priority ones.
///     </para>
///     <para>
///         Returning null from <see cref="GetConfiguration"/> means "defer to lower priority providers".
///         Returning a config with null fields means "this provider doesn't set this field, defer to others".
///     </para>
/// </remarks>
/// <example>
/// <code>
/// // Priority levels (higher overrides lower):
/// // 0   - SystemConfigProvider (from IOptions, always present)
/// // 50  - GlobalConfigProvider (from database global settings)
/// // 100 - TenantConfigProvider (from database tenant settings)
/// // 200 - UserConfigProvider (from database user preferences)
/// // 300 - RequestConfigProvider (from HTTP Accept-Language)
/// </code>
/// </example>
public interface II18NConfigProvider
{
    /// <summary>
    ///     Gets the priority of this provider.
    /// </summary>
    /// <remarks>
    ///     Higher priority providers are checked first and can override lower priority ones.
    ///     <list type="table">
    ///         <item><term>0</term><description>System defaults (always present)</description></item>
    ///         <item><term>50-99</term><description>Global/Application settings</description></item>
    ///         <item><term>100-199</term><description>Tenant settings</description></item>
    ///         <item><term>200-299</term><description>User preferences</description></item>
    ///         <item><term>300+</term><description>Request-specific (HTTP headers, etc.)</description></item>
    ///     </list>
    /// </remarks>
    int Priority { get; }

    /// <summary>
    ///     Gets the configuration from this provider.
    /// </summary>
    /// <returns>
    ///     The configuration, or null if this provider has no configuration.
    ///     Returning null defers entirely to lower-priority providers.
    /// </returns>
    I18NConfig? GetConfiguration();

    /// <summary>
    ///     Gets the configuration from this provider asynchronously: what the request's middleware asks (<see cref="I18NConfigResolver.ResolveAsync" />).
    /// </summary>
    /// <remarks>
    ///     By default the synchronous answer. A provider that reads a source only asynchronously (a database an engine reads with no
    ///     synchronous path) overrides this one; <see cref="CachedConfigProvider" /> does it through <c>LoadConfigurationAsync</c>.
    /// </remarks>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The configuration, or null to defer to lower-priority providers.</returns>
    ValueTask<I18NConfig?> GetConfigurationAsync(CancellationToken ct = default) => new(GetConfiguration());
}
