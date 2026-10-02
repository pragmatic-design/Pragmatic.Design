using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Attributes;

namespace Pragmatic.Persistence.EFCore.Samples.Entities;

/// <summary>
///     Order line entity demonstrating:
///     - [Entity] with Guid identifier
///     - IEntity interface implementation
///     - Composite relationships
/// </summary>
[Entity]
[Relation.ManyToOne<Order>.WithNavigation("Order")]
[Relation.ManyToOne<Product>.WithNavigation("Product", Required = false)]
public partial class OrderLine : IEntity
{
    /// <summary>
    ///     Entity persistence identifier.
    /// </summary>
    public Guid PersistenceId { get; set; } = Guid.NewGuid();

    /// <summary>
    ///     Alias for PersistenceId for convenience.
    /// </summary>
    public Guid Id => PersistenceId;

    /// <summary>
    ///     Line quantity.
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    ///     Unit price at time of order.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    ///     Discount percentage.
    /// </summary>
    public decimal DiscountPercent { get; set; }

    /// <summary>
    ///     Line total (computed).
    ///     [Projectable] generates OrderLine.Expr.Total for SQL-translatable projection.
    /// </summary>
    [Projectable]
    public decimal Total => UnitPrice * Quantity * (1 - DiscountPercent / 100);
}
