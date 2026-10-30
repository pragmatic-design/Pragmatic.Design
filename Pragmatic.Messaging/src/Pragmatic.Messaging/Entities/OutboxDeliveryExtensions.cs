using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Pragmatic.ControlPlane;

namespace Pragmatic.Messaging.Entities;

/// <summary>
///     The read side of the Messaging transactional outbox, whatever store holds the rows: the delivery pump, the retention purge and the host
///     health contributor. Each iterates every registered <see cref="IOutboxSource" />, so one instance covers every boundary of every store.
/// </summary>
public static class OutboxDeliveryExtensions
{
    /// <summary>
    ///     Registers the pump, the purge and the health contributor once, however many boundaries call it (<c>TryAddEnumerable</c>). The caller
    ///     registers the boundary's <see cref="IOutboxSource" />.
    /// </summary>
    public static IServiceCollection AddOutboxDelivery(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxDeliveryService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxPurgeService>());

        // Host health, deduped for the same reason as the pump: one contributor iterates every
        // registered IOutboxSource. It existed but was never registered, so HostHealthAggregator —
        // which the generated host DOES register — reported no outbox at all.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostHealthContributor, OutboxHealthContributor>());

        return services;
    }
}
