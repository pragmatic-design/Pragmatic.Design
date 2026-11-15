using Pragmatic.FeatureFlags;
using Showcase.Booking.Infrastructure.FeatureFlags;

namespace Showcase.Booking.Reservations.Queries;

/// <summary>
/// The caller's reservations, for the early check-in desk — a desk only the hotels with early check-in have.
/// Demonstrates: [RequiresFeature&lt;TFlag&gt;] (#747), which answers 404 to a tenant whose flag is off.
/// </summary>
/// <remarks>
///     <see cref="EarlyCheckInFlag" /> targets the <c>premium-hotel</c> tenant, so the route exists for that
///     tenant and for no other: the generated invoker asks <c>IFeatureFlags</c> before the read.
/// </remarks>
[Query<Reservation, ReservationSummaryDto>]
[RequirePermission(BookingPermissions.Reservation.Read)]
[RequiresFeature<EarlyCheckInFlag>]
[Endpoint(HttpVerb.Get, "api/reservations/early-check-in")]
public partial class EarlyCheckInReservationsQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
