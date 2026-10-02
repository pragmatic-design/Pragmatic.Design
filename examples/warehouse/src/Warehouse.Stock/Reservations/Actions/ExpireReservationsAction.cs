using Pragmatic.Caching;
using Warehouse.Stock.Infrastructure.Caching;

namespace Warehouse.Stock.Reservations.Actions;

/// <summary>
///     Gives back what is still held for an order: each hold becomes <c>Expired</c>, its level's
///     <c>Reserved</c> goes down, and each one says so to Orders through the outbox.
/// </summary>
/// <remarks>
///     <para>
///         Run by <c>ExpireReservationsJob</c> when the hold's time is up. Only what is still
///         <c>Held</c> is read: a hold the order's confirmation made permanent is not given back, and a
///         second run of the job finds nothing and changes nothing. That is what makes the job safe to
///         run twice — the answer is the number given back, and zero is a legitimate one.
///     </para>
///     <para>
///         No <c>[Endpoint]</c>: nobody gives stock back by calling this service's API.
///     </para>
///     <para>
///         The cache is dropped for the products whose holds were given back, so an order confirmed in time
///         — the usual case — drops nothing when its job runs (<see cref="AvailabilityCache" />).
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(StockPermissions.Reservation.Update)]
public partial class ExpireReservationsAction : DomainAction<int, NotFoundError>, ICacheInvalidator
{
    private IRepository<Reservation> _reservations = null!;
    private IRepository<StockLevel> _levels = null!;
    private readonly ISet<Guid> _moved = new HashSet<Guid>();

    public required Guid OrderId { get; init; }

    public override async Task<Result<int, IError>> Execute(CancellationToken ct = default)
    {
        var held = await _reservations
            .FindAsync(Spec<Reservation>.Where(r => r.OrderId == OrderId && r.Status == ReservationStatus.Held), ct)
            .ConfigureAwait(false);

        foreach (var reservation in held)
        {
            var level = await _levels
                .FirstOrDefaultAsync(StockLevelSpecifications.Of(reservation.ProductId, reservation.LocationId), ct)
                .ConfigureAwait(false);

            // A hold is made on a level, so the level is there; a hold without one is a row this service
            // did not write, and giving back "nothing" would hide it.
            if (level is null)
                return NotFoundError.Create("StockLevel", $"{reservation.ProductId}/{reservation.LocationId}");

            var given = reservation.Expire(level);
            if (given.IsFailure)
                return Result<int, IError>.Failure(given.Error);

            _moved.Add(reservation.ProductId);
        }

        return held.Count;
    }

    /// <inheritdoc />
    public ValueTask InvalidateAsync(ICacheStack cache, CancellationToken ct = default)
        => AvailabilityCache.DropAsync(cache, _moved, ct);
}
