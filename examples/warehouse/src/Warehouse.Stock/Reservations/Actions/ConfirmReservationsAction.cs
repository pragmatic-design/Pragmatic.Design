namespace Warehouse.Stock.Reservations.Actions;

/// <summary>
///     Makes an order's holds permanent: the order was confirmed, so its stock is no longer given back
///     when the hold's time is up.
/// </summary>
/// <remarks>
///     Run when Orders publishes <c>OrderReadyToPick</c>. Only what is still <c>Held</c> is confirmed, so
///     a second delivery finds nothing and changes nothing; the answer is how many were confirmed.
///     ⚠️ A confirmation that arrives after the hold expired finds nothing too: the stock was given back,
///     and Orders has been told so. The two services disagree for as long as that message takes, and the
///     order's side decides — see <c>ExpireOrderMutation</c> in Orders.
/// </remarks>
[DomainAction]
[RequirePermission(StockPermissions.Reservation.Update)]
public partial class ConfirmReservationsAction : DomainAction<int>
{
    private IRepository<Reservation> _reservations = null!;

    public required Guid OrderId { get; init; }

    public override async Task<Result<int, IError>> Execute(CancellationToken ct = default)
    {
        var held = await _reservations
            .FindAsync(Spec<Reservation>.Where(r => r.OrderId == OrderId && r.Status == ReservationStatus.Held), ct)
            .ConfigureAwait(false);

        foreach (var reservation in held)
        {
            var confirmed = reservation.TransitionTo(ReservationStatus.Confirmed);
            if (confirmed.IsFailure)
                return Result<int, IError>.Failure(confirmed.Error);
        }

        return held.Count;
    }
}
