namespace Warehouse.Stock.Levels.Queries;

/// <summary>
///     A product's availability: its level at every location that holds it — on hand, reserved, and what
///     is left to promise.
/// </summary>
/// <remarks>
///     <para>
///         Cached, because every order asks it and it changes only with a movement: every operation that
///         moves stock — receipt, adjustment, hold, expiry, pick, restoration — drops the
///         <c>availability</c> tag. The total is the sum of the rows; the rows are what a picker needs to
///         know where to go.
///     </para>
///     <para>
///         ⚠️ Each instance keeps its own copy. With Stock running twice, a movement on one instance drops
///         that instance's copy, and the host's Redis broadcast drops the other's — without it the other
///         answers the old numbers until the entry expires (measured, <c>TwoInstancesAgreeOnAvailability</c>).
///         The drop crosses Redis, so it lands on the other instance a moment after the save, not with it.
///     </para>
/// </remarks>
[Query<StockLevel, StockLevelDto>]
[Cacheable(Duration = "5m", Tags = ["availability"])]
[RequirePermission(StockPermissions.StockLevel.Read)]
[Endpoint(HttpVerb.Get, "api/levels")]
public partial class GetStockLevelsQuery
{
    [Filter]
    public Guid? ProductId { get; init; }
}
