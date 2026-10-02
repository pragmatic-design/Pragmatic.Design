using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Entities;

/// <summary>
///     The second level: what makes the shape <b>two deep</b>.
/// </summary>
/// <remarks>
///     <para>
///         It is a child of <c>OrderLine</c>, which in turn is a child of <c>Order</c>. Writing an order
///         must therefore reach it through two navigations.
///     </para>
///     <para>
///         ⚠️ It is the case that exercises the prefixed <c>WrittenNavigations</c>: the invoker must
///         include <c>Lines.Allocations</c>, not just <c>Lines</c>. If it stopped at the first level, the
///         merge would find the allocations unloaded and rewrite them all.
///     </para>
/// </remarks>
[Entity]
[PartOf<OrderLine>]
[Relation.OneToMany<AllocationTag>.WithNavigation("Tags")]
[Relation.ManyToOne<OrderLine>]
public partial class Allocation : IEntity
{
    [Required]
    public string Warehouse { get; private set; } = "";

    public int Quantity { get; private set; }

    /// <summary>
    ///     The value object inside a written child.
    /// </summary>
    /// <remarks>
    ///     Two columns on the same row, <c>Slot_Aisle</c> and <c>Slot_Shelf</c>. The question the case
    ///     asks is whether its <em>content</em> survives a nested write, not whether the element is still
    ///     there.
    /// </remarks>
    public StorageSlot Slot { get; private set; } = new("", 0);
}
