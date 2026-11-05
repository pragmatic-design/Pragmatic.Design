using Pragmatic.Persistence.Entity;

namespace PragmaticDocuments.Tests;

/// <summary>
///     A charge on a stay, the root of a table-per-hierarchy (#539): one collection, <c>ChargeType</c> naming each document's type. Its members
///     have private setters, as Showcase.Billing's <c>Fee</c>'s do: a derived type cannot assign them, the engine writes them through accessors.
///     Auditable, as <c>Fee</c> is. The Sql suite's hierarchy, on the documents engine.
/// </summary>
[Entity]
[Auditable]
[Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "ChargeType")]
public partial class Charge : IEntity
{
    public decimal Amount { get; private set; }

    public string Reason { get; private set; } = "";

    /// <summary>The charge's amount and reason, the only way to set them.</summary>
    public void Charged(decimal amount, string reason)
    {
        Amount = amount;
        Reason = reason;
    }
}
