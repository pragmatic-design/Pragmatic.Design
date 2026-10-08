using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Serialization;

/// <summary>
///     Registration helpers for the shared <see cref="PragmaticJsonOptions"/> seam.
/// </summary>
public static class PragmaticJsonServiceCollectionExtensions
{
    /// <summary>
    ///     Contributes a source-generated <see cref="IJsonTypeInfoResolver"/> (pass a context's
    ///     <c>Default</c> instance) to the shared seam. Framework packages call this from their own
    ///     registration so their closed types serialize AOT-safely without user configuration.
    ///     Idempotent: adding the same context instance twice is ignored.
    /// </summary>
    public static IServiceCollection AddPragmaticJsonContext(this IServiceCollection services, IJsonTypeInfoResolver context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);
        GetOrAddOptions(services).AddContextOnce(context);
        return services;
    }

    /// <summary>
    ///     Contributes a <see cref="JsonTypeInfo"/> modifier to the shared seam. Framework packages call
    ///     this from their own registration so a per-property behaviour applies wherever the host
    ///     serializes, without owning the resolver.
    ///     Idempotent: adding the same delegate twice is ignored.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <see cref="PragmaticJsonOptions"/> is registered as a singleton <b>instance</b>, not through
    ///     the options pattern, so <c>services.Configure&lt;PragmaticJsonOptions&gt;(...)</c> never runs:
    ///     nothing resolves <c>IOptions&lt;PragmaticJsonOptions&gt;</c>. This is the registration that
    ///     reaches the object the host actually builds from.
    /// </remarks>
    public static IServiceCollection AddPragmaticJsonModifier(this IServiceCollection services, Action<JsonTypeInfo> modifier)
        => services.AddPragmaticJsonModifier(modifier, touches: null);

    /// <summary>
    ///     Contributes a <see cref="JsonTypeInfo"/> modifier that changes only the types it says it touches.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="modifier">The modifier.</param>
    /// <param name="touches">Whether the modifier changes how a type is written; null when it cannot say.</param>
    /// <remarks>See <see cref="PragmaticJsonOptions.AddModifier(Action{JsonTypeInfo}, Func{Type, bool})" />.</remarks>
    public static IServiceCollection AddPragmaticJsonModifier(
        this IServiceCollection services, Action<JsonTypeInfo> modifier, Func<Type, bool>? touches)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modifier);
        GetOrAddOptions(services).AddModifier(modifier, touches);
        return services;
    }

    /// <summary>
    ///     Ensures a singleton <see cref="PragmaticJsonOptions"/> exists in the container.
    ///     Idempotent — every module that serializes JSON calls this from its own registration,
    ///     and the host's <c>UseJson(...)</c> configures the same singleton.
    /// </summary>
    public static IServiceCollection AddPragmaticJson(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        GetOrAddOptions(services);
        return services;
    }

    /// <summary>
    ///     Returns the shared, mutable <see cref="PragmaticJsonOptions"/> instance, registering a
    ///     fresh one as a singleton if none exists yet. Registered as an instance (not by type) so
    ///     that host configuration and module resolution observe the exact same object.
    /// </summary>
    internal static PragmaticJsonOptions GetOrAddOptions(IServiceCollection services)
    {
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(PragmaticJsonOptions)
                && services[i].ImplementationInstance is PragmaticJsonOptions existing)
                return existing;
        }

        var options = new PragmaticJsonOptions();
        services.TryAddSingleton(options);
        return options;
    }
}
