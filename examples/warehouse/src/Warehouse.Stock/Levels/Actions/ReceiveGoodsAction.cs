using Warehouse.Stock.Infrastructure.Caching;

namespace Warehouse.Stock.Levels.Actions;

/// <summary>
///     Goods arrived at a location: the level rises by what arrived, and a receipt says so.
/// </summary>
/// <remarks>
///     <para>
///         The product and the location are loaded by the invoker before <c>Execute</c>
///         (<c>[LoadEntity]</c>): either unknown is a 404, and <c>Execute</c> never sees it missing. A
///         quantity that is not positive is refused before anything is loaded.
///     </para>
///     <para>
///         The first receipt of a product at a location creates its level. The level and the movement are
///         written in one transaction, which is what "a level changes only through a movement" rests on.
///     </para>
///     <para>
///         The availability read (<see cref="Queries.GetStockLevelsQuery" />) is cached: this drops the
///         product's entry and the unfiltered one, and no other product's.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(StockPermissions.StockLevel.Receive)]
[LoadEntity<Product>(nameof(ProductId))]
[LoadEntity<Location>(nameof(LocationId))]
[InvalidatesCache(AvailabilityCache.OfProductTemplate, AvailabilityCache.Unfiltered)]
[Endpoint(HttpVerb.Post, "api/levels/receipts")]
public partial class ReceiveGoodsAction : DomainAction<StockLevelDto, NotFoundError>
{
    private IRepository<StockLevel> _levels = null!;
    private IRepository<StockMovement> _movements = null!;

    public required Guid ProductId { get; init; }

    public required Guid LocationId { get; init; }

    [GreaterThan(0)]
    public required int Quantity { get; init; }

    public override async Task<Result<StockLevelDto, IError>> Execute(CancellationToken ct = default)
    {
        var level = await _levels
            .FirstOrDefaultAsync(StockLevelSpecifications.Of(ProductId, LocationId), ct)
            .ConfigureAwait(false);

        if (level is null)
        {
            level = StockLevel.Empty(ProductId, LocationId);
            _levels.Add(level);
        }

        _movements.Add(level.Receive(Quantity));
        return StockLevelDto.FromEntity(level);
    }
}
