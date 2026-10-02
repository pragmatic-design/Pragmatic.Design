namespace Invoicing.Billing.Events;

/// <summary>
///     An invoice became a document: it has a number, and a customer may already hold it.
/// </summary>
/// <remarks>
///     Raised by the state machine on the move itself, whatever operation takes it — the fact is the
///     issue, and <c>IssueInvoiceAction</c> is one way to reach it.
///     <para>
///         ⚠️ Also raised on the move <b>back</b> from <c>Paid</c>, which exists because a payment can be
///         removed: the money was never there and the invoice is owed again. That is an issue of the same
///         document under the same number, which is why the event carries the number rather than a
///         "first time" flag nobody could compute here.
///     </para>
/// </remarks>
public sealed record InvoiceIssued(
    Guid InvoiceId,
    string? Number,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
