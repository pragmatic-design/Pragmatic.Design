namespace Warehouse.Stock.Infrastructure.FeatureFlags;

/// <summary>
///     Whether an order short of stock is accepted, holding what there is and backordering the rest.
///     Off unless switched on: an order is then held whole or refused (<c>ReserveStockAction</c>).
/// </summary>
/// <remarks>
///     Switched through the Agent (<c>flags/AcceptBackorders</c>), so every Stock instance reads the same
///     answer — whichever one the reservation request reaches.
/// </remarks>
public sealed class AcceptBackordersFlag : IFeatureFlag
{
    public static string Name => "AcceptBackorders";
    public static string? Description => "Accept an order short of stock, backordering the missing quantity";
}
