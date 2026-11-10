using Showcase.Accounts.Entities;

namespace Showcase.Booking.Reservations.Queries;

/// <summary>
///     The caller's own place on the rota.
/// </summary>
/// <remarks>
///     The user lives in Accounts and the rota here: <see cref="StaffId" /> is filled with the <see cref="AppUser" /> that Accounts'
///     generated resolver finds for the request's identity, so no id travels on the request and nobody can ask for another's rota.
/// </remarks>
[Query<StaffAssignment, StaffAssignmentDto>]
[RequirePermission(BookingPermissions.Reservation.Read)]
[Endpoint(HttpVerb.Get, "api/staff-assignments/mine")]
public partial class GetMyStaffAssignmentsQuery
{
    [FromCurrentUser(nameof(AppUser.Id))]
    public Guid StaffId { get; private set; }
}
