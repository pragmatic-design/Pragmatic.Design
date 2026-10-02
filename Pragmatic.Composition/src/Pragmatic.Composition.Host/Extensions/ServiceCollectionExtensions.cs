using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Attributes;

namespace Pragmatic.Composition.Extensions;

/// <summary>
///     Extension methods for <see cref="IServiceCollection" /> supporting Pragmatic Composition.
///     Note: Decorate methods are in Pragmatic.Abstractions (ServiceCollectionDecorateExtensions),
///     and Scan is in the Pragmatic.Composition.Scanning package, so the host runtime does not
///     carry an assembly scanner it never calls.
/// </summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Tries to add a service only if no registration already exists.
        /// </summary>
        public IServiceCollection TryAddService<TService, TImplementation>(Lifetime lifetime = Lifetime.Scoped)
            where TService : class
            where TImplementation : class, TService
        {
            if (!services.Any(d => d.ServiceType == typeof(TService)))
                services.Add(new ServiceDescriptor(
                    typeof(TService),
                    typeof(TImplementation),
                    ToMsLifetime(lifetime)));

            return services;
        }
    }

    private static Microsoft.Extensions.DependencyInjection.ServiceLifetime ToMsLifetime(Lifetime lifetime)
    {
        return lifetime switch
        {
            Lifetime.Singleton => Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton,
            Lifetime.Scoped => Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped,
            Lifetime.Transient => Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient,
            // Fail loudly on an unmapped enum value rather than silently defaulting to Scoped — a future
            // Lifetime addition must be mapped explicitly, not silently mis-registered.
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime,
                $"Unmapped {nameof(Lifetime)} value; add an explicit mapping in {nameof(ToMsLifetime)}.")
        };
    }
}
