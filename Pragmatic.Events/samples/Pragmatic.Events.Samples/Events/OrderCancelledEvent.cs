namespace Pragmatic.Events.Samples.Events;

/// <summary>
///     Raised when an order is cancelled.
/// </summary>
public sealed record OrderCancelledEvent(
    Guid OrderId,
    string Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;
