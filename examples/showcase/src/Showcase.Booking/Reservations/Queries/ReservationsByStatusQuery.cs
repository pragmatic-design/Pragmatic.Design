namespace Showcase.Booking.Reservations.Queries;

/// <summary>
/// The reservation book by status, as an ordinary declared read.
/// Demonstrates: an aggregate query — the result type declares the grouping, the query declares the
/// filters, and the route, permission and invoker are the ones every other query gets.
/// </summary>
/// <remarks>
///     ⚠️ Before this pairing existed, an aggregate stopped one step short of HTTP: a
///     <c>[QueryView]</c> generates a <c>Build</c> and nothing else — no route, no entry in the
///     published contract — so exposing one meant a hand-written action holding a repository and
///     writing the grouping again in LINQ. Two modules of the consumer had done exactly that.
/// </remarks>
[Query<Reservation, ReservationsByStatusView>]
[RequirePermission(BookingPermissions.Reservation.Read)]
[Endpoint(HttpVerb.Get, "api/reservations/by-status")]
public partial class ReservationsByStatusQuery
{
    /// <summary>Count only the reservations of one property, when asked.</summary>
    [Filter]
    public Guid? PropertyId { get; init; }
}
