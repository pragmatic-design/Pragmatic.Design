using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Audit.Engines.Sql;

/// <summary>The audit trail on the Sql engine: the entries a boundary's saves write, and the trail's store over a boundary's session.</summary>
public static class SqlAuditExtensions
{
    /// <summary>
    ///     The phase that writes an entry for each write to an <c>[Audited]</c> entity of <typeparamref name="TBoundary" />, in its save. Called
    ///     by the boundary's generated <c>Add{Boundary}Persistence</c>; keyed by the boundary, so another boundary's saves are not recorded here.
    /// </summary>
    public static IServiceCollection AddSqlAuditedEntities<TBoundary>(this IServiceCollection services)
    {
        AuditEngineRegistration.Defaults(services);
        services.AddKeyedScoped<ISqlSavePhase>(typeof(TBoundary), static (sp, _) => new SqlAuditPhase(
            sp.GetRequiredService<AuditEntryPreparer>(), sp.GetService<ICurrentUser>()));
        return services;
    }

    /// <summary>
    ///     The trail's store on <typeparamref name="TBoundary" />'s session, the database whose saves write the entries: <see cref="IAuditTrail" />,
    ///     <see cref="IAuditTrailReader" />, the sealer, the retention and the sealing worker. One per application, as EF Core's
    ///     <c>AddAuditTrail</c>: when the <c>[Audited]</c> entities live in more than one database, the application names the one the trail is read from.
    /// </summary>
    public static IServiceCollection AddSqlAuditTrail<TBoundary>(this IServiceCollection services)
        => AuditEngineRegistration.Trail(services,
            static sp => new SqlAuditTrail(Session<TBoundary>(sp), sp.GetRequiredService<AuditEntryPreparer>()),
            static sp => new SqlAuditTrailReader(Session<TBoundary>(sp)),
            static sp => new SqlAuditSealer(Session<TBoundary>(sp), sp.GetRequiredService<IAuditSegmentNaming>(), sp.GetRequiredService<TimeProvider>()),
            static sp => new SqlAuditRetention(Session<TBoundary>(sp), sp.GetRequiredService<TimeProvider>()));

    private static SqlSession Session<TBoundary>(IServiceProvider sp) => sp.GetRequiredKeyedService<SqlSession>(typeof(TBoundary));
}
