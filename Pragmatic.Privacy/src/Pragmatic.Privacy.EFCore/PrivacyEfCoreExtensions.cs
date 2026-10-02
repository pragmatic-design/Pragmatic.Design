using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Privacy.EFCore;

/// <summary>
///     Registers the EF Core-backed subject registry.
/// </summary>
public static class PrivacyEfCoreExtensions
{
    /// <summary>
    ///     Registers <see cref="ISubjectRegistry" /> over <see cref="PrivacyDbContext" />.
    /// </summary>
    /// <remarks>
    ///     The caller must supply <see cref="PrivacyDbContext" />, an
    ///     <c>ISecretEncryptor</c> for the stored identities, and an
    ///     <see cref="ISubjectLookupKeyProvider" /> for the blind index. Neither key may live in the same
    ///     database as the table — a key stored beside what it protects protects nothing.
    /// </remarks>
    public static IServiceCollection AddSubjectRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        // Replace, not TryAdd: AddPrivacy() registers a fallback for both that throws on use, and in a
        // Pragmatic host it runs first — the generated wiring calls it before the application's
        // registrations. With TryAdd on both sides the fallback stayed, every time.
        services.Replace(ServiceDescriptor.Scoped<ISubjectRegistry, EfCoreSubjectRegistry>());
        services.Replace(ServiceDescriptor.Scoped<IConsentStore, EfCoreConsentStore>());

        return services;
    }
}
