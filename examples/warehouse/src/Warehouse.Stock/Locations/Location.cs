namespace Warehouse.Stock.Entities;

/// <summary>
///     A place in the warehouse that holds stock: a bay, a shelf, a cage.
/// </summary>
[Entity]
public partial class Location : IEntity
{
    /// <summary>What is painted on the rack: <c>A-01-03</c>.</summary>
    [LogicKey]
    [Required]
    [MaxLength(20)]
    public string Code { get; private set; } = "";

    [MaxLength(120)]
    public string? Description { get; private set; }
}
