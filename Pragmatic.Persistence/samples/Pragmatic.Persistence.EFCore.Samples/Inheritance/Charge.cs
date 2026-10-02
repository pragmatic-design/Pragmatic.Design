using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Inheritance;

/// <summary>
///     Base of a charge hierarchy mapped Table-Per-Hierarchy.
///     <c>[Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "ChargeType")]</c> makes the
///     SG emit <c>ChargeInheritanceConfiguration.Configure(modelBuilder)</c>, which calls
///     <c>HasDiscriminator&lt;string&gt;("ChargeType")</c> with a <c>HasValue</c> per derived type.
///     Derived types: <see cref="LateFee"/>, <see cref="UsageCharge"/>.
/// </summary>
[Entity]
[Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "ChargeType")]
public partial class Charge : IEntity
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();

    public Guid Id => PersistenceId;

    public decimal Amount { get; set; }

    public string Description { get; set; } = "";
}
