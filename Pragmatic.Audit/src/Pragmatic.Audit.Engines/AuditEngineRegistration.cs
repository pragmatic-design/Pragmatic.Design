using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Audit.Engines;

/// <summary>What both engines register the same way EF Core's <c>AddAuditTrail</c> does.</summary>
internal static class AuditEngineRegistration
{
    /// <summary>
    ///     The clock, the redactor, the segment naming and the preparer, each once and replaceable (<c>TryAdd</c>): the phases and the store
    ///     shape and segment entries with the same ones, so a segment a phase opens is one the sealer closes.
    /// </summary>
    public static void Defaults(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAuditDetailRedactor, PatternAuditDetailRedactor>();
        services.TryAddSingleton<IAuditSegmentNaming, HourlyAuditSegmentNaming>();
        services.TryAddSingleton(sp => new AuditEntryPreparer(
            sp.GetRequiredService<IAuditDetailRedactor>(), sp.GetRequiredService<IAuditSegmentNaming>(), sp.GetRequiredService<TimeProvider>()));
    }

    /// <summary>
    ///     The trail's store, the contracts each to its factory: the first store an application registers is its trail (<c>TryAdd</c>), and the
    ///     sealing worker runs once. Verification checks sealed segments only, so the sealer is part of having a trail, not an extra to remember.
    /// </summary>
    public static IServiceCollection Trail(IServiceCollection services, Func<IServiceProvider, IAuditTrail> trail, Func<IServiceProvider, IAuditTrailReader> reader,
        Func<IServiceProvider, IAuditSealer> sealer, Func<IServiceProvider, IAuditRetention> retention)
    {
        Defaults(services);
        services.TryAddScoped(trail);
        services.TryAddScoped(reader);
        services.TryAddScoped(sealer);
        services.TryAddScoped(retention);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AuditSealingWorker>());
        return services;
    }
}
