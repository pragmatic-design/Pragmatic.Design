namespace Pragmatic.Events.Samples.Events;

public sealed record PaymentProcessedEvent(
    Guid OrderId,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAt) : IDomainEvent;
