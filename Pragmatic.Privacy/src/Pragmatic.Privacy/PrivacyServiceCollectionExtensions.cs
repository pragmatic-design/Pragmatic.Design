using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Privacy;

/// <summary>
///     Registers the data-subject processes: erasure, access, portability and the Article 30 register.
/// </summary>
/// <remarks>
///     Separate from the store registration (<c>AddSubjectRegistry</c> in the EF Core package) because
///     these are the parts that do not depend on where anything is persisted, and an application that
///     stores subjects somewhere else still wants them.
/// </remarks>
public static class PrivacyServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the services that can be built without an application-specific choice.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The caller still supplies the pieces only they can know:
    ///         <see cref="IErasureStep" /> implementations (what erasure actually touches),
    ///         <see cref="IPersonalDataSource" /> implementations (what an access request collects),
    ///         <see cref="IProcessingActivitySource" /> implementations (what the register describes),
    ///         and <see cref="ILegalHoldStore" /> when holds apply. Each is registered as an enumerable,
    ///         so contributing one is adding a registration rather than replacing anything.
    ///     </para>
    ///     <para>
    ///         <b><see cref="ConsentAwareRetentionResolver" /> is deliberately not registered.</b> It
    ///         needs the current notice version, and there is no defensible default for it: a wrong
    ///         version silently evaluates consent against a notice the subject never saw, which reads as
    ///         working. Register it explicitly with the version you publish.
    ///     </para>
    /// </remarks>
    public static IServiceCollection AddPrivacy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPortabilityFormatter, JsonPortabilityFormatter>();

        // TryAdd, so a store registered before this wins; AddSubjectRegistry() replaces the fallback when
        // it comes after, so it wins in either order. What this buys is that an
        // application wanting only the Article 30 register does not have to stand up a subject store to
        // start: the generated adapters resolve, and the ones that need a real registry throw where they
        // are used instead of at container build. See UnconfiguredSubjectRegistry.
        services.TryAddScoped<ISubjectRegistry, UnconfiguredSubjectRegistry>();
        services.TryAddScoped<IConsentStore, UnconfiguredConsentStore>();

        services.TryAddScoped<ErasureOrchestrator>();
        services.TryAddScoped<SubjectAccessService>();
        services.TryAddScoped<ProcessingRegisterBuilder>();

        // Also by interface, the same instance behind both: an operation depends on an interface,
        // because a concrete type is not injected into it (PRAG0419).
        services.TryAddScoped<ISubjectErasure>(sp => sp.GetRequiredService<ErasureOrchestrator>());
        services.TryAddScoped<ISubjectAccess>(sp => sp.GetRequiredService<SubjectAccessService>());
        services.TryAddScoped<IProcessingRegisterBuilder>(sp => sp.GetRequiredService<ProcessingRegisterBuilder>());

        // Needed by ProcessingRegisterBuilder even when the application configures nothing: the register
        // is designed to state what it is missing, which it cannot do without its options.
        services.AddOptions<ProcessingRegisterOptions>();

        return services;
    }
}
