using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity;
using Pragmatic.Persistence.Documents;

namespace Pragmatic.Audit.Engines.Documents;

/// <summary>The audit trail on the documents engine: the entries a boundary's saves write, and the trail's store over a boundary's session.</summary>
public static class DocAuditExtensions
{
    /// <summary>
    ///     The phase that writes an entry for each write to an <c>[Audited]</c> entity of <typeparamref name="TBoundary" />, in its save. Called
    ///     by the boundary's generated <c>Add{Boundary}Persistence</c>; keyed by the boundary. The collections are in the boundary's
    ///     <c>{Boundary}DocSchema</c>.
    /// </summary>
    public static IServiceCollection AddDocAuditedEntities<TBoundary>(this IServiceCollection services)
    {
        AuditEngineRegistration.Defaults(services);
        services.AddKeyedScoped<IDocSavePhase>(typeof(TBoundary), static (sp, _) => new DocAuditPhase(
            sp.GetRequiredService<AuditEntryPreparer>(), sp.GetService<ICurrentUser>()));
        return services;
    }

    /// <summary>
    ///     The trail's store on <typeparamref name="TBoundary" />'s session: <see cref="IAuditTrail" />, <see cref="IAuditTrailReader" />, the
    ///     sealer, the retention and the sealing worker. One per application, as EF Core's <c>AddAuditTrail</c>.
    /// </summary>
    public static IServiceCollection AddDocAuditTrail<TBoundary>(this IServiceCollection services)
        => AuditEngineRegistration.Trail(services,
            static sp => new DocAuditTrail(Session<TBoundary>(sp), sp.GetRequiredService<AuditEntryPreparer>()),
            static sp => new DocAuditTrailReader(Session<TBoundary>(sp)),
            static sp => new DocAuditSealer(Session<TBoundary>(sp), sp.GetRequiredService<IAuditSegmentNaming>(), sp.GetRequiredService<TimeProvider>()),
            static sp => new DocAuditRetention(Session<TBoundary>(sp), sp.GetRequiredService<TimeProvider>()));

    private static DocSession Session<TBoundary>(IServiceProvider sp) => sp.GetRequiredKeyedService<DocSession>(typeof(TBoundary));
}
