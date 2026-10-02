namespace Showcase.Billing.Entities;

/// <summary>
/// Base for all fee types. Demonstrates TPH inheritance mapping pattern.
/// [Inheritance] TPH is fully generated and exercised end-to-end (see InheritanceTests).
/// Derived types: <see cref="ServiceFee"/>, <see cref="CancellationFee"/>.
/// </summary>
[Entity]
[Auditable]
[Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "FeeType")]
[Relation.ManyToOne<Invoice>]
public partial class Fee : IEntity
{

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "EUR";

    public string Reason { get; private set; } = "";

}
