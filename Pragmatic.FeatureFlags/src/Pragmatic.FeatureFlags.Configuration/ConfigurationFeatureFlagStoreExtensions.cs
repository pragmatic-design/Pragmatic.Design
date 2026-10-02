using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.FeatureFlags.Configuration;

/// <summary>
///     DI registration helpers for <see cref="ConfigurationFeatureFlagStore" />.
/// </summary>
public static class ConfigurationFeatureFlagStoreExtensions
{
    /// <summary>
    ///     Registers <see cref="ConfigurationFeatureFlagStore" /> as the
    ///     <see cref="IFeatureFlagStore" /> singleton. Reads from
    ///     <paramref name="sectionName" /> (default: <c>"FeatureFlags"</c>) of the
    ///     ambient <see cref="IConfiguration" />.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It <b>replaces</b>, not <c>TryAdd</c>s. The generated entry point registers the
    ///     framework's services <em>before</em> it invokes the host's callback — so by the moment an
    ///     application says which store it wants, the in-memory default has the slot and a
    ///     <c>TryAdd</c> against a taken slot is silence: this registration, called exactly where its
    ///     own documentation says to call it, would do nothing. The module declares and the composition
    ///     follows; it does not silently outvote the declaration.
    /// </remarks>
    public static IServiceCollection AddConfigurationFeatureFlagStore(
        this IServiceCollection services,
        string sectionName = ConfigurationFeatureFlagStore.DefaultSectionName)
    {
        services.Replace(ServiceDescriptor.Singleton<IFeatureFlagStore>(sp =>
            new ConfigurationFeatureFlagStore(
                sp.GetRequiredService<IConfiguration>(),
                sectionName)));
        return services;
    }
}
