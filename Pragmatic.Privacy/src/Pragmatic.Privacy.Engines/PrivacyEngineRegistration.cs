using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pragmatic.Cryptography;

namespace Pragmatic.Privacy.Engines;

/// <summary>What both engines register the way EF Core's <c>AddSubjectRegistry</c> does.</summary>
internal static class PrivacyEngineRegistration
{
    /// <summary>
    ///     The registry and the consent store, each to its factory. Replace, not TryAdd: <c>AddPrivacy()</c> registers a fallback for both that
    ///     throws on use, and in a Pragmatic host it runs first. The keys (<see cref="ISecretEncryptor" />, <see cref="ISubjectLookupKeyProvider" />)
    ///     are the application's to register, as on EF Core: neither may live in the database that holds the subjects.
    /// </summary>
    public static IServiceCollection Stores(IServiceCollection services, Func<IServiceProvider, ISubjectRegistry> registry,
        Func<IServiceProvider, IConsentStore> consents)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Scoped(registry));
        services.Replace(ServiceDescriptor.Scoped(consents));
        return services;
    }

    /// <summary>The identity's encryption and blind index, from the keys the application registered.</summary>
    public static SubjectIdentity Identity(IServiceProvider sp)
        => new(sp.GetRequiredService<ISecretEncryptor>(), sp.GetRequiredService<ISubjectLookupKeyProvider>());
}
