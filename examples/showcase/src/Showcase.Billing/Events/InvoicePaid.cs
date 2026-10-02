namespace Showcase.Billing.Events;

public sealed record InvoicePaid(
    Guid InvoiceId,
    Guid ReservationId,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
