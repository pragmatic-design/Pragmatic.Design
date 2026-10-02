using Pragmatic.Messaging.Attributes;
using Pragmatic.Messaging.Saga;
using Warehouse.Orders.Contracts.Events;
using Warehouse.Orders.Events;
using Warehouse.Shipping.Contracts.Events;
using Warehouse.Stock.Contracts.Events;

namespace Warehouse.Orders.Infrastructure.Sagas;

/// <summary>
///     The process that carries a confirmed order to its carrier: Stock picks it, Shipping dispatches it,
///     and the order ends <c>Shipped</c>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>It lives in Orders, and Stock and Shipping do not know it exists.</b> They react to what
///         they are told and publish what they did; this class is the one place that says what an order is
///         waiting for, and its rows in <c>__SagaInstances</c> and <c>__SagaSteps</c> say it at run time.
///         A restart of Orders in the middle resumes it from those rows (<c>[EnableSagaPersistence]</c>).
///     </para>
///     <para>
///         <b>No dependencies</b>, as a saga has none: its effects leave as messages
///         (<see cref="RecordOrderPicked" />, <see cref="RecordOrderShipped" />), which the orchestrator
///         publishes after saving the state and which Orders' own handlers carry out through the order's
///         machine.
///     </para>
///     <para>
///         A message that arrives in the wrong state is not this process's step: a second
///         <c>OrderPicked</c> finds it <see cref="Fulfilment.AwaitingDispatch" />, and the order is not
///         moved twice.
///     </para>
/// </remarks>
[Saga<Fulfilment>]
public partial class OrderFulfilmentSaga : ISaga<Fulfilment>
{
    /// <inheritdoc />
    public Guid Id { get; set; }

    /// <inheritdoc />
    public Fulfilment State { get; set; }

    /// <inheritdoc />
    public string CorrelationId { get; set; } = "";

    /// <inheritdoc />
    public DateTimeOffset StartedAt { get; set; }

    /// <inheritdoc />
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>The order this process is about — the same value as <see cref="CorrelationId" />, typed.</summary>
    public Guid OrderId { get; set; }

    /// <summary>The order was confirmed: the process begins, waiting for the picker.</summary>
    /// <remarks>
    ///     It publishes nothing: <c>OrderReadyToPick</c> already told Stock, which makes the holds permanent
    ///     and lets the picker take them.
    /// </remarks>
    [SagaStart]
    [InState(Fulfilment.AwaitingPick)]
    public void WhenTheOrderIsConfirmed(OrderReadyToPick confirmed) => OrderId = confirmed.OrderId;

    /// <summary>Stock picked it: the order is marked picked, and the process waits for the carrier.</summary>
    /// <remarks>The order is the process's own — the event was routed here because it named the same one.</remarks>
    [InState(Fulfilment.AwaitingPick, NextState = Fulfilment.AwaitingDispatch)]
    public RecordOrderPicked WhenTheOrderIsPicked(OrderPicked picked) => new(OrderId);

    /// <summary>The shipment left: the order is marked shipped, and the process is over.</summary>
    [InState(Fulfilment.AwaitingDispatch, NextState = Fulfilment.Shipped)]
    public RecordOrderShipped WhenTheShipmentLeaves(ShipmentDispatched dispatched)
    {
        CompletedAt = dispatched.OccurredAt;
        return new RecordOrderShipped(OrderId);
    }

    /// <summary>Whether Shipping has to say it withdrew a shipment: true once the order was picked.</summary>
    public bool ShipmentToWithdraw { get; set; }

    /// <summary>Stock said it restored what it held or picked for this order.</summary>
    public bool StockRestored { get; set; }

    /// <summary>Shipping said it withdrew this order's shipment.</summary>
    public bool ShipmentWithdrawn { get; set; }

    /// <summary>
    ///     The order was cancelled: the process waits for the services that did something to undo it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It sends nothing. <c>OrderCancelled</c> already reached Stock and Shipping, and each undoes its
    ///         part from its own state. What this step knows that they do not is <b>who has to answer</b>:
    ///         Stock always, Shipping only if the order had been picked and so has a shipment.
    ///     </para>
    ///     <para>
    ///         An acknowledgement can overtake the cancellation on its way here — the services' queues and
    ///         this one are not ordered against each other — so the steps below also record one that
    ///         arrives while the process is still waiting for the picker or the carrier, and this step
    ///         finishes at once when everything it needs is already in.
    ///     </para>
    /// </remarks>
    [InState(Fulfilment.AwaitingPick, NextState = Fulfilment.Compensating)]
    [InState(Fulfilment.AwaitingDispatch, NextState = Fulfilment.Compensating)]
    public void WhenTheOrderIsCancelled(OrderCancelled cancelled)
    {
        ShipmentToWithdraw = State == Fulfilment.AwaitingDispatch;
        State = Fulfilment.Compensating;
        FinishIfUndone(cancelled.OccurredAt);
    }

    /// <summary>Stock restored the order's stock.</summary>
    [InState(Fulfilment.AwaitingPick)]
    [InState(Fulfilment.AwaitingDispatch)]
    [InState(Fulfilment.Compensating)]
    public void WhenTheStockIsRestored(StockRestored restored)
    {
        StockRestored = true;
        FinishIfUndone(restored.OccurredAt);
    }

    /// <summary>Shipping withdrew the order's shipment.</summary>
    [InState(Fulfilment.AwaitingPick)]
    [InState(Fulfilment.AwaitingDispatch)]
    [InState(Fulfilment.Compensating)]
    public void WhenTheShipmentIsWithdrawn(ShipmentWithdrawn withdrawn)
    {
        ShipmentWithdrawn = true;
        FinishIfUndone(withdrawn.OccurredAt);
    }

    /// <summary>
    ///     <see cref="Fulfilment.Compensated" /> once cancelled and every acknowledgement it needs is in —
    ///     Stock's always, Shipping's when there was a shipment. Never before the cancellation itself.
    /// </summary>
    private void FinishIfUndone(DateTimeOffset at)
    {
        if (State != Fulfilment.Compensating || !StockRestored || (ShipmentToWithdraw && !ShipmentWithdrawn))
            return;

        State = Fulfilment.Compensated;
        CompletedAt = at;
    }

}
