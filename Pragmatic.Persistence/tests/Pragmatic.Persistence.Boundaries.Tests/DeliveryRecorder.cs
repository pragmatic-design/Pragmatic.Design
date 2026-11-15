using Pragmatic.Events;
using Pragmatic.MultiTenancy;

namespace PragmaticBoundaries.Tests;

/// <summary>
///     The handler of the boundaries' events, those the entities raise and those the operations declare: it records each one and the
///     tenant it ran under.
/// </summary>
public sealed class DeliveryRecorder(Deliveries deliveries, ITenantContext tenant)
    : IDomainEventHandler<ProductListed>, IDomainEventHandler<ProductReviewed>, IDomainEventHandler<ProductRenamed>, IDomainEventHandler<ReviewEdited>,
      IDomainEventHandler<OrderRenumbered>
{
    public Task HandleAsync(ProductListed @event, CancellationToken ct = default) => Record(@event);

    public Task HandleAsync(ProductReviewed @event, CancellationToken ct = default) => Record(@event);

    public Task HandleAsync(ProductRenamed @event, CancellationToken ct = default) => Record(@event);

    public Task HandleAsync(ReviewEdited @event, CancellationToken ct = default) => Record(@event);

    public Task HandleAsync(OrderRenumbered @event, CancellationToken ct = default) => Record(@event);

    private Task Record(object @event)
    {
        deliveries.Received.Enqueue((@event, tenant.TenantId));
        return Task.CompletedTask;
    }
}
