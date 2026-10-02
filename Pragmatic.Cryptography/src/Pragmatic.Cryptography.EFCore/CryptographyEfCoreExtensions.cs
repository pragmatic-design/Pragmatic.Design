using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Cryptography.EFCore;

/// <summary>
///     Registers the EF Core-backed per-subject key store.
/// </summary>
public static class CryptographyEfCoreExtensions
{
    /// <summary>
    ///     Registers <see cref="ISubjectKeyStore" /> and the matching <see cref="IKeyResolver" /> over
    ///     <see cref="CryptographyDbContext" />.
    /// </summary>
    /// <remarks>
    ///     Both resolve to the same scoped instance: a store that hands out keys and a resolver that
    ///     answers by key id are two views of one table, and splitting them across instances would mean
    ///     two DbContext round trips for one logical lookup.
    ///     <para>
    ///         The caller must have registered <see cref="CryptographyDbContext" /> and an
    ///         <see cref="ISecretEncryptor" /> for the master key ring — the master key never comes from
    ///         the same database as the wrapped keys.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddCryptographySubjectKeys(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<SubjectKeyCache>();
        services.TryAddScoped<EfCoreSubjectKeyStore>();
        services.TryAddScoped<ISubjectKeyStore>(sp => sp.GetRequiredService<EfCoreSubjectKeyStore>());

        // The resolver is cached, the store is not. Only unwrapping key material is expensive enough to
        // be worth caching; the subject's status is a single indexed column and is read fresh on every
        // access, which is what keeps a warm cache from ever resurrecting erased data.
        services.TryAddScoped<IKeyResolver>(sp => new CachingKeyResolver(
            sp.GetRequiredService<EfCoreSubjectKeyStore>(),
            sp.GetRequiredService<SubjectKeyCache>()));

        services.TryAddScoped<ISubjectDataProtector, SubjectDataProtector>();

        return services;
    }
}
