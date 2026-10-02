using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Entities;

/// <summary>
///     A row that is <b>chosen</b>, not written — the case of <c>[LinkIds]</c>.
/// </summary>
/// <remarks>
///     <para>
///         A label exists on its own and nobody modifies it while updating an order: what changes is
///         <b>which</b> labels the order has. Sending back its whole shape to link it is a query per write
///         that buys nothing — EF needs the key and nothing more to write the join row.
///     </para>
///     <para>
///         ⚠️ It is not <c>[PartOf&lt;Order&gt;]</c>, and that is the difference that matters: a child of
///         the aggregate is written through the parent, this one is only <b>linked</b>. Removing it from
///         an order does not delete it — the join row disappears, and <c>TheLinkedRows</c> measures it.
///     </para>
/// </remarks>
[Entity]
public partial class Label : IEntity
{
    [Required]
    public string Name { get; private set; } = "";
}
