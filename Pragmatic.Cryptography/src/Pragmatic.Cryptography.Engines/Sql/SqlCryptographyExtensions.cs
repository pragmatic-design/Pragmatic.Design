using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Cryptography.Engines.Sql;

/// <summary>The per-subject keys on the Sql engine.</summary>
public static class SqlCryptographyExtensions
{
    /// <summary>
    ///     <see cref="ISubjectKeyStore" />, <see cref="IKeyResolver" /> and <see cref="ISubjectDataProtector" /> on <typeparamref name="TBoundary" />'s
    ///     session, the database that holds the <c>[DataSubject]</c> entities and so their <c>ProtectedValue</c> columns: erasing a value is
    ///     destroying its key, in the same database. As EF Core's <c>AddCryptographySubjectKeys</c>, the master key ring
    ///     (<see cref="ISecretEncryptor" />) is the application's to register.
    /// </summary>
    public static IServiceCollection AddSqlSubjectKeys<TBoundary>(this IServiceCollection services)
        => SubjectKeyRegistration.Stores(services, static sp => new SqlSubjectKeyStore(sp.GetRequiredKeyedService<SqlSession>(typeof(TBoundary)),
            sp.GetRequiredService<ISecretEncryptor>(), sp.GetRequiredService<TimeProvider>(), sp.GetService<SubjectKeyCache>()));
}
