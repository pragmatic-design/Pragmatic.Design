using Pragmatic.Persistence.Entity;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Entities;

/// <summary>
///     The child of the <b>1:N depth 1</b> case.
/// </summary>
/// <remarks>
///     <para>
///         <c>[PartOf&lt;Order&gt;]</c> says it has no life of its own: it is written through its
///         aggregate. Without it, a mutation that carries it gets <c>PRAG0436</c>.
///     </para>
///     <para>
///         It has no operations of its own, otherwise it would be child and aggregate at once and
///         <c>PRAG0438</c> would fire.
///     </para>
/// </remarks>
[Entity]
[PartOf<Order>]
[Relation.OneToMany<Allocation>.WithNavigation("Allocations")]
[Relation.ManyToOne<Order>]
public partial class OrderLine : IEntity
{
    [Required]
    public string Product { get; private set; } = "";

    public int Quantity { get; private set; }

    /// <summary>
    ///     A value <b>the entity</b> computes: nobody writes it from outside.
    /// </summary>
    /// <remarks>
    ///     The case of <c>[MapIgnore(MappingDirection.ToEntity)]</c>. Without the direction, a
    ///     bidirectional DTO that exposes it for reading would also try to write it, and the only way to
    ///     say «read it and that is all» would be to declare two types for a single shape.
    /// </remarks>
    public string Display => $"{Product} x{Quantity}";
}
