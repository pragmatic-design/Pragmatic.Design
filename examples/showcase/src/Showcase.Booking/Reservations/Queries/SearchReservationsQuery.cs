using Pragmatic.Authorization.Policy;

namespace Showcase.Booking.Reservations.Queries;

/// <summary>
/// Paged search for reservations.
/// Demonstrates: [Query] + [Endpoint] unified combo, and a resource policy on a query.
/// </summary>
/// <remarks>
///     <para>
///         No <c>[LoadWith]</c>: this query projects to a DTO, and a projection resolves
///         <c>Property.Name</c> and <c>Guest.FirstName</c> in SQL — there is nothing left to include.
///         A <c>[LoadWith]</c> here would ask for every navigation at depth 1 rather than the two the
///         DTO reads — four, counting the ones <c>[Relation.*]</c> generates — and PRAG0716 would
///         report it.
///     </para>
///     The policy is the only example of one on a <b>query</b>, and it is here so that the code the
///     generator emits for that case is compiled by something: the branch is emitted only when a query
///     declares a policy, and a mistake in a branch nothing compiles ships with every generator test
///     green.
/// </remarks>
[Query<Reservation, ReservationSummaryDto>]
[RequirePermission(BookingPermissions.Reservation.Read)]
[RequirePolicy<Infrastructure.Authorization.ReservationSearchPolicy>]
[Endpoint(HttpVerb.Get, "api/reservations/search")]
public partial class SearchReservationsQuery
{
    [Filter]
    public Guid? GuestId { get; init; }

    [Filter]
    public Guid? PropertyId { get; init; }

    [Filter]
    public ReservationStatus? Status { get; init; }

    [Filter(Operator = FilterOperator.GreaterOrEqual, MapTo = "CheckIn")]
    public DateTimeOffset? FromDate { get; init; }

    [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "CheckOut")]
    public DateTimeOffset? ToDate { get; init; }

    [Sort(DefaultDirection = SortDirection.Descending)]
    public SortDirection? CheckInSort { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
