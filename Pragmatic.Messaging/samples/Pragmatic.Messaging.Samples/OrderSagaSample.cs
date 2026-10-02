using Pragmatic.Messaging;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Samples;

// =========================================================================
// Sample: Order Saga — demonstrates [Saga<TState>] pattern
//
// Flow: OrderRequested → PaymentPending → ShippingPending → Completed
// Compensation: PaymentReceived fails → RefundPayment dispatched
// =========================================================================

/// <summary>Saga state enum — pure values, no attributes needed.</summary>
public enum OrderSagaState
{
    Initial,
    PaymentPending,
    ShippingPending,
    Completed,
    Cancelled,
    Refunded
}

// Events that trigger saga transitions. ICorrelatedMessage is required so the
// SG-generated EventHandler_<T> can feed CorrelationId to the orchestrator —
// the saga uses OrderId as the correlation key across its lifecycle.
public record OrderRequested(Guid OrderId, decimal Total, string CustomerId) : ICorrelatedMessage
{
    public string CorrelationId => OrderId.ToString();
}

public record PaymentReceived(Guid OrderId, string TransactionId) : ICorrelatedMessage
{
    public string CorrelationId => OrderId.ToString();
}

public record OrderShipped(Guid OrderId, string TrackingNumber) : ICorrelatedMessage
{
    public string CorrelationId => OrderId.ToString();
}

public record PaymentFailed(Guid OrderId, string Reason) : ICorrelatedMessage
{
    public string CorrelationId => OrderId.ToString();
}

// DomainActions dispatched by saga steps (would be [DomainAction] in real code)
public record ProcessPaymentAction(Guid OrderId, decimal Amount);
public record ShipOrderAction(Guid OrderId);
public record RefundPaymentAction(Guid OrderId, string TransactionId);
public record CancelOrderAction(Guid OrderId, string Reason);

/// <summary>
///     Order saga: orchestrates payment → shipping → completion.
///     The SG generates OrderSagaOrchestrator with:
///     - State routing (switch on current state + event type)
///     - Compensation chain ([CompensateWith] dispatches on failure)
///     - Transition validation (compile-time graph from [InState] + NextState)
/// </summary>
[Saga<OrderSagaState>]
public partial class OrderSaga : ISaga<OrderSagaState>
{
    public Guid Id { get; set; }
    public OrderSagaState State { get; set; }
    public string CorrelationId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    // Saga-specific data
    public decimal OrderTotal { get; set; }
    public string? TransactionId { get; set; }

    /// <summary>
    ///     Test hook: the SagaFlowSample flips this to <c>true</c> to simulate an in-step
    ///     failure inside <c>Handle(PaymentReceived)</c>. Real sagas would throw based on
    ///     a rejected downstream call; for a console demo a static switch keeps the sample
    ///     self-contained without introducing a fake transport.
    /// </summary>
    public static bool ThrowOnNextPaymentReceived { get; set; }

    /// <summary>Entry point: order requested → dispatch payment.</summary>
    [SagaStart]
    public ProcessPaymentAction Handle(OrderRequested @event)
    {
        OrderTotal = @event.Total;
        // User-code transition: the SagaStart step drives the saga from the
        // default state to PaymentPending so the orchestrator routes follow-up
        // events to the PaymentReceived / PaymentFailed handlers below.
        State = OrderSagaState.PaymentPending;
        return new ProcessPaymentAction(@event.OrderId, @event.Total);
    }

    /// <summary>
    ///     Payment received → dispatch shipping. Compensate with refund on failure
    ///     (either the step throws, or the follow-up <c>OrderShipped</c> never arrives
    ///     within the <see cref="SagaTimeoutAttribute"/> window).
    /// </summary>
    [InState(OrderSagaState.PaymentPending, NextState = OrderSagaState.ShippingPending)]
    [CompensateWith<RefundPaymentAction>]
    [SagaTimeout(Duration = "00:00:02")]
    public ShipOrderAction Handle(PaymentReceived @event)
    {
        TransactionId = @event.TransactionId;
        if (ThrowOnNextPaymentReceived)
        {
            ThrowOnNextPaymentReceived = false;
            throw new InvalidOperationException("downstream payment gateway rejected (simulated)");
        }
        return new ShipOrderAction(@event.OrderId);
    }

    /// <summary>Order shipped → saga complete.</summary>
    [InState(OrderSagaState.ShippingPending, NextState = OrderSagaState.Completed)]
    public object? Handle(OrderShipped @event)
    {
        CompletedAt = DateTimeOffset.UtcNow;
        return null; // No further action
    }

    /// <summary>Payment failed → cancel order.</summary>
    [InState(OrderSagaState.PaymentPending, NextState = OrderSagaState.Cancelled)]
    public CancelOrderAction Handle(PaymentFailed @event)
    {
        State = OrderSagaState.Cancelled;
        return new CancelOrderAction(@event.OrderId, @event.Reason);
    }
}
