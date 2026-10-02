using Showcase.Accounts.Entities;

namespace Showcase.Booking.Reservations.Queries;

/// <summary>
/// The rota, with the name of whoever is on it.
/// </summary>
/// <remarks>
///     <para>
///         The one read in the Showcase that no other declaration can express.
///         <c>StaffAssignment.StaffId</c> is written from a token and carries no <c>[Relation]</c>, so
///         there is no navigation: <c>[EagerLoad]</c> has nothing to load and
///         <c>[GenerateProjection]</c> has nothing to flatten across. The join is declared by key, and
///         the generated step reads both sources and builds the result from them.
///     </para>
///     <para>
///         ⚠️ <c>Left</c>, and it is not decoration: with <c>Inner</c> an assignment whose
///         <c>StaffId</c> matches no user disappears from the rota — which is the normal case here,
///         since the id is not a foreign key and nothing guarantees an account exists. The
///         integration test measures exactly that difference.
///     </para>
///     <para>
///         ⚠️ <c>Alias = "Staff"</c> is what makes <c>StaffDisplayName</c> resolve to
///         <c>AppUser.DisplayName</c>: the prefix says which side a result property comes from, which
///         is also how two joins to the same type would be told apart.
///     </para>
/// </remarks>
[Query<StaffAssignment, StaffAssignmentWithStaffDto>]
[Join<AppUser>(ForeignKey = "StaffId", TargetKey = "Id", Type = JoinType.Left, Alias = "Staff")]
[RequirePermission(BookingPermissions.Reservation.Read)]
[Endpoint(HttpVerb.Get, "api/staff-assignments")]
public partial class GetStaffAssignmentsQuery
{
    /// <summary>Only the rota of one property, when asked.</summary>
    [Filter]
    public Guid? PropertyId { get; init; }

    /// <summary>Only one role, when asked.</summary>
    [Filter]
    public string? Role { get; init; }
}
