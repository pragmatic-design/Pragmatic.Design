namespace Pragmatic.Events.Samples.Events;

/// <summary>
///     Raised when a new order is placed.
/// </summary>
public sealed record OrderPlacedEvent(
    Guid OrderId,
    string Product,
    int Quantity,
    DateTimeOffset OccurredAt) : IDomainEvent;
