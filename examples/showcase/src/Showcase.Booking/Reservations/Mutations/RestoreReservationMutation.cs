namespace Showcase.Booking.Reservations.Mutations;

/// <summary>
/// Restores a retired reservation.
/// </summary>
/// <remarks>
///     <see cref="Reservation"/> is both <c>[SoftDelete]</c> and <c>[HasOwner]</c>, which is what
///     makes this the case worth having: a restore lifts the soft-delete filter to find the marked
///     row, and must leave the ownership filter standing so it can only find its own.
/// </remarks>
[Endpoint(HttpVerb.Post, "/{id}/restore")]
[EndpointGroup<ReservationsGroup>]
[RequirePermission(BookingPermissions.Reservation.Update)]
[Mutation(Mode = MutationMode.Restore)]
public partial class RestoreReservationMutation : Mutation<Reservation>
{
    public required Guid Id { get; init; }
}
