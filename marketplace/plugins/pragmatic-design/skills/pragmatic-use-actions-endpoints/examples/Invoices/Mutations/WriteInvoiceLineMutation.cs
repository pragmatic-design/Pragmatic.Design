using Pragmatic.Internationalization.Types;

namespace Invoicing.Billing.Invoices.Mutations;

/// <summary>
///     A line, written as a child of its invoice: a mutation, not a DTO.
/// </summary>
/// <remarks>
///     No <c>[Endpoint]</c>, and that is what makes it a child rather than an operation. Its rules travel
///     with it — a quantity must be positive, a price cannot be negative, and the VAT rate must be one of
///     the rates in use — so a line written through its invoice passes exactly the checks a line written
///     on its own would.
/// </remarks>
[Mutation(Mode = MutationMode.Update)]
public partial class WriteInvoiceLineMutation : Mutation<InvoiceLine>
{
    /// <summary>The key the merge pairs on: an id that is already there updates the line, a new one adds it.</summary>
    public Guid Id { get; init; }

    [Required]
    [MaxLength(300)]
    public string Description { get; init; } = "";

    [Positive]
    public decimal Quantity { get; init; }

    /// <summary>
    ///     The price of one unit, in the currency this application bills in.
    /// </summary>
    /// <remarks>
    ///     <c>[SupportedCurrency]</c> and not an invariant on the invoice: the amount arrives with its
    ///     own currency inside it, and the totals are <c>Money.Zero(Invoice.Euro)</c>, so a line in
    ///     dollars is not a line worth less or more — it is a line that cannot be added to them. The
    ///     rule belongs where the value arrives, before anything sums it.
    ///     ⚠️ Distinct from <c>CurrencyMismatchError</c>, which is the payments' rule: that one asks
    ///     whether the money matches <em>this</em> invoice and answers 409 on the loaded row. This one
    ///     asks whether the application takes that currency at all, and answers 422 before any load.
    /// </remarks>
    [NonNegativeMoney]
    [SupportedCurrency("EUR")]
    public Money UnitPrice { get; init; }

    /// <summary>Checked by the line's own invariant: a decimal cannot be an attribute argument (CS0182).</summary>
    public decimal VatRate { get; init; }
}
