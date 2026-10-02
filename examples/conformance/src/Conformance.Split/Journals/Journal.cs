using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Split.Entities;

/// <summary>
///     The entity <c>JournalBoundary</c> claims, in the same assembly and in another context.
/// </summary>
/// <remarks>
///     The twin of <c>Ledger</c>, and the reason the case is measurable: two entities that <b>no</b>
///     declaration ties together, each in the <c>DbContext</c> of the boundary that claimed it. With a
///     single entity, «it sits in the right context» would be satisfied by a generator that puts
///     everything in every context.
/// </remarks>
[Entity]
public partial class Journal : IEntity
{
    [Required]
    public string Name { get; private set; } = "";
}
