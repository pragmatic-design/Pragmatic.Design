namespace Invoicing.Billing.Infrastructure.Audit;

/// <summary>
///     The names an invoice's moves are recorded under in the audit trail — constants, never assembled
///     at runtime, so a reader can search for them.
/// </summary>
/// <remarks>
///     ⚠️ These names are <b>stored data</b>: a trail already written keeps the old spelling, so
///     renaming one makes the history and the present disagree about the same fact. They are additive.
///     <para>
///         They sit beside what <c>[Audited]</c> writes — <c>Data.EntityUpdated</c> and its kin — and
///         say something that one cannot: not that the row changed, but what the change meant.
///     </para>
/// </remarks>
public static class BillingAuditOperations
{
    public const string InvoiceIssued = "Billing.InvoiceIssued";
    public const string InvoicePaid = "Billing.InvoicePaid";
    public const string InvoiceVoided = "Billing.InvoiceVoided";

    /// <summary>What the audit trail calls an invoice.</summary>
    public const string InvoiceTarget = nameof(Entities.Invoice);
}
