using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Privacy.Engines.Sql;

/// <summary>The subject registry and the consent store on the Sql engine.</summary>
public static class SqlPrivacyExtensions
{
    /// <summary>
    ///     <see cref="ISubjectRegistry" /> and <see cref="IConsentStore" /> on <typeparamref name="TBoundary" />'s session, the database that holds
    ///     the <c>[DataSubject]</c> entities. One per application, as EF Core's <c>AddSubjectRegistry</c>: when they live in more than one place,
    ///     the application names the one. Its keys (<c>ISecretEncryptor</c>, <see cref="ISubjectLookupKeyProvider" />) are its own to register.
    /// </summary>
    public static IServiceCollection AddSqlSubjectRegistry<TBoundary>(this IServiceCollection services)
        => PrivacyEngineRegistration.Stores(services,
            static sp => new SqlSubjectRegistry(Session<TBoundary>(sp), PrivacyEngineRegistration.Identity(sp), sp.GetRequiredService<TimeProvider>()),
            static sp => new SqlConsentStore(Session<TBoundary>(sp), sp.GetRequiredService<TimeProvider>()));

    private static SqlSession Session<TBoundary>(IServiceProvider sp) => sp.GetRequiredKeyedService<SqlSession>(typeof(TBoundary));
}
