namespace Showcase.Booking.Reservations.Mutations;

/// <summary>
/// Retires a reservation: the row is marked, not removed, because <see cref="Reservation"/> is
/// <c>[SoftDelete]</c>.
/// </summary>
/// <remarks>
///     It exists as the other half of <see cref="RestoreReservationMutation"/>. Property had the only
///     delete/restore pair in the application, and Property is not owned — so no test could reach the
///     case where a restore has to respect the ownership filter, and the restore that ignored it went
///     unnoticed.
/// </remarks>
[Endpoint(HttpVerb.Delete, "/{id}")]
[EndpointGroup<ReservationsGroup>]
[RequirePermission(BookingPermissions.Reservation.Delete)]
[Mutation(Mode = MutationMode.Delete)]
public partial class RetireReservationMutation : Mutation<Reservation>
{
    public required Guid Id { get; init; }
}
