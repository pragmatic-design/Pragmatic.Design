using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Entities;

/// <summary>
///     The fourth level: <c>Order → Lines → Allocations → Tags → Notes</c>.
/// </summary>
/// <remarks>
///     <para>
///         Depth four executed on a database, not only snapshotted: a snapshot compares the generated
///         text and does not send it to a database.
///     </para>
///     <para>
///         ⚠️ It tests the <b>three-hop</b> prefix: for a note to keep its identity, the invoker must
///         include <c>Lines.Allocations.Tags.Notes</c>. The path is composed going up — <c>Notes</c> on
///         <c>AllocationTagDto</c>, prefixed by <c>AllocationDto</c>, then by <c>OrderLineDto</c>, then by
///         the invoker.
///     </para>
/// </remarks>
[Entity]
[PartOf<AllocationTag>]
[Relation.ManyToOne<AllocationTag>]
public partial class TagNote : IEntity
{
    [Required]
    public string Text { get; private set; } = "";
}
