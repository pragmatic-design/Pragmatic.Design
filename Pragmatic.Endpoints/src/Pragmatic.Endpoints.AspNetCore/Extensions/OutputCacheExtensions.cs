using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Endpoints.AspNetCore.Extensions;

/// <summary>
///     Extension methods for bridging ASP.NET Core Output Cache with Pragmatic.Caching.
/// </summary>
public static class OutputCacheExtensions
{
    /// <summary>
    ///     Registers <see cref="PragmaticOutputCacheStore"/> as the <see cref="IOutputCacheStore"/>
    ///     implementation, backed by Pragmatic.Caching's <c>ICacheStack</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         When this is enabled, <c>[ResponseCache]</c> on endpoints uses the same distributed
    ///         cache backend as <c>[Cacheable]</c> on domain actions — Redis, SQL, or whatever
    ///         <c>IDistributedCache</c> is configured for <c>HybridCache</c>.
    ///     </para>
    ///     <para>
    ///         Requires <c>Pragmatic.Caching</c> to be registered in DI (<c>AddPragmaticCaching()</c>).
    ///         If <c>ICacheStack</c> is not available at runtime, operations are no-ops.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code>
    /// builder.Services.AddPragmaticCaching();              // Pragmatic.Caching with HybridCache
    /// builder.Services.UseOutputCacheFromPragmaticCaching(); // Bridge: output cache → same backend
    /// app.UseOutputCache();
    ///     </code>
    /// </example>
    public static IServiceCollection UseOutputCacheFromPragmaticCaching(this IServiceCollection services)
    {
        services.AddOutputCache();

        // Replace, not TryAdd: AddOutputCache has just registered its own IOutputCacheStore through a
        // factory, so TryAdd found one and skipped — the bridge registered nothing and responses went
        // on being cached per instance in memory while everything else used the configured backend.
        // Nothing said so, because a store was present either way.
        services.Replace(ServiceDescriptor.Singleton<IOutputCacheStore, PragmaticOutputCacheStore>());
        return services;
    }
}
