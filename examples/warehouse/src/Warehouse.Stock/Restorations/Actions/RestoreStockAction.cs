using Pragmatic.Caching;
using Pragmatic.Temporal.Clock;
using Warehouse.Stock.Infrastructure.Caching;

namespace Warehouse.Stock.Restorations.Actions;

/// <summary>
///     Undoes Stock's part of a cancelled order, from Stock's own state: what is still held is released,
///     what was picked goes back to its shelf. The answer is whether this call did it.
/// </summary>
/// <remarks>
///     <para>
///         How far the order got is read here, not told: a hold still <c>Held</c> or <c>Confirmed</c> is
///         released, one <c>Picked</c> is returned with a receipt movement to the level it left, and one
///         that expired has nothing left to give. Orders does not know which of those Stock is in, and
///         does not need to.
///     </para>
///     <para>
///         Once per order: a <see cref="StockRestoration" /> already there answers <c>false</c> and writes
///         nothing, so a cancellation delivered twice is applied once. No <c>[Endpoint]</c>; run by
///         <c>RestoreTheStockOfACancelledOrder</c>.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(StockPermissions.StockRestoration.Create)]
public partial class RestoreStockAction : DomainAction<bool, NotFoundError>, ICacheInvalidator
{
    private IRepository<StockRestoration> _restorations = null!;
    private IRepository<Reservation> _reservations = null!;
    private IRepository<StockLevel> _levels = null!;
    private IRepository<StockMovement> _movements = null!;
    private readonly ISet<Guid> _moved = new HashSet<Guid>();

    public required Guid OrderId { get; init; }

    [FromClock]
    public DateTimeOffset Now { get; private set; }

    public override async Task<Result<bool, IError>> Execute(CancellationToken ct = default)
    {
        if (await _restorations.ExistsAsync(Spec<StockRestoration>.Where(r => r.OrderId == OrderId), ct).ConfigureAwait(false))
            return false;

        var undone = await _reservations
            .FindAsync(Spec<Reservation>.Where(r => r.OrderId == OrderId
                && (r.Status == ReservationStatus.Held || r.Status == ReservationStatus.Confirmed
                    || r.Status == ReservationStatus.Picked)), ct)
            .ConfigureAwait(false);

        foreach (var reservation in undone)
        {
            var level = await _levels
                .FirstOrDefaultAsync(StockLevelSpecifications.Of(reservation.ProductId, reservation.LocationId), ct)
                .ConfigureAwait(false);
            if (level is null)
                return NotFoundError.Create("StockLevel", $"{reservation.ProductId}/{reservation.LocationId}");

            if (reservation.Status == ReservationStatus.Picked)
            {
                var returned = reservation.TransitionTo(ReservationStatus.Returned);
                if (returned.IsFailure)
                    return Result<bool, IError>.Failure(returned.Error);
                _movements.Add(level.Return(reservation.Quantity));
            }
            else
            {
                var released = reservation.TransitionTo(ReservationStatus.Released);
                if (released.IsFailure)
                    return Result<bool, IError>.Failure(released.Error);
                level.Release(reservation.Quantity);
            }

            _moved.Add(reservation.ProductId);
        }

        _restorations.Add(StockRestoration.Of(OrderId, Now));
        return true;
    }

    /// <summary>Drops the availability of the products this restoration gave back, and of no other.</summary>
    public ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default)
        => AvailabilityCache.DropAsync(cache, _moved, ct);
}
