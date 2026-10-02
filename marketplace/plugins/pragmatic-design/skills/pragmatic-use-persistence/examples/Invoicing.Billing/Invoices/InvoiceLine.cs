using Pragmatic.Internationalization.Types;

namespace Invoicing.Billing.Entities;

/// <summary>
///     One line of an invoice: what was sold, how much of it, at what price and at which VAT rate.
/// </summary>
/// <remarks>
///     <c>[PartOf&lt;Invoice&gt;]</c> because it has no life of its own: it is written through its invoice,
///     in the invoice's transaction and under the invoice's permission, and nothing addresses a line on
///     its own. It sits beside its aggregate, with no folder of its own, for the same reason.
/// </remarks>
[Entity]
[PartOf<Invoice>]
[Relation.ManyToOne<Invoice>]
public partial class InvoiceLine : IEntity
{
    [Required]
    [MaxLength(300)]
    public string Description { get; private set; } = "";

    [Positive]
    public decimal Quantity { get; private set; }

    [NonNegativeMoney]
    public Money UnitPrice { get; private set; } = Money.Zero(Invoice.Euro);

    /// <summary>The VAT rate as a percentage — one of the rates in use, not an arbitrary number.</summary>
    /// <remarks>
    ///     ⚠️ Not <c>[OneOf(0m, 4m, …)]</c>: a <c>decimal</c> is not a constant expression, so decimal
    ///     arguments to an attribute are <b>CS0182</b> and that rule cannot be written as one at all. The
    ///     set of legal rates is therefore an invariant of the line — checked after the write is applied
    ///     and before it lands, with the same 422 a declarative rule would have given.
    /// </remarks>
    public decimal VatRate { get; private set; }

    /// <summary>The rates in use in Italy. A line at any other rate is not a line anybody may send.</summary>
    /// <remarks>
    ///     <para>
    ///         Declared here, on the entity the rule is about, and checked when the invoice that carries
    ///         this line is written — which is the only way a line is ever written.
    ///     </para>
    ///     <para>
    ///         The refusal names its key, so it is read in the caller's language like every other message
    ///         this application shows; the sentence above is what a host with no translation for it
    ///         answers.
    ///     </para>
    /// </remarks>
    [Invariant("The VAT rate must be one of 0, 4, 5, 10 or 22 per cent",
        MessageKey = "error.unknown_vat_rate")]
    internal bool ChargesARateInUse() => VatRate is 0m or 4m or 5m or 10m or 22m;

    /// <summary>Quantity × price, rounded to the cent. Written by the invoice, never from outside.</summary>
    public Money LineNet { get; private set; } = Money.Zero(Invoice.Euro);

    /// <summary>The VAT of this line, on its rounded net. Written by the invoice.</summary>
    public Money LineVat { get; private set; } = Money.Zero(Invoice.Euro);

    /// <summary>
    ///     Rounds this line: net first, then the VAT of that rounded net.
    /// </summary>
    /// <remarks>
    ///     Banker's rounding, and twice: the VAT of a line is a percentage of what the line actually
    ///     charges, not of a longer number that is never shown.
    /// </remarks>
    internal void Recalculate(CurrencyCode currency)
    {
        var net = (Money.From(UnitPrice.Amount, currency) * Quantity).Round(2, MidpointRounding.ToEven);

        SetLineNet(net);
        SetLineVat((net * VatRate / 100m).Round(2, MidpointRounding.ToEven));
    }
}
