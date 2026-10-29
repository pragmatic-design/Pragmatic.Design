using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Events.Outbox;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Sql;
using Pragmatic.Serialization;

namespace Pragmatic.Events.Engines.Sql;

/// <summary>
///     The event outbox of a boundary the Sql engine serves. Called by the boundary's generated <c>Add{Boundary}Persistence</c> when the boundary
///     is <c>[EnableEventOutbox]</c>: the phase of its unit of work's save (keyed by the boundary, so another boundary's saves are not captured),
///     its drainer and the loop that runs it, the options and the allowlist of event types (shared by every outbox of the application).
/// </summary>
public static class SqlEventOutboxExtensions
{
    /// <summary>The outbox of <typeparamref name="TBoundary" />. A second call for the same boundary adds nothing.</summary>
    public static IServiceCollection AddSqlEventOutbox<TBoundary>(this IServiceCollection services, Action<EventOutboxOptions>? configure = null)
    {
        if (!EngineEventOutboxRegistration.Shared<TBoundary>(services, configure))
            return services;
        services.AddKeyedScoped<ISqlSavePhase>(typeof(TBoundary), static (sp, _) => new SqlEventOutboxPhase(sp.GetService<ITenantContext>(), sp.GetService<PragmaticJsonOptions>()));
        services.AddSingleton<IEngineEventOutboxDrainer<TBoundary>>(static sp => new SqlEventOutboxDrainer<TBoundary>(sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<EventOutboxOptions>(), sp.GetRequiredService<ILogger<SqlEventOutboxDrainer<TBoundary>>>(), sp.GetService<TimeProvider>()));
        return EngineEventOutboxRegistration.Loop<TBoundary>(services);
    }
}
