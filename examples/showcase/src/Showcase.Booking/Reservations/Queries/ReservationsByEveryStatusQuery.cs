namespace Showcase.Booking.Reservations.Queries;

/// <summary>
/// The reservation book by every status: the same read as <see cref="ReservationsByStatusQuery" />, answered by a view that includes the
/// statuses no reservation is in (#766).
/// </summary>
[Query<Reservation, ReservationsByEveryStatusView>]
[RequirePermission(BookingPermissions.Reservation.Read)]
[Endpoint(HttpVerb.Get, "api/reservations/by-every-status")]
public partial class ReservationsByEveryStatusQuery
{
    /// <summary>Count only the reservations of one property, when asked.</summary>
    [Filter]
    public Guid? PropertyId { get; init; }
}
