using Pragmatic;
using Invoicing.Billing.Events;

namespace Invoicing.Billing.Enums;

/// <summary>
///     Where an invoice is in its life: drafted, issued, then paid or void.
/// </summary>
/// <remarks>
///     Every move that exists is declared here, and the generated <c>TransitionTo</c> refuses any other —
///     which is why no operation carries an <c>if</c> about the status. A draft is the only state that can
///     be changed at all; once issued, the document is what it says it is.
///     <para>
///         The event of a move is declared here too, and that is deliberate: only the machine knows which
///         call is the one that moves. <c>RecordPaymentAction</c> is called for every payment and cannot
///         say which one settles the invoice — the transition to <c>Paid</c> can, and it is the fact
///         worth announcing.
///     </para>
/// </remarks>
[FastEnum]
public enum InvoiceStatus
{
    [InitialState]
    Draft,

    // Back from Paid because a payment can be removed: the money was never there, and the invoice is owed
    // again. Declared here rather than assigned in the operation, so the move exists in the one list
    // anybody reads to know which moves there are.
    [TransitionFrom(InvoiceStatus.Draft)]
    [TransitionFrom(InvoiceStatus.Paid)]
    [RaisesEvent<InvoiceIssued>]
    Issued,

    [TransitionFrom(InvoiceStatus.Issued)]
    [RaisesEvent<InvoicePaid>]
    Paid,

    [TransitionFrom(InvoiceStatus.Issued)]
    [RaisesEvent<InvoiceVoided>]
    Void
}
