namespace Invoicing.Billing.Events;

/// <summary>
///     An invoice is settled: what was owed has arrived, and it stops being chased.
/// </summary>
/// <remarks>
///     Raised by the transition and not by <c>RecordPaymentAction</c>, which is the difference that
///     matters: a partial payment records money without settling anything, and this event is about the
///     settlement. The operation cannot know which of its calls is the last one — the machine does.
/// </remarks>
public sealed record InvoicePaid(
    Guid InvoiceId,
    string? Number,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
