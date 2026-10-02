using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Split.Entities;

/// <summary>
///     The entity <c>LedgerBoundary</c> claims.
/// </summary>
/// <remarks>
///     It does not and cannot carry its boundary's name: the boundary claims the entity, and that
///     direction is what keeps a folder of entities portable — copy it into another project and it
///     belongs to the boundary it finds there, with nothing to change. An attribute on the entity would
///     name a type of the old project and would not compile.
/// </remarks>
[Entity]
public partial class Ledger : IEntity
{
    [Required]
    public string Name { get; private set; } = "";
}
