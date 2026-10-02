namespace Invoicing.Billing.Events;

/// <summary>
///     An invoice issued by mistake was voided: it keeps its number, says why, and stops being chased.
/// </summary>
/// <remarks>
///     The reason travels with the event because it is the point of the operation — a void invoice with
///     no reason is a document nobody can account for — and because a handler that records the fact
///     cannot go back and read the row: by then the request is over.
/// </remarks>
public sealed record InvoiceVoided(
    Guid InvoiceId,
    string? Number,
    string? VoidReason,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
