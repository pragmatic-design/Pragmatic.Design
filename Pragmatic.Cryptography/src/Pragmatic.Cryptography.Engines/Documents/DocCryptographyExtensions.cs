using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Documents;

namespace Pragmatic.Cryptography.Engines.Documents;

/// <summary>The per-subject keys on the documents engine.</summary>
public static class DocCryptographyExtensions
{
    /// <summary>
    ///     <see cref="ISubjectKeyStore" />, <see cref="IKeyResolver" /> and <see cref="ISubjectDataProtector" /> on <typeparamref name="TBoundary" />'s
    ///     session, the boundary that holds the <c>[DataSubject]</c> entities and so their <c>ProtectedValue</c> fields. As EF Core's
    ///     <c>AddCryptographySubjectKeys</c>, the master key ring (<see cref="ISecretEncryptor" />) is the application's to register.
    /// </summary>
    public static IServiceCollection AddDocSubjectKeys<TBoundary>(this IServiceCollection services)
        => SubjectKeyRegistration.Stores(services, static sp => new DocSubjectKeyStore(sp.GetRequiredKeyedService<DocSession>(typeof(TBoundary)),
            sp.GetRequiredService<ISecretEncryptor>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<SubjectKeyCache>()));
}
