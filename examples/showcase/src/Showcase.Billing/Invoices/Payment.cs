namespace Showcase.Billing.Entities;

/// <summary>
/// A payment made against an invoice.
/// </summary>
[Entity]
[Auditable]
[SoftDelete]
[Relation.ManyToOne<Invoice>]
public partial class Payment : IEntity
{
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "EUR";

    public PaymentMethod Method { get; private set; }

    public string? TransactionReference { get; private set; }

    public DateTimeOffset PaidAt { get; private set; }

}
