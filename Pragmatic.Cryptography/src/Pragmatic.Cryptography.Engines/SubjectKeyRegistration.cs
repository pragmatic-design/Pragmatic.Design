using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Cryptography.Engines;

/// <summary>What both engines register the way EF Core's <c>AddCryptographySubjectKeys</c> does.</summary>
internal static class SubjectKeyRegistration
{
    /// <summary>
    ///     The store, as <see cref="ISubjectKeyStore" />, and as <see cref="IKeyResolver" /> behind the key cache: the resolver is cached, the
    ///     status is not, which keeps a warm cache from resurrecting erased data. And <see cref="ISubjectDataProtector" /> over both. The master
    ///     key ring (<see cref="ISecretEncryptor" />) is the application's to register, never from the database that holds the wrapped keys.
    /// </summary>
    public static IServiceCollection Stores<TStore>(IServiceCollection services, Func<IServiceProvider, TStore> store)
        where TStore : class, ISubjectKeyStore, IKeyResolver
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SubjectKeyCache>();
        services.TryAddScoped(store);
        services.TryAddScoped<ISubjectKeyStore>(sp => sp.GetRequiredService<TStore>());
        services.TryAddScoped<IKeyResolver>(sp => new CachingKeyResolver(sp.GetRequiredService<TStore>(), sp.GetRequiredService<SubjectKeyCache>()));
        services.TryAddScoped<ISubjectDataProtector, SubjectDataProtector>();
        return services;
    }
}
