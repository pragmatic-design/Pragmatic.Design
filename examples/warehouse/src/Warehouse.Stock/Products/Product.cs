namespace Warehouse.Stock.Entities;

/// <summary>
///     Something the warehouse keeps: identified by its SKU, named in each language it is sold in.
/// </summary>
/// <remarks>
///     The name is content, not a message: whoever manages the catalogue writes it in each language, and
///     a reader gets it in theirs. The SKU is how every other service names the product — an order line
///     carries it, never this row's id.
/// </remarks>
[Entity]
public partial class Product : IEntity
{
    /// <summary>The stock-keeping unit: unique, and the product's name everywhere outside this service.</summary>
    [LogicKey]
    [Required]
    [MaxLength(40)]
    public string Sku { get; private set; } = "";

    public LocalizedString Name { get; private set; } = new();

    /// <summary>Below this many on hand, the product is due for reordering.</summary>
    [GreaterThanOrEqual(0)]
    public int ReorderThreshold { get; private set; }
}
