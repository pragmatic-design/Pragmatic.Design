namespace Warehouse.Orders.Entities;

/// <summary>
///     One line of an order: which product, by SKU, and how many.
/// </summary>
/// <remarks>
///     <c>[PartOf&lt;Order&gt;]</c> because it has no life of its own: written through its order, in the
///     order's transaction and under the order's permission.
/// </remarks>
[Entity]
[PartOf<Order>]
[Relation.ManyToOne<Order>]
public partial class OrderLine : IEntity
{
    [Required]
    [MaxLength(40)]
    public string Sku { get; private set; } = "";

    [GreaterThan(0)]
    public int Quantity { get; private set; }

    /// <summary>
    ///     How many of <see cref="Quantity" /> Stock could not hold when the order was placed, and waits for.
    ///     Zero unless Stock accepts backorders (its <c>AcceptBackorders</c> switch).
    /// </summary>
    [GreaterThanOrEqual(0)]
    public int Backordered { get; private set; }

    /// <summary>Stock held this line only in part: the rest is backordered.</summary>
    internal void MarkBackordered(int quantity) => Backordered = quantity;
}
