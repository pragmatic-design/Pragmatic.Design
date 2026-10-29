using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Events.Outbox;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Serialization;

namespace Pragmatic.Events.Engines.Documents;

/// <summary>
///     The event outbox of a boundary the documents engine serves. Called by the boundary's generated <c>Add{Boundary}Persistence</c> when the
///     boundary is <c>[EnableEventOutbox]</c>: the phase of its unit of work's save (keyed by the boundary, so another boundary's saves are not
///     captured), its drainer and the loop that runs it, the options and the allowlist of event types (shared by every outbox of the
///     application). The collection is in the boundary's <c>{Boundary}DocSchema</c> (<see cref="EventOutboxDocument.Schema" />).
/// </summary>
public static class DocEventOutboxExtensions
{
    /// <summary>The outbox of <typeparamref name="TBoundary" />. A second call for the same boundary adds nothing.</summary>
    public static IServiceCollection AddDocEventOutbox<TBoundary>(this IServiceCollection services, Action<EventOutboxOptions>? configure = null)
    {
        if (!EngineEventOutboxRegistration.Shared<TBoundary>(services, configure))
            return services;
        services.AddKeyedScoped<IDocSavePhase>(typeof(TBoundary), static (sp, _) => new DocEventOutboxPhase(sp.GetService<ITenantContext>(), sp.GetService<PragmaticJsonOptions>()));
        services.AddSingleton<IEngineEventOutboxDrainer<TBoundary>>(static sp => new DocEventOutboxDrainer<TBoundary>(sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<EventOutboxOptions>(), sp.GetRequiredService<ILogger<DocEventOutboxDrainer<TBoundary>>>(), sp.GetService<TimeProvider>()));
        return EngineEventOutboxRegistration.Loop<TBoundary>(services);
    }
}
