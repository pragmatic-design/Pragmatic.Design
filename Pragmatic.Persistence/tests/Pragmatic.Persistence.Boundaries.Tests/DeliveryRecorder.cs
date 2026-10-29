using Pragmatic.Events;
using Pragmatic.MultiTenancy;

namespace PragmaticBoundaries.Tests;

/// <summary>The handler of both boundaries' events: it records each one and the tenant it ran under.</summary>
public sealed class DeliveryRecorder(Deliveries deliveries, ITenantContext tenant) : IDomainEventHandler<ProductListed>, IDomainEventHandler<ProductReviewed>
{
    public Task HandleAsync(ProductListed @event, CancellationToken ct = default) => Record(@event);

    public Task HandleAsync(ProductReviewed @event, CancellationToken ct = default) => Record(@event);

    private Task Record(object @event)
    {
        deliveries.Received.Enqueue((@event, tenant.TenantId));
        return Task.CompletedTask;
    }
}
