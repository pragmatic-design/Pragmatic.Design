namespace Warehouse.Shipping.Entities;

/// <summary>One line of a shipment: a product by SKU, and how many are packed.</summary>
[Entity]
[PartOf<Shipment>]
[Relation.ManyToOne<Shipment>]
public partial class ShipmentLine : IEntity
{
    [Required]
    [MaxLength(40)]
    public string Sku { get; private set; } = "";

    [GreaterThan(0)]
    public int Quantity { get; private set; }

    internal static ShipmentLine Of(string sku, int quantity)
    {
        var line = Create();
        line.Sku = sku;
        line.Quantity = quantity;
        return line;
    }
}
