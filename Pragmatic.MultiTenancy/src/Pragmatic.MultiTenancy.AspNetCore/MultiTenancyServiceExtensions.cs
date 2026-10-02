using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.MultiTenancy;

/// <summary>
///     DI registration extensions for Pragmatic.MultiTenancy.
/// </summary>
public static class MultiTenancyServiceExtensions
{
    /// <summary>
    ///     Registers multi-tenancy services with the specified resolution strategy.
    ///     If no strategy is configured, defaults to single-tenant mode (zero cost).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional builder to configure tenant resolution strategy.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPragmaticMultiTenancy(
        this IServiceCollection services,
        Action<MultiTenancyBuilder>? configure = null)
    {
        var builder = new MultiTenancyBuilder(services);

        if (configure is not null)
            configure(builder);
        else
            builder.UseSingleTenant();

        builder.Register();

        // Register the mutable tenant context (scoped per request)
        services.TryAddScoped<MutableTenantContext>();
        // The effective context prefers the request-scoped tenant and falls back to the ambient
        // TenantScope (AsyncLocal), so background jobs / tests using TenantScope.BeginScope are honored (MT-M1).
        services.TryAddScoped<ITenantContext>(sp => new AmbientTenantContext(sp.GetRequiredService<MutableTenantContext>()));
        // Exposed as the mutable interface too: consumers that must SET the tenant on a scope
        // (background job processors, seeders) cannot get there by casting ITenantContext, because
        // the effective context is a read-only composite.
        services.TryAddScoped<IMutableTenantContext>(sp => sp.GetRequiredService<MutableTenantContext>());

        // Here, not only in UseMultiTenancy: the source generator calls this method directly when it
        // detects the module, so an application that never touches the builder would otherwise get a
        // resolver with nothing running it. The tenant would stay unresolved, the interceptor would leave
        // TenantId at its default on write, and the fail-closed query filter would match nothing on
        // read — rows in the table, empty lists in the API, no exception and no log. Registering the
        // step is what makes the single-tenant default actually resolve.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupStep, TenantResolutionStep>());

        return services;
    }
}
