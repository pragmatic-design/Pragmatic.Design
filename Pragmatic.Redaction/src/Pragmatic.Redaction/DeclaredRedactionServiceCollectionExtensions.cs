using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Redaction;

/// <summary>
///     Turns the generated <c>IRedactionMap</c>s into an effect: every logger the application hands
///     out masks declared members before any provider formats them.
/// </summary>
public static class DeclaredRedactionServiceCollectionExtensions
{
    /// <summary>
    ///     Applies declared redaction to every logger in the container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         Order-independent by construction: it replaces <see cref="ILoggerFactory" />, which is
    ///         resolved when the container is built, so providers registered after this call are
    ///         covered too. Decorating the <see cref="ILoggerProvider" /> registrations instead would
    ///         only reach those present at call time — and the first thing that adds one later is a
    ///         test host, which is precisely where the guarantee has to hold to be measurable.
    ///     </para>
    ///     <para>
    ///         Idempotent: a factory already wrapped is left alone.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddDeclaredRedaction(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<DeclaredRedactor>();

        // No ILoggerFactory registered means the application has no logging at all, so there is
        // nothing to mask and nothing to wrap. Every real host — WebApplicationBuilder, HostBuilder —
        // registers it before any Pragmatic wiring runs. Calling AddLogging() here would drag the
        // logging implementation package into this assembly for a case that cannot leak.

        for (var i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType != typeof(ILoggerFactory))
                continue;

            services[i] = Wrap(services[i]);
        }

        return services;
    }

    /// <summary>Replaces the factory registration with one that yields the same factory, wrapped.</summary>
    private static ServiceDescriptor Wrap(ServiceDescriptor descriptor)
    {
        return ServiceDescriptor.Describe(
            typeof(ILoggerFactory),
            provider =>
            {
                var inner = Resolve(descriptor, provider);
                return inner is RedactingLoggerFactory already
                    ? already
                    : new RedactingLoggerFactory(inner, provider.GetRequiredService<DeclaredRedactor>());
            },
            descriptor.Lifetime);
    }

    private static ILoggerFactory Resolve(ServiceDescriptor descriptor, IServiceProvider provider)
    {
        if (descriptor.ImplementationInstance is ILoggerFactory instance)
            return instance;

        if (descriptor.ImplementationFactory is not null)
            return (ILoggerFactory)descriptor.ImplementationFactory(provider);

        // A type registration: build it through the container so its own dependencies still resolve.
        return (ILoggerFactory)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!);
    }
}
