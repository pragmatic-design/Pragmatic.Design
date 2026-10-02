using Pragmatic.Caching;
using Pragmatic.Temporal.Clock;
using Warehouse.Stock.Contracts.Events;
using Warehouse.Stock.Infrastructure.Caching;

namespace Warehouse.Stock.Picks.Actions;

/// <summary>
///     The picker takes an order off the shelves: each confirmed hold becomes a pick movement, and the order
///     is announced picked.
/// </summary>
/// <remarks>
///     <para>
///         Only <c>Confirmed</c> holds are picked — an unconfirmed order is not the picker's to take, and
///         an expired one has nothing left. None at all is <see cref="NothingToPickError" />, 409.
///     </para>
///     <para>
///         The movements, the holds and the <see cref="PickList" /> — whose creation raises
///         <c>OrderPicked</c> into the outbox — are one save: the order is announced picked exactly when
///         its stock has left.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(StockPermissions.PickList.Create)]
[Endpoint(HttpVerb.Post, "api/picks")]
public partial class PickOrderAction : DomainAction<Guid, NothingToPickError, NotFoundError>, ICacheInvalidator
{
    private IReadRepository<Product> _products = null!;
    private IRepository<Reservation> _reservations = null!;
    private IRepository<StockLevel> _levels = null!;
    private IRepository<StockMovement> _movements = null!;
    private IRepository<PickList> _picks = null!;
    private readonly ISet<Guid> _moved = new HashSet<Guid>();

    public required Guid OrderId { get; init; }

    [FromClock]
    public DateTimeOffset Now { get; private set; }

    public override async Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var confirmed = await _reservations
            .FindAsync(Spec<Reservation>.Where(r => r.OrderId == OrderId && r.Status == ReservationStatus.Confirmed), ct)
            .ConfigureAwait(false);

        if (confirmed.Count == 0)
            return new NothingToPickError();

        var productIds = confirmed.Select(r => r.ProductId).Distinct().ToList();
        var skus = (await _products.FindAsync(Spec<Product>.Where(p => productIds.Contains(p.PersistenceId)), ct)
                .ConfigureAwait(false))
            .ToDictionary(p => p.PersistenceId, p => p.Sku);

        foreach (var reservation in confirmed)
        {
            var level = await _levels
                .FirstOrDefaultAsync(StockLevelSpecifications.Of(reservation.ProductId, reservation.LocationId), ct)
                .ConfigureAwait(false);
            if (level is null)
                return NotFoundError.Create("StockLevel", $"{reservation.ProductId}/{reservation.LocationId}");

            var picked = reservation.TransitionTo(ReservationStatus.Picked);
            if (picked.IsFailure)
                return Result<Guid, IError>.Failure(picked.Error);

            _movements.Add(level.Pick(reservation.Quantity));
            _moved.Add(reservation.ProductId);
        }

        // One line per SKU, however many locations it was held at: the event is about what the order gets.
        var lines = confirmed
            .GroupBy(r => skus[r.ProductId])
            .Select(g => new PickedLine(g.Key, g.Sum(r => r.Quantity)))
            .ToList();

        var pick = PickList.Of(OrderId, lines, Now);
        _picks.Add(pick);
        return pick.PersistenceId;
    }

    /// <summary>Drops the availability of the products this pick took off the shelves, and of no other.</summary>
    public ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default)
        => AvailabilityCache.DropAsync(cache, _moved, ct);
}
