namespace Showcase.Billing.Events;

/// <summary>
/// Raised when an invoice is successfully refunded.
/// Demonstrates: domain event cross-boundary propagation for refund tracking.
/// </summary>
public sealed record InvoiceRefunded(
    Guid InvoiceId,
    Guid ReservationId,
    // TotalAmount/RefundTransactionId match the entity members by name so [RaisesEvent] auto-fills them.
    decimal TotalAmount,
    string Currency,
    string? RefundTransactionId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
