namespace Showcase.Billing.Events;

/// <summary>
///     A payment recorded against an invoice, declared by <c>MarkInvoicePaidAction</c> with <c>[Raises&lt;T&gt;]</c>: the number and the
///     total come from the invoice the action loaded, the payer from its input (#737).
/// </summary>
public sealed record InvoicePaymentRecorded(
    Guid InvoiceId,
    string InvoiceNumber,
    decimal TotalAmount,
    Guid PaidByUserId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
