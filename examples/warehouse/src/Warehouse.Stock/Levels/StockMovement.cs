namespace Warehouse.Stock.Entities;

/// <summary>
///     One change of one stock level, and why: the record a level is the sum of.
/// </summary>
/// <remarks>
///     Written only by <see cref="StockLevel" />, in the transaction that changes the level, and never
///     updated afterwards: a wrong movement is corrected by another one.
/// </remarks>
[Entity]
[Auditable]
[Relation.ManyToOne<Product>]
[Relation.ManyToOne<Location>]
public partial class StockMovement : IEntity
{
    public MovementKind Kind { get; private set; }

    /// <summary>Positive when stock came in, negative when it went out.</summary>
    public int Quantity { get; private set; }

    [MaxLength(200)]
    public string? Reason { get; private set; }

    internal static StockMovement Of(StockLevel level, MovementKind kind, int quantity, string? reason)
    {
        var movement = Create();
        movement.SetProductId(level.ProductId);
        movement.SetLocationId(level.LocationId);
        movement.Kind = kind;
        movement.Quantity = quantity;
        movement.Reason = reason;
        return movement;
    }
}
