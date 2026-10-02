using Pragmatic.Internationalization.Types;
using Pragmatic.MultiTenancy;

namespace Invoicing.Billing.Entities;

/// <summary>
///     What a customer paid against one invoice: when, how much, by what means, and the reference they
///     quoted.
/// </summary>
/// <remarks>
///     <para>
///         It has a folder of its own because it has operations of its own — it is recorded and removed by
///         name — which is the question that separates an aggregate from a child. A line is not: nothing
///         addresses a line.
///     </para>
///     <para>
///         Written only through <see cref="Invoice.RecordPayment" /> and
///         <see cref="Invoice.RemovePayment" />, because the amount paid is kept on the invoice and the two
///         must move together. The constructor here is <c>internal</c> for the same reason.
///     </para>
/// </remarks>
[Entity]
[Audited]
[ConcurrencyAware]
// No OnDelete here: the invoice declares the collection that owns this relationship, and PRAG0611 refuses
// a value set on this side — which is right, because the one that would be read is the other.
[Relation.ManyToOne<Invoice>.WithNavigation("Invoice", Inverse = "Payments")]
public partial class Payment : IEntity, ITenantEntity
{
    /// <summary>The company this payment belongs to, filled by the interceptor and filtered on by every read.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>The day the money arrived — the customer's date, not the day it was typed in.</summary>
    public DateOnly PaidOn { get; private set; }

    /// <summary>
    ///     How much arrived, in the invoice's currency.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The rule that this is positive is declared on <c>RecordPaymentAction.Amount</c>, where it
    ///     runs: an action writes the entity without a mutation, and the validation attributes of an entity
    ///     — like its invariants — are checked by the mutation invoker, which is not on this path.
    /// </remarks>
    public Money Amount { get; private set; } = Money.Zero(Invoice.Euro);

    /// <summary>What the customer quoted: a transfer's reference, a receipt number, the end of a card.</summary>
    [MaxLength(100)]
    public string? Reference { get; private set; }

    public PaymentMethod Method { get; private set; }

    /// <summary>A payment as it is recorded. Internal: the invoice is the only thing that makes one.</summary>
    internal static Payment Record(DateOnly paidOn, Money amount, string? reference, PaymentMethod method)
    {
        var payment = Create();

        payment.SetPaidOn(paidOn);
        payment.SetAmount(amount);
        payment.SetReference(reference);
        payment.SetMethod(method);

        return payment;
    }
}
