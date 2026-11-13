using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     Registers <see cref="EntitySavingHooksInterceptor" /> among the DI interceptors, for a context that takes its interceptors from
///     the container (library mode, an explicit DbContext).
/// </summary>
/// <remarks>
///     Once, whoever calls it: every module's library-mode registration does, and an interceptor registered twice would run each hook
///     twice. A Pragmatic host adds the interceptor in its generated context registration and does not call this.
/// </remarks>
public static class EntitySavingHooksRegistration
{
    /// <summary>Adds the interceptor as a scoped <see cref="IInterceptor" />, unless it is registered already.</summary>
    public static IServiceCollection AddEntitySavingHooksInterceptor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(d => d.ServiceType == typeof(EntitySavingHooksInterceptor)))
            return services;

        services.AddScoped(sp => new EntitySavingHooksInterceptor(
            new EntitySavingHooksRunner(sp, sp.GetServices<IEntitySavingHooks>()),
            sp.GetService<TimeProvider>() ?? TimeProvider.System,
            sp.GetService<ICurrentUser>(),
            sp.GetService<ITenantContext>()));
        services.AddScoped<IInterceptor>(sp => sp.GetRequiredService<EntitySavingHooksInterceptor>());
        return services;
    }
}
