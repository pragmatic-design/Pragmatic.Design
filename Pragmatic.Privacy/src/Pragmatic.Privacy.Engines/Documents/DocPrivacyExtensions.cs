using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Documents;

namespace Pragmatic.Privacy.Engines.Documents;

/// <summary>The subject registry and the consent store on the documents engine.</summary>
public static class DocPrivacyExtensions
{
    /// <summary>
    ///     <see cref="ISubjectRegistry" /> and <see cref="IConsentStore" /> on <typeparamref name="TBoundary" />'s session, the boundary that holds
    ///     the <c>[DataSubject]</c> entities. One per application, as EF Core's <c>AddSubjectRegistry</c>: when they live in more than one place,
    ///     the application names the one. Its keys (<c>ISecretEncryptor</c>, <see cref="ISubjectLookupKeyProvider" />) are its own to register.
    /// </summary>
    public static IServiceCollection AddDocSubjectRegistry<TBoundary>(this IServiceCollection services)
        => PrivacyEngineRegistration.Stores(services,
            static sp => new DocSubjectRegistry(Session<TBoundary>(sp), PrivacyEngineRegistration.Identity(sp), sp.GetRequiredService<TimeProvider>()),
            static sp => new DocConsentStore(Session<TBoundary>(sp), sp.GetRequiredService<TimeProvider>()));

    private static DocSession Session<TBoundary>(IServiceProvider sp) => sp.GetRequiredKeyedService<DocSession>(typeof(TBoundary));
}
