using Showcase.Catalog.Entities;

namespace Showcase.Billing.Entities;

/// <summary>
/// A line item on an invoice (e.g. room charge, taxes, extras).
/// Demonstrates [CascadeOn] — when RoomType.BaseRate changes, UnitPrice is updated.
/// </summary>
[Entity]
[Relation.ManyToOne<Invoice>]
[Relation.ManyToOne<RoomType>.WithNavigation("RoomType", Required = false)]
public partial class LineItem : IEntity
{

    public string Description { get; private set; } = "";

    [DefaultValue(1)]
    public int Quantity { get; private set; } = 1;

    /// <summary>
    /// Unit price. Cascaded from RoomType.BaseRate when the rate changes.
    /// </summary>
    [CascadeOn<RoomType>(nameof(RoomType.BaseRate))]
    public decimal UnitPrice { get; private set; }

    public decimal TotalPrice { get; private set; }
}
