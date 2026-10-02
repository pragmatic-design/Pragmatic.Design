using Warehouse.Stock.Infrastructure.Caching;

namespace Warehouse.Stock.Levels.Actions;

/// <summary>
///     A count found a difference: the level moves by the difference, with the reason written down.
/// </summary>
/// <remarks>
///     <para>
///         A permission of its own and not the receipt's: correcting what the system believes is on the
///         shelf is how stock goes missing without a trace, so it is the stock manager's and it always
///         carries a reason.
///     </para>
///     <para>
///         An adjustment that would leave less than nothing is refused with
///         <see cref="StockWouldGoNegativeError" /> and changes nothing. A product never received at the
///         location has nothing to adjust, which is the same refusal: there is no level below zero.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(StockPermissions.StockLevel.Adjust)]
[LoadEntity<Product>(nameof(ProductId))]
[LoadEntity<Location>(nameof(LocationId))]
[InvalidatesCache(AvailabilityCache.OfProductTemplate, AvailabilityCache.Unfiltered)]
[Endpoint(HttpVerb.Post, "api/levels/adjustments")]
public partial class AdjustStockAction : DomainAction<StockLevelDto, NotFoundError, StockWouldGoNegativeError>
{
    private IRepository<StockLevel> _levels = null!;
    private IRepository<StockMovement> _movements = null!;

    public required Guid ProductId { get; init; }

    public required Guid LocationId { get; init; }

    /// <summary>How far the level moves: negative for a loss, positive for a find.</summary>
    public required int Delta { get; init; }

    [Required]
    [MaxLength(200)]
    public required string Reason { get; init; }

    public override async Task<Result<StockLevelDto, IError>> Execute(CancellationToken ct = default)
    {
        var level = await _levels
            .FirstOrDefaultAsync(StockLevelSpecifications.Of(ProductId, LocationId), ct)
            .ConfigureAwait(false);

        var isNew = level is null;
        level ??= StockLevel.Empty(ProductId, LocationId);

        var movement = level.Adjust(Delta, Reason);
        if (movement.IsFailure)
            return Result<StockLevelDto, IError>.Failure(movement.Error);

        if (isNew)
            _levels.Add(level);
        _movements.Add(movement.Value);

        return StockLevelDto.FromEntity(level);
    }
}
