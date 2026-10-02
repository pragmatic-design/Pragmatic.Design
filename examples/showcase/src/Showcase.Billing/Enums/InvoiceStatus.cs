using Pragmatic;
using Showcase.Billing.Events;

namespace Showcase.Billing.Enums;

[FastEnum]
public enum InvoiceStatus
{
    [InitialState]
    Draft,

    [TransitionFrom(InvoiceStatus.Draft)]
    Issued,

    [TransitionFrom(InvoiceStatus.Draft)]
    [TransitionFrom(InvoiceStatus.Issued)]
    [RaisesEvent<InvoicePaid>]
    Paid,

    [TransitionFrom(InvoiceStatus.Issued)]
    PartiallyPaid,

    [TransitionFrom(InvoiceStatus.Issued)]
    Overdue,

    [TransitionFrom(InvoiceStatus.Draft)]
    [TransitionFrom(InvoiceStatus.Issued)]
    [TransitionFrom(InvoiceStatus.Overdue)]
    Cancelled,

    // InvoiceRefunded's RefundTransactionId binds to entity.RefundTransactionId, set before the transition.
    [TransitionFrom(InvoiceStatus.Paid)]
    [RaisesEvent<InvoiceRefunded>]
    Refunded
}
