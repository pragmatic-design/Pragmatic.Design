using Pragmatic.Persistence.Query.Filters;

namespace Showcase.Booking.Reservations.Queries;

/// <summary>
/// The caller's reservations, the retired (soft-deleted) ones among them.
/// Demonstrates: [WithoutFilter&lt;T&gt;(SoftDeleteOnly = true)], which lifts the soft-delete filter and keeps the others (#732).
/// </summary>
/// <remarks>
///     A reservation is <c>[SoftDelete]</c> and <c>[HasOwner]</c>: lifting every filter, as <c>[WithoutFilter&lt;Reservation&gt;]</c>
///     does, would hand the caller other owners' retired rows too. This lifts the soft-delete filter only, so the caller reads exactly the
///     rows they would have read alive.
/// </remarks>
[Query<Reservation, ReservationSummaryDto>]
[RequirePermission(BookingPermissions.Reservation.Read)]
[WithoutFilter<Reservation>(SoftDeleteOnly = true)]
[Endpoint(HttpVerb.Get, "api/reservations/with-retired")]
public partial class SearchReservationsWithRetiredQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
