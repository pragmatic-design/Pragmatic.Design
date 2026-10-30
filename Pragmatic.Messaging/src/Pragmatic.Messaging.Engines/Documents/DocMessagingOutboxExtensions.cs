using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pragmatic.Identity;
using Pragmatic.Messaging.Entities;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Serialization;

namespace Pragmatic.Messaging.Engines.Documents;

/// <summary>
///     The Messaging outbox of a boundary the documents engine serves. Called by the boundary's generated <c>Add{Boundary}Persistence</c> when the
///     boundary is <c>[EnableOutbox]</c>: the phase of its unit of work's save (keyed by the boundary, so another boundary's saves are not
///     captured), its <see cref="IOutboxSource" /> over the boundary's session, and — once for the application — the delivery pump, the purge and
///     the health contributor, which read every source. The collection is in the boundary's <c>{Boundary}DocSchema</c>
///     (<see cref="OutboxMessageDocument.Schema" />).
/// </summary>
public static class DocMessagingOutboxExtensions
{
    /// <summary>The outbox of <typeparamref name="TBoundary" />, named <paramref name="boundaryName" /> on its source. Call once per boundary.</summary>
    public static IServiceCollection AddDocMessagingOutbox<TBoundary>(this IServiceCollection services, string boundaryName)
    {
        services.AddKeyedScoped<IDocSavePhase>(typeof(TBoundary), static (sp, _) => new DocMessagingOutboxPhase(
            sp.GetService<ITenantContext>(), sp.GetService<ICurrentUser>(), sp.GetService<PragmaticJsonOptions>()));
        // AddScoped, not TryAddEnumerable: every boundary's source shares the implementation type, which would collapse them to one.
        services.AddScoped<IOutboxSource>(sp => new DocOutboxSource(sp.GetRequiredKeyedService<DocSession>(typeof(TBoundary)), boundaryName,
            sp.GetRequiredService<ILogger<DocOutboxSource>>(), sp.GetService<TimeProvider>()));
        return services.AddOutboxDelivery();
    }
}
