using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Audit.EFCore;

/// <summary>
///     Registers the EF Core-backed audit trail.
/// </summary>
public static class AuditEfCoreExtensions
{
    /// <summary>
    ///     Registers <see cref="IAuditTrail" />, <see cref="IAuditTrailReader" />, and the sealing and
    ///     retention services over <see cref="AuditDbContext" />, and <see cref="AuditSealingWorker" /> to
    ///     seal the trail while the host runs.
    /// </summary>
    /// <remarks>
    ///     The redactor is registered with <c>TryAdd</c>, so a deployment that has its own notion of
    ///     sensitive text can replace it — but never remove it: <see cref="IAuditTrail" /> requires one,
    ///     because a trail that only redacts when someone remembered to configure it does not hold the
    ///     property the rest of the design depends on.
    /// </remarks>
    public static IServiceCollection AddAuditTrail(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAuditDetailRedactor, PatternAuditDetailRedactor>();
        services.TryAddSingleton<IAuditSegmentNaming, HourlyAuditSegmentNaming>();

        services.TryAddScoped<EfCoreAuditTrail>();
        services.TryAddScoped<IAuditTrail>(sp => sp.GetRequiredService<EfCoreAuditTrail>());

        // The same instance behind both contracts, so a caller that enlists and a caller that does not
        // cannot end up writing through two different DbContexts in the same scope.
        services.TryAddScoped<ITransactionalAuditTrail>(sp => sp.GetRequiredService<EfCoreAuditTrail>());
        services.TryAddScoped<IAuditTrailReader, EfCoreAuditTrailReader>();
        services.TryAddScoped<AuditSealingService>();

        // Verification checks sealed segments only, so a trail nobody seals verifies as intact whatever
        // happened to it. The sealer is part of having a trail, not an extra to remember.
        services.AddHostedService<AuditSealingWorker>();
        services.TryAddScoped<AuditRetentionService>();
        // Also by interface: an operation that prunes depends on IAuditRetention rather than on the
        // store that implements it, and a concrete type is not injected into an action at all.
        services.TryAddScoped<global::Pragmatic.Audit.IAuditRetention>(sp => sp.GetRequiredService<AuditRetentionService>());

        return services;
    }
}
